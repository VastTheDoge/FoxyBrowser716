using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using FoxyBrowser716.DataObjects.Complex;
using FoxyBrowser716.ErrorHandeler;
using Microsoft.Web.WebView2.Core;

namespace FoxyBrowser716.DataManagement;

/// <summary>
/// The download list behind the themed downloads panel. One per instance (persisted to Downloads.json).
/// WebView2 still performs the download; this only tracks the
/// <see cref="CoreWebView2DownloadOperation"/> and exposes pause/resume/cancel/open.
/// <see cref="Items"/> is only touched on the UI thread (WebView2 raises download events there).
/// </summary>
public sealed class DownloadManager
{
	private readonly FoxyAutoSaverLockedList<Download> _store;

	public ObservableCollection<Download> Items { get; } = [];

	public event Action<Download>? DownloadStarted;
	public event Action<Download>? DownloadCompleted;

	internal IFoxyAutoSaverItem SaverItem => _store;

	public DownloadManager(string instanceName)
	{
		_store = new FoxyAutoSaverLockedList<Download>("Downloads.json", FoxyFileManager.FolderType.Data, instanceName);
	}

	/// <summary>Call once the store has loaded. Downloads that were running when the browser closed cannot resume.</summary>
	internal void Initialize()
	{
		foreach (var download in _store.Snapshot().OrderByDescending(d => d.StartedAt))
		{
			if (download.Status is DownloadStatus.InProgress or DownloadStatus.Paused)
			{
				download.Status = DownloadStatus.Failed;
				download.FailureReason = "Interrupted when the browser closed";
			}
			download.PropertyChanged += OnDownloadPropertyChanged;
			Items.Add(download);
		}
		SyncStore();
	}

	public Download Track(CoreWebView2DownloadOperation operation, string resultFilePath)
	{
		var download = new Download
		{
			Url = operation.Uri,
			FilePath = resultFilePath,
			TotalBytes = operation.TotalBytesToReceive,
			ReceivedBytes = operation.BytesReceived,
			Status = DownloadStatus.InProgress,
			StartedAt = DateTime.Now,
			Operation = operation,
		};

		operation.BytesReceivedChanged += (op, _) =>
		{
			download.ReceivedBytes = op.BytesReceived;
			download.TotalBytes = op.TotalBytesToReceive;
		};
		operation.EstimatedEndTimeChanged += (op, _) =>
		{
			download.EstimatedEnd = DateTime.TryParse(op.EstimatedEndTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end)
				? end.ToLocalTime()
				: null;
		};
		operation.StateChanged += (op, _) => UpdateState(download, op);

		download.PropertyChanged += OnDownloadPropertyChanged;
		Items.Insert(0, download);
		SyncStore();

		DownloadStarted?.Invoke(download);
		return download;
	}

	private void UpdateState(Download download, CoreWebView2DownloadOperation op)
	{
		if (!string.IsNullOrEmpty(op.ResultFilePath))
			download.FilePath = op.ResultFilePath;
		download.ReceivedBytes = op.BytesReceived;
		download.TotalBytes = op.TotalBytesToReceive;

		switch (op.State)
		{
			case CoreWebView2DownloadState.InProgress:
				download.Status = DownloadStatus.InProgress;
				download.FailureReason = null;
				break;
			case CoreWebView2DownloadState.Completed:
				download.Status = DownloadStatus.Completed;
				download.CompletedAt = DateTime.Now;
				download.EstimatedEnd = null;
				download.Operation = null;
				DownloadCompleted?.Invoke(download);
				break;
			case CoreWebView2DownloadState.Interrupted:
				download.EstimatedEnd = null;
				switch (op.InterruptReason)
				{
					case CoreWebView2DownloadInterruptReason.UserPaused:
						download.Status = DownloadStatus.Paused;
						break;
					case CoreWebView2DownloadInterruptReason.UserCanceled:
						download.Status = DownloadStatus.Cancelled;
						download.Operation = null;
						break;
					default:
						download.Status = DownloadStatus.Failed;
						download.FailureReason = DescribeInterruptReason(op.InterruptReason);
						if (!op.CanResume) download.Operation = null;
						break;
				}
				break;
		}
	}

	public static void Pause(Download download)
	{
		try { download.Operation?.Pause(); }
		catch (Exception e) { FoxyLogger.AddError(e); }
	}

	public static void Resume(Download download)
	{
		try
		{
			if (download.Operation is { CanResume: true } op) op.Resume();
		}
		catch (Exception e) { FoxyLogger.AddError(e); }
	}

	public static void Cancel(Download download)
	{
		try { download.Operation?.Cancel(); }
		catch (Exception e) { FoxyLogger.AddError(e); }
	}

	public void Remove(Download download)
	{
		if (download.IsActive) Cancel(download);
		download.PropertyChanged -= OnDownloadPropertyChanged;
		Items.Remove(download);
		SyncStore();
	}

	/// <summary>Removes everything that is not still downloading.</summary>
	public void ClearFinished()
	{
		foreach (var download in Items.Where(d => !d.IsActive).ToList())
		{
			download.PropertyChanged -= OnDownloadPropertyChanged;
			Items.Remove(download);
		}
		SyncStore();
	}

	public static bool FileExists(Download download) =>
		!string.IsNullOrEmpty(download.FilePath) && File.Exists(download.FilePath);

	public static void OpenFile(Download download)
	{
		if (!FileExists(download)) return;
		try { Process.Start(new ProcessStartInfo(download.FilePath) { UseShellExecute = true }); }
		catch (Exception e) { FoxyLogger.AddError(e); }
	}

	public static void ShowInFolder(Download download)
	{
		try
		{
			if (FileExists(download))
				Process.Start("explorer.exe", $"/select,\"{download.FilePath}\"");
			else if (Path.GetDirectoryName(download.FilePath) is { Length: > 0 } dir && Directory.Exists(dir))
				Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
		}
		catch (Exception e) { FoxyLogger.AddError(e); }
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out string pszPath);

	private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

	/// <summary>The user's Downloads known folder (it can be moved, so it is not always %USERPROFILE%\Downloads).</summary>
	public static string? GetSystemDownloadsFolder()
	{
		try
		{
			if (SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero, out var path) == 0)
				return path;
		}
		catch (Exception e) { FoxyLogger.AddError(e); }

		var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
		return Directory.Exists(fallback) ? fallback : null;
	}

	public static string FormatBytes(long bytes)
	{
		if (bytes < 0) return "?";
		string[] units = ["B", "KB", "MB", "GB", "TB"];
		double value = bytes;
		var unit = 0;
		while (value >= 1024 && unit < units.Length - 1)
		{
			value /= 1024;
			unit++;
		}
		return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
	}

	public static string DescribeInterruptReason(CoreWebView2DownloadInterruptReason reason) => reason switch
	{
		CoreWebView2DownloadInterruptReason.FileAccessDenied => "Access denied",
		CoreWebView2DownloadInterruptReason.FileNoSpace => "Disk full",
		CoreWebView2DownloadInterruptReason.FileNameTooLong => "File name too long",
		CoreWebView2DownloadInterruptReason.FileTooLarge => "File too large",
		CoreWebView2DownloadInterruptReason.FileMalicious => "Blocked: file may be harmful",
		CoreWebView2DownloadInterruptReason.FileBlockedByPolicy => "Blocked by policy",
		CoreWebView2DownloadInterruptReason.FileSecurityCheckFailed => "Security check failed",
		CoreWebView2DownloadInterruptReason.NetworkFailed or CoreWebView2DownloadInterruptReason.NetworkDisconnected => "Network error",
		CoreWebView2DownloadInterruptReason.NetworkTimeout => "Network timed out",
		CoreWebView2DownloadInterruptReason.ServerUnauthorized or CoreWebView2DownloadInterruptReason.ServerForbidden => "Server refused the download",
		CoreWebView2DownloadInterruptReason.ServerFailed or CoreWebView2DownloadInterruptReason.ServerBadContent => "Server error",
		CoreWebView2DownloadInterruptReason.UserShutdown => "Browser closed",
		CoreWebView2DownloadInterruptReason.DownloadProcessCrashed => "Download process crashed",
		_ => "Failed",
	};

	private void OnDownloadPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		// progress ticks only change fields of existing items, so a save request is enough (no list copy)
		_store.RequestSave(null);
	}

	private void SyncStore() => _store.Mutate(list =>
	{
		list.Clear();
		list.AddRange(Items);
	});
}

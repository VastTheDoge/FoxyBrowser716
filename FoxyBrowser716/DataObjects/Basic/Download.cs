using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;

namespace FoxyBrowser716.DataObjects.Basic;

public enum DownloadStatus
{
	InProgress,
	Paused,
	Completed,
	Failed,
	Cancelled,
}

/// <summary>
/// A download shown in the downloads panel. The persisted fields survive restarts; <see cref="Operation"/> only
/// exists while WebView2 is still running the download in this session.
/// </summary>
public partial class Download : ObservableObject
{
	[ObservableProperty] public partial string Url { get; set; } = string.Empty;

	[NotifyPropertyChangedFor(nameof(FileName))]
	[ObservableProperty] public partial string FilePath { get; set; } = string.Empty;

	[ObservableProperty] public partial long TotalBytes { get; set; }
	[ObservableProperty] public partial long ReceivedBytes { get; set; }

	[NotifyPropertyChangedFor(nameof(IsActive))]
	[ObservableProperty] public partial DownloadStatus Status { get; set; }

	[ObservableProperty] public partial string? FailureReason { get; set; }
	[ObservableProperty] public partial DateTime StartedAt { get; set; }
	[ObservableProperty] public partial DateTime? CompletedAt { get; set; }

	[JsonIgnore] [ObservableProperty] public partial DateTime? EstimatedEnd { get; set; }

	[JsonIgnore] public string FileName => string.IsNullOrEmpty(FilePath) ? Url : Path.GetFileName(FilePath);

	/// <summary>Still running (or paused) in this session, so pause/resume/cancel apply.</summary>
	[JsonIgnore] public bool IsActive => Operation is not null && Status is DownloadStatus.InProgress or DownloadStatus.Paused;

	[JsonIgnore] public CoreWebView2DownloadOperation? Operation { get; internal set; }
}

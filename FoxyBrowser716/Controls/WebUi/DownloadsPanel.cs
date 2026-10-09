using System.ComponentModel;
using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.DataManagement;
using FoxyBrowser716.DataObjects.Settings;
using Material.Icons.WinUI3;
using Windows.ApplicationModel.DataTransfer;

namespace FoxyBrowser716.Controls.WebUi;

/// <summary>Themed replacement for WebView2's download flyout: the list from a <see cref="DownloadManager"/>.</summary>
public sealed partial class DownloadsPanel : ThemedUserControl
{
	private readonly DownloadManager _manager;
	private readonly Func<string> _getDownloadFolder;
	private readonly Border _card;
	private readonly TextBlock _title;
	private readonly TextBlock _emptyText;
	private readonly StackPanel _list;
	private readonly FIconButton _folderButton;
	private readonly FTextButton _clearButton;
	private readonly Dictionary<Download, DownloadRow> _rows = [];

	/// <param name="getDownloadFolder">Folder opened by the folder button (the configured download folder).</param>
	public DownloadsPanel(DownloadManager manager, Func<string> getDownloadFolder)
	{
		_manager = manager;
		_getDownloadFolder = getDownloadFolder;

		_title = WebUiStyle.Text("Downloads", 16, bold: true, wrap: false);
		_folderButton = WebUiStyle.IconButton(MaterialIconKind.FolderOpen, OpenDownloadFolder, 26, 4);
		_clearButton = WebUiStyle.TextButton("Clear list", () => _manager.ClearFinished(), MaterialIconKind.DeleteSweep);

		var header = new Grid
		{
			ColumnSpacing = 4,
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = GridLength.Auto },
			},
		};
		Grid.SetColumn(_folderButton, 1);
		Grid.SetColumn(_clearButton, 2);
		header.Children.Add(_title);
		header.Children.Add(_folderButton);
		header.Children.Add(_clearButton);

		_emptyText = WebUiStyle.Text("No downloads yet.", 13);
		_emptyText.Margin = new Thickness(4, 8, 4, 8);
		_list = new StackPanel { Spacing = 4 };

		var stack = new StackPanel { Spacing = 6 };
		stack.Children.Add(header);
		stack.Children.Add(_emptyText);
		stack.Children.Add(new ScrollViewer
		{
			Content = _list,
			MaxHeight = 460,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
		});

		_card = WebUiStyle.Card(stack, 8);
		Content = _card;

		// only listen while visible so a closed panel does not keep rows alive
		Loaded += (_, _) =>
		{
			_manager.Items.CollectionChanged += ItemsOnCollectionChanged;
			Rebuild();
		};
		Unloaded += (_, _) =>
		{
			_manager.Items.CollectionChanged -= ItemsOnCollectionChanged;
			foreach (var row in _rows.Values) row.Detach();
			_rows.Clear();
			_list.Children.Clear();
		};

		ApplyTheme();
	}

	private void ItemsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

	private void Rebuild()
	{
		foreach (var row in _rows.Values) row.Detach();
		_rows.Clear();
		_list.Children.Clear();

		foreach (var download in _manager.Items)
		{
			var row = new DownloadRow(download, _manager) { CurrentTheme = CurrentTheme };
			_rows[download] = row;
			_list.Children.Add(row);
		}

		_emptyText.Visibility = _manager.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
	}

	private void OpenDownloadFolder()
	{
		var folder = _getDownloadFolder();
		if (!Directory.Exists(folder)) return;
		try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); }
		catch (Exception e) { ErrorHandeler.FoxyLogger.AddError(e); }
	}

	protected override void ApplyTheme()
	{
		if (_card is null) return;

		WebUiStyle.ApplyCardTheme(_card, CurrentTheme, solid: false);
		_title.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		_emptyText.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		_folderButton.CurrentTheme = CurrentTheme;
		_clearButton.CurrentTheme = CurrentTheme;
		WebUiStyle.ThemeIcon(_clearButton.Icon, CurrentTheme.PrimaryForegroundColor);
		foreach (var row in _rows.Values) row.CurrentTheme = CurrentTheme;
	}
}

/// <summary>One download: name, status line, progress and the actions that apply to its state.</summary>
public sealed partial class DownloadRow : ThemedUserControl
{
	private readonly Download _download;
	private readonly DownloadManager _manager;
	private readonly Grid _root;
	private readonly MaterialIcon _fileIcon;
	private readonly TextBlock _name;
	private readonly TextBlock _status;
	private readonly FProgressBar _progress;
	private readonly StackPanel _actions;
	private bool _pointerOver;

	public DownloadRow(Download download, DownloadManager manager)
	{
		_download = download;
		_manager = manager;

		_fileIcon = new MaterialIcon { Kind = MaterialIconKind.File, Width = 22, Height = 22, Margin = new Thickness(2, 0, 4, 0) };
		_name = WebUiStyle.Text(string.Empty, 13, bold: true, wrap: false);
		_status = WebUiStyle.Text(string.Empty, 11, wrap: false);
		_progress = new FProgressBar { Margin = new Thickness(0, 3, 0, 1) };
		_actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Center };

		var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
		text.Children.Add(_name);
		text.Children.Add(_status);
		text.Children.Add(_progress);

		_root = new Grid
		{
			Padding = new Thickness(4),
			CornerRadius = new CornerRadius(6),
			BorderThickness = new Thickness(2),
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
				new ColumnDefinition { Width = GridLength.Auto },
			},
		};
		Grid.SetColumn(text, 1);
		Grid.SetColumn(_actions, 2);
		_root.Children.Add(_fileIcon);
		_root.Children.Add(text);
		_root.Children.Add(_actions);
		Content = _root;

		_root.PointerEntered += (_, _) => { _pointerOver = true; ChangeColorAnimation(_root.Background, CurrentTheme.PrimaryAccentColorSlightTransparent); };
		_root.PointerExited += (_, _) => { _pointerOver = false; ChangeColorAnimation(_root.Background, CurrentTheme.PrimaryBackgroundColorVeryTransparent); };
		_root.DoubleTapped += (_, _) => DownloadManager.OpenFile(_download);

		_download.PropertyChanged += DownloadOnPropertyChanged;
		Update();
		ApplyTheme();
	}

	/// <summary>Stops listening to the download (the panel calls this when rows are discarded).</summary>
	public void Detach() => _download.PropertyChanged -= DownloadOnPropertyChanged;

	private DownloadStatus? _actionsBuiltFor;
	private bool _actionsBuiltWithFile;

	private void DownloadOnPropertyChanged(object? sender, PropertyChangedEventArgs e) => Update();

	private void Update()
	{
		_name.Text = _download.FileName;

		// only completed downloads have a file worth checking (and this runs on every progress tick)
		var fileExists = _download.Status == DownloadStatus.Completed && DownloadManager.FileExists(_download);
		var received = DownloadManager.FormatBytes(_download.ReceivedBytes);
		var total = _download.TotalBytes > 0 ? DownloadManager.FormatBytes(_download.TotalBytes) : null;

		_status.Text = _download.Status switch
		{
			DownloadStatus.InProgress => total is null ? $"{received}" : $"{received} of {total}{FormatRemaining()}",
			DownloadStatus.Paused => total is null ? $"Paused · {received}" : $"Paused · {received} of {total}",
			DownloadStatus.Completed => fileExists
				? $"{DownloadManager.FormatBytes(_download.TotalBytes > 0 ? _download.TotalBytes : _download.ReceivedBytes)} · {WebUiStyle.FormatRelative(_download.CompletedAt ?? _download.StartedAt)}"
				: "File deleted or moved",
			DownloadStatus.Failed => $"Failed: {_download.FailureReason ?? "unknown error"}",
			DownloadStatus.Cancelled => "Cancelled",
			_ => string.Empty,
		};

		var showProgress = _download.Status is DownloadStatus.InProgress or DownloadStatus.Paused;
		_progress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
		_progress.IsMuted = _download.Status == DownloadStatus.Paused;
		_progress.Value = _download.TotalBytes > 0 ? (double)_download.ReceivedBytes / _download.TotalBytes : 0;

		_fileIcon.Kind = _download.Status switch
		{
			DownloadStatus.Completed when fileExists => MaterialIconKind.FileDownload,
			DownloadStatus.Failed or DownloadStatus.Cancelled => MaterialIconKind.AlertCircle,
			DownloadStatus.Completed => MaterialIconKind.LinkOff,
			_ => MaterialIconKind.Download,
		};

		_root.Opacity = _download.Status is DownloadStatus.Cancelled || (_download.Status == DownloadStatus.Completed && !fileExists) ? 0.6 : 1;

		// progress ticks arrive many times a second; only rebuild the buttons when they would change
		if (_actionsBuiltFor != _download.Status || _actionsBuiltWithFile != fileExists)
		{
			_actionsBuiltFor = _download.Status;
			_actionsBuiltWithFile = fileExists;
			BuildActions(fileExists);
		}
	}

	private string FormatRemaining()
	{
		if (_download.EstimatedEnd is not { } end) return string.Empty;
		var left = end - DateTime.Now;
		if (left <= TimeSpan.Zero) return string.Empty;
		return left.TotalMinutes >= 1 ? $" · {(int)Math.Ceiling(left.TotalMinutes)} min left" : $" · {(int)Math.Ceiling(left.TotalSeconds)} s left";
	}

	private void BuildActions(bool fileExists)
	{
		_actions.Children.Clear();

		switch (_download.Status)
		{
			case DownloadStatus.InProgress:
				AddAction(MaterialIconKind.Pause, () => DownloadManager.Pause(_download));
				AddAction(MaterialIconKind.Close, () => DownloadManager.Cancel(_download), danger: true);
				break;
			case DownloadStatus.Paused:
				AddAction(MaterialIconKind.Play, () => DownloadManager.Resume(_download));
				AddAction(MaterialIconKind.Close, () => DownloadManager.Cancel(_download), danger: true);
				break;
			case DownloadStatus.Completed when fileExists:
				AddAction(MaterialIconKind.OpenInNew, () => DownloadManager.OpenFile(_download));
				AddAction(MaterialIconKind.Folder, () => DownloadManager.ShowInFolder(_download));
				AddAction(MaterialIconKind.Close, () => _manager.Remove(_download), danger: true);
				break;
			default:
				if (_download.Status == DownloadStatus.Failed && _download.Operation is { CanResume: true })
					AddAction(MaterialIconKind.Refresh, () => DownloadManager.Resume(_download));
				AddAction(MaterialIconKind.LinkVariant, CopyLink);
				AddAction(MaterialIconKind.Close, () => _manager.Remove(_download), danger: true);
				break;
		}
	}

	private void AddAction(MaterialIconKind kind, Action action, bool danger = false)
	{
		var button = WebUiStyle.IconButton(kind, action, 24, 3);
		button.CurrentTheme = danger ? WebUiStyle.DangerTheme(CurrentTheme) : CurrentTheme;
		button.Tag = danger;
		_actions.Children.Add(button);
	}

	private void CopyLink()
	{
		var package = new DataPackage();
		package.SetText(_download.Url);
		Clipboard.SetContent(package);
	}

	protected override void ApplyTheme()
	{
		if (_root is null) return;

		_root.Background = new SolidColorBrush(_pointerOver ? CurrentTheme.PrimaryAccentColorSlightTransparent : CurrentTheme.PrimaryBackgroundColorVeryTransparent);
		_root.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColor);
		_fileIcon.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		_name.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		_status.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		_progress.CurrentTheme = CurrentTheme;
		foreach (var child in _actions.Children)
			if (child is FIconButton button)
				button.CurrentTheme = button.Tag is true ? WebUiStyle.DangerTheme(CurrentTheme) : CurrentTheme;
	}
}

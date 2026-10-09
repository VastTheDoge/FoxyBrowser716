using System.Threading;
using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("Photo Frame Widget", MaterialIconKind.ImageFrame, WidgetCategory.Misc)]
public partial class PhotoFrameWidget : WidgetBase
{
	private static readonly string[] PickerFileTypes = [".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico"];
	private static readonly HashSet<string> ImageExtensions = new(PickerFileTypes, StringComparer.OrdinalIgnoreCase);

	private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(500);
	private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(10);
	private const int MinSlideSeconds = 2;

	private readonly Border[] _layers;
	private readonly ImageBrush[] _brushes;
	private int _front = -1;          // layer on screen, -1 = none
	private int _loadingLayer = -1;   // hidden layer whose picture is still loading, -1 = none
	private DateTime _loadStartedAt;
	private DateTime _revealedAt;

	private string _imageText = "";
	private string _folderText = "";
	private int _slideSeconds = 30;
	private bool _fit;
	private bool _initialized;
	private bool _statusIsError;

	private int _reloadVersion;              // bumped by every reload so stale folder scans are dropped
	private CancellationTokenSource? _debounceCts;
	private List<string> _slides = [];       // slideshow files; empty = single picture mode
	private int _slideIndex;
	private int _failedInARow;
	private bool _rescanning;

	protected PhotoFrameWidget()
	{
		InitializeComponent();
		_layers = [LayerA, LayerB];
		_brushes = [new ImageBrush(), new ImageBrush()];
		for (var i = 0; i < _layers.Length; i++)
		{
			var layer = i;
			_layers[i].Background = _brushes[i];
			_layers[i].OpacityTransition = new ScalarTransition { Duration = FadeDuration };
			_brushes[i].ImageOpened += (_, _) => OnPictureOpened(layer, _brushes[layer].ImageSource);
			_brushes[i].ImageFailed += (_, _) => OnPictureFailed(layer, _brushes[layer].ImageSource);
		}
		ApplyStretch();

		WidgetSettings =
		[
			new FilePickerSetting("Image", "A picture file or an image URL (shown when no slideshow folder is set)", "",
				v => { _imageText = v ?? ""; ScheduleReload(); }, supportUrls: true, fileTypes: PickerFileTypes),
			new FolderPickerSetting("Slideshow Folder", "Cycles through the pictures in this folder and its subfolders", "",
				v => { _folderText = v ?? ""; ScheduleReload(); }),
			new IntSetting("Slideshow Seconds", "How long each slideshow picture stays up", _slideSeconds, v => _slideSeconds = v, MinSlideSeconds, 86400),
			new ComboSetting("Fit", "Fill crops the picture to cover the widget; Fit shows all of it", 0,
				v => { _fit = v == 1; ApplyStretch(); }, ("Fill", 0), ("Fit", 1)),
		];
		ShowStatus("Choose a picture in this widget's settings");
	}

	protected override Task Initialize()
	{
		_initialized = true;
		// nothing decodes while off-screen, so restart the load timeout on Loaded (registered before the live timer,
		// so this runs before its immediate tick)
		Loaded += (_, _) => _loadStartedAt = DateTime.Now;
		CreateLiveTimer(TimeSpan.FromSeconds(1), Tick);
		ApplyTheme();
		_ = ReloadAsync();
		return Task.CompletedTask;
	}

	private void ApplyStretch()
	{
		foreach (var brush in _brushes) brush.Stretch = _fit ? Stretch.Uniform : Stretch.UniformToFill;
	}

	private static string CleanPath(string text) => text.Trim().Trim('"').Trim(); // Explorer's "Copy as path" adds quotes

	private static Uri? ToImageUri(string text)
	{
		text = CleanPath(text);
		if (text.Length == 0) return null;
		// local paths parse as file: uris, which BitmapImage loads directly (same as the home page background)
		if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "file" or "http" or "https" or "ms-appx" or "ms-appdata")
			return uri;
		return NormalizeUrl(text); // bare "example.com/photo.jpg"
	}

	private void ScheduleReload()
	{
		// callbacks also fire while saved settings load, before Initialize, which does the first load itself
		if (_initialized) _ = DebouncedReloadAsync();
	}

	private async Task DebouncedReloadAsync()
	{
		_debounceCts?.Cancel();
		var cts = new CancellationTokenSource();
		_debounceCts = cts;
		try
		{
			// the path boxes report every keystroke; let typing settle before touching the disk or network
			await Task.Delay(TimeSpan.FromMilliseconds(600), cts.Token);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		await ReloadAsync();
	}

	private async Task ReloadAsync()
	{
		var version = ++_reloadVersion;
		_slides = [];
		_failedInARow = 0;

		var folder = CleanPath(_folderText);
		if (folder.Length > 0)
		{
			if (_front < 0) ShowStatus("Loading pictures…");
			var slides = await ScanFolderAsync(folder);
			if (version != _reloadVersion) return; // settings changed again while scanning
			if (slides.Count > 0)
			{
				_slides = slides;
				_slideIndex = 0;
				ShowSlide();
				return;
			}
		}

		if (ToImageUri(_imageText) is { } uri) ShowPicture(uri);
		else ShowNothing(folder.Length > 0 ? "No pictures found in the slideshow folder" : "Choose a picture in this widget's settings");
	}

	private static Task<List<string>> ScanFolderAsync(string folder) => Task.Run(() =>
	{
		try
		{
			if (!Directory.Exists(folder)) return new List<string>();
			// IgnoreInaccessible: one locked subfolder shouldn't hide every other picture
			return Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
				.Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
				.Order(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
		catch (Exception)
		{
			return new List<string>();
		}
	});

	private async Task RescanAsync()
	{
		if (_rescanning) return;
		_rescanning = true;
		var version = _reloadVersion;
		var slides = await ScanFolderAsync(CleanPath(_folderText));
		_rescanning = false;
		// a settings change or giving up since the scan started wins; a folder that came back empty keeps the old list
		if (version != _reloadVersion || _slides.Count == 0 || slides.Count == 0) return;
		var current = _slides[_slideIndex % _slides.Count];
		_slides = slides;
		_slideIndex = Math.Max(0, slides.FindIndex(s => string.Equals(s, current, StringComparison.OrdinalIgnoreCase)));
	}

	private void Tick()
	{
		var now = DateTime.Now;
		if (_loadingLayer >= 0)
		{
			// a load that never reports back shouldn't stall the frame; show whatever the layer has by now
			if (now - _loadStartedAt > LoadTimeout) Reveal(_loadingLayer);
			return;
		}

		// once the cross-fade is over, drop the hidden picture so only one decoded photo stays in memory
		if (_front >= 0 && now - _revealedAt > FadeDuration * 2 && _brushes[1 - _front].ImageSource is not null)
			_brushes[1 - _front].ImageSource = null;

		if (_slides.Count == 0 || now - _revealedAt < TimeSpan.FromSeconds(Math.Max(MinSlideSeconds, _slideSeconds))) return;
		_slideIndex++;
		if (_slideIndex >= _slides.Count)
		{
			_slideIndex = 0;
			_ = RescanAsync(); // pick up pictures added to the folder since the last pass
		}
		ShowSlide();
	}

	private void ShowSlide()
	{
		while (_slides.Count > 0)
		{
			_slideIndex %= _slides.Count;
			if (Uri.TryCreate(_slides[_slideIndex], UriKind.Absolute, out var uri))
			{
				ShowPicture(uri);
				return;
			}
			if (!SkipFailedSlide()) return;
		}
	}

	/// <summary>Moves past an unreadable slide; false (and an error state) once a whole pass has failed.</summary>
	private bool SkipFailedSlide()
	{
		if (++_failedInARow >= _slides.Count)
		{
			ShowNothing("Couldn't open the pictures in the slideshow folder", isError: true);
			return false;
		}
		_slideIndex++;
		return true;
	}

	/// <summary>Loads <paramref name="uri"/> into the hidden layer; it's revealed once it has decoded.</summary>
	private void ShowPicture(Uri uri)
	{
		var target = _front == 0 ? 1 : 0;
		_loadingLayer = target;
		_loadStartedAt = DateTime.Now;
		_layers[target].Opacity = 0;
		if (_front < 0) ShowStatus("Loading picture…");
		try
		{
			var bitmap = new BitmapImage();
			bitmap.ImageOpened += (_, _) => OnPictureOpened(target, bitmap);
			bitmap.ImageFailed += (_, _) => OnPictureFailed(target, bitmap);
			// source set after attaching, XAML's recommended order for decoding at display size
			_brushes[target].ImageSource = bitmap;
			bitmap.UriSource = uri;
		}
		catch (Exception)
		{
			OnPictureFailed(target, _brushes[target].ImageSource);
		}
	}

	// both the bitmap and its brush report; only the first report for the picture currently loading counts
	private void OnPictureOpened(int layer, ImageSource? source)
	{
		if (layer != _loadingLayer || !ReferenceEquals(_brushes[layer].ImageSource, source)) return;
		Reveal(layer);
	}

	private void OnPictureFailed(int layer, ImageSource? source)
	{
		if (layer != _loadingLayer || !ReferenceEquals(_brushes[layer].ImageSource, source)) return;
		_loadingLayer = -1;
		_brushes[layer].ImageSource = null;
		if (_slides.Count > 0)
		{
			// the current picture stays up while we move on to the next file
			if (SkipFailedSlide()) ShowSlide();
		}
		else ShowNothing("Couldn't load the picture", isError: true);
	}

	private void Reveal(int layer)
	{
		_loadingLayer = -1;
		_failedInARow = 0;
		var previous = _front;
		_front = layer;
		_revealedAt = DateTime.Now;
		// the new picture fades in on top while the old one fades out underneath
		Canvas.SetZIndex(_layers[layer], 1);
		_layers[layer].Opacity = 1;
		if (previous >= 0 && previous != layer)
		{
			Canvas.SetZIndex(_layers[previous], 0);
			_layers[previous].Opacity = 0;
		}
		ShowStatus(null);
	}

	private void ShowNothing(string message, bool isError = false)
	{
		_slides = [];
		_loadingLayer = -1;
		_front = -1;
		for (var i = 0; i < _layers.Length; i++)
		{
			_layers[i].Opacity = 0;
			_brushes[i].ImageSource = null;
		}
		ShowStatus(message, isError);
	}

	private void ShowStatus(string? message, bool isError = false)
	{
		_statusIsError = isError;
		StatusView.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
		StatusText.Text = message ?? "";
		StatusIcon.Kind = isError ? MaterialIconKind.ImageOffOutline : MaterialIconKind.ImageOutline;
		ColorStatus();
	}

	private void ColorStatus()
	{
		var brush = new SolidColorBrush(_statusIsError ? CurrentTheme.NoColor : CurrentTheme.SecondaryForegroundColor);
		StatusText.Foreground = brush;
		StatusIcon.Foreground = brush;
	}

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);
		ColorStatus();
	}
}

using FoxyBrowser716.Controls.Generic;
using Material.Icons.WinUI3;
using Microsoft.UI.Dispatching;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

// Mirrors whatever Windows shows in its media flyout (Spotify, a YouTube tab, Media Player...) through the
// global system media transport controls. Every WinRT call can throw once the app behind a session exits.
[WidgetInfo("Now Playing Widget", MaterialIconKind.MusicNote, WidgetCategory.Tools)]
public partial class NowPlayingWidget : WidgetBase
{
	private const double ProgressWidth = 186; // the text column's fixed design width

	// friendly names for common sources; anything else is derived from its app id
	private static readonly (string key, string name)[] KnownApps =
	[
		("spotify", "Spotify"),
		("msedge", "Microsoft Edge"),
		("chrome", "Google Chrome"),
		("firefox", "Firefox"),
		("308046B0AF4A39CB", "Firefox"), // a default Firefox install registers under this hashed id
		("foxybrowser", "FoxyBrowser716"),
		("brave", "Brave"),
		("opera", "Opera"),
		("vivaldi", "Vivaldi"),
		("zunemusic", "Media Player"),
		("zunevideo", "Movies & TV"),
		("vlc", "VLC"),
		("applemusic", "Apple Music"),
		("itunes", "iTunes"),
		("discord", "Discord"),
	];

	private readonly DispatcherQueue _ui;

	private GlobalSystemMediaTransportControlsSessionManager? _manager;
	private GlobalSystemMediaTransportControlsSession? _session;
	private bool _active;               // between Loaded and Unloaded
	private bool _requestingManager;
	private bool _managerSubscribed;
	private int _mediaVersion;          // bumped per refresh so a slow art load for an older track is dropped

	private ImageBrush? _art;
	private bool _playing;
	private bool? _shownPlaying;        // which icon the play/pause button currently has
	private double _rate = 1;
	private bool _hasTimeline;
	private TimeSpan _start, _end, _position;
	private DateTimeOffset _positionAt; // when _position was true; extrapolated from while playing

	protected NowPlayingWidget()
	{
		InitializeComponent();
		_ui = DispatcherQueue;
	}

	protected override Task Initialize()
	{
		CreateLiveTimer(TimeSpan.FromMilliseconds(500), RenderProgress);
		// session events keep native references to this widget, so only hold them while on screen
		Loaded += (_, _) =>
		{
			_active = true;
			_ = AttachAsync();
		};
		Unloaded += (_, _) =>
		{
			_active = false;
			DetachManager();
			DetachSession();
		};
		RefreshAll();
		ApplyTheme();
		return Task.CompletedTask;
	}

	#region Sessions

	private async Task AttachAsync()
	{
		if (_manager is null)
		{
			if (_requestingManager) return; // the request already in flight attaches when it completes
			_requestingManager = true;
			try { _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
			catch { /* no media service (old Windows, Wine): stay in the empty state */ }
			finally { _requestingManager = false; }
		}

		// Unloaded may have run while the manager was being requested
		if (!_active || _manager is null) return;
		if (!_managerSubscribed)
		{
			try
			{
				_manager.CurrentSessionChanged += Manager_OnCurrentSessionChanged;
				_managerSubscribed = true;
			}
			catch { return; }
		}
		SwitchSession(GetCurrentSession());
	}

	private void DetachManager()
	{
		if (!_managerSubscribed || _manager is null) return;
		_managerSubscribed = false;
		try { _manager.CurrentSessionChanged -= Manager_OnCurrentSessionChanged; }
		catch { /* media service went away */ }
	}

	private GlobalSystemMediaTransportControlsSession? GetCurrentSession()
	{
		try { return _manager?.GetCurrentSession(); }
		catch { return null; }
	}

	private void SwitchSession(GlobalSystemMediaTransportControlsSession? session)
	{
		if (!Equals(_session, session))
		{
			DetachSession();
			_session = session;
			if (session is not null)
			{
				try
				{
					session.MediaPropertiesChanged += Session_OnMediaPropertiesChanged;
					session.PlaybackInfoChanged += Session_OnPlaybackInfoChanged;
					session.TimelinePropertiesChanged += Session_OnTimelinePropertiesChanged;
				}
				catch
				{
					// the app closed while subscribing; drop whatever did attach, a session-changed event follows
					DetachSession();
				}
			}
		}
		RefreshAll();
	}

	private void DetachSession()
	{
		if (_session is not { } session) return;
		_session = null;
		// separately, so one failure (app already gone) doesn't leave the other handlers attached
		try { session.MediaPropertiesChanged -= Session_OnMediaPropertiesChanged; } catch { /* app exited */ }
		try { session.PlaybackInfoChanged -= Session_OnPlaybackInfoChanged; } catch { /* app exited */ }
		try { session.TimelinePropertiesChanged -= Session_OnTimelinePropertiesChanged; } catch { /* app exited */ }
	}

	// all four events arrive on background threads; the sender check drops events queued by a session we've since left
	private void Manager_OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
		=> _ui.TryEnqueue(() =>
		{
			if (_active && _managerSubscribed) SwitchSession(GetCurrentSession());
		});

	private void Session_OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
		=> _ui.TryEnqueue(() =>
		{
			if (IsCurrent(sender)) _ = RefreshMediaPropertiesAsync();
		});

	private void Session_OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
		=> _ui.TryEnqueue(() =>
		{
			if (IsCurrent(sender)) RefreshPlaybackInfo();
		});

	private void Session_OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
		=> _ui.TryEnqueue(() =>
		{
			if (IsCurrent(sender)) RefreshTimeline();
		});

	private bool IsCurrent(GlobalSystemMediaTransportControlsSession session) => _active && Equals(_session, session);

	#endregion

	#region Refresh

	private void RefreshAll()
	{
		RefreshPlaybackInfo();
		RefreshTimeline();
		_ = RefreshMediaPropertiesAsync();
	}

	private async Task RefreshMediaPropertiesAsync()
	{
		var version = ++_mediaVersion;
		if (_session is not { } session)
		{
			TitleText.Text = "Nothing playing";
			ArtistText.Text = "Media from any app shows up here";
			SourceText.Text = "";
			SetArt(null);
			return;
		}

		string title = "", artist = "", source = "";
		IRandomAccessStreamReference? thumbnail = null;
		try
		{
			source = PrettifyAppId(session.SourceAppUserModelId);
			if (await session.TryGetMediaPropertiesAsync() is { } properties)
			{
				title = properties.Title;
				artist = FirstNonEmpty(properties.Artist, properties.AlbumArtist, properties.Subtitle);
				thumbnail = properties.Thumbnail;
			}
		}
		catch { /* the app closed mid-query; a session-changed event follows */ }
		// apps often fire several property changes per track; only the newest refresh gets to write
		if (version != _mediaVersion) return;

		TitleText.Text = string.IsNullOrWhiteSpace(title) ? "Unknown title" : title;
		ArtistText.Text = artist;
		SourceText.Text = source;

		var art = await LoadArtAsync(thumbnail);
		if (version == _mediaVersion) SetArt(art);
	}

	private static async Task<ImageBrush?> LoadArtAsync(IRandomAccessStreamReference? thumbnail)
	{
		if (thumbnail is null) return null;
		try
		{
			using var stream = await thumbnail.OpenReadAsync();
			var bitmap = new BitmapImage();
			await bitmap.SetSourceAsync(stream);
			return new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
		}
		catch { return null; } // unreadable or unsupported image: the music icon stands in
	}

	private void RefreshPlaybackInfo()
	{
		bool playing = false, canPrevious = false, canToggle = false, canNext = false;
		var rate = 1.0;
		try
		{
			if (_session?.GetPlaybackInfo() is { } info)
			{
				playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
				var reportedRate = info.PlaybackRate ?? 1;
				if (reportedRate is > 0 and <= 16) rate = reportedRate;
				if (info.Controls is { } controls)
				{
					canPrevious = controls.IsPreviousEnabled;
					canNext = controls.IsNextEnabled;
					canToggle = controls.IsPlayPauseToggleEnabled || (playing ? controls.IsPauseEnabled : controls.IsPlayEnabled);
				}
			}
		}
		catch { /* the app exited; show it as stopped until the session-changed event lands */ }

		if (playing != _playing || rate != _rate)
		{
			// re-anchor so the bar freezes (or resumes) where it is, even for apps that don't send a timeline update
			_position = CurrentPosition();
			_positionAt = DateTimeOffset.Now;
			_playing = playing;
			_rate = rate;
		}

		SetButtonEnabled(ButtonPrevious, canPrevious);
		SetButtonEnabled(ButtonPlayPause, canToggle);
		SetButtonEnabled(ButtonNext, canNext);
		if (_shownPlaying != _playing)
		{
			ButtonPlayPause.Content = new MaterialIcon { Kind = _playing ? MaterialIconKind.Pause : MaterialIconKind.Play };
			_shownPlaying = _playing;
		}
		RenderProgress();
	}

	private void RefreshTimeline()
	{
		_hasTimeline = false;
		try
		{
			// EndTime stays zero for sources that don't report a duration (most live streams, some players)
			if (_session?.GetTimelineProperties() is { } timeline && timeline.EndTime > timeline.StartTime)
			{
				_start = timeline.StartTime;
				_end = timeline.EndTime;
				_position = timeline.Position;
				var now = DateTimeOffset.Now;
				var updated = timeline.LastUpdatedTime;
				// some apps leave this unset or skewed; extrapolating from it would pin the bar to the end
				_positionAt = updated > now - TimeSpan.FromDays(1) && updated <= now + TimeSpan.FromSeconds(5) ? updated : now;
				_hasTimeline = true;
			}
		}
		catch { /* the app exited */ }

		var visibility = _hasTimeline ? Visibility.Visible : Visibility.Collapsed;
		TimelineBar.Visibility = visibility;
		ElapsedText.Visibility = visibility;
		DurationText.Visibility = visibility;
		RenderProgress();
	}

	private TimeSpan CurrentPosition()
	{
		var position = _position;
		if (_playing) position += (DateTimeOffset.Now - _positionAt) * _rate;
		return position < _start ? _start : position > _end ? _end : position;
	}

	private void RenderProgress()
	{
		if (!_hasTimeline) return;
		var length = _end - _start;
		var elapsed = CurrentPosition() - _start;
		ProgressFill.Width = ProgressWidth * Math.Clamp(elapsed / length, 0, 1);
		ElapsedText.Text = FormatTime(elapsed);
		DurationText.Text = FormatTime(length);
	}

	#endregion

	#region Controls

	private async void ButtonPrevious_OnClick(object sender, RoutedEventArgs e)
	{
		if (_session is not { } session) return;
		try { await session.TrySkipPreviousAsync(); }
		catch { /* the app closed between the click and the call */ }
	}

	private async void ButtonPlayPause_OnClick(object sender, RoutedEventArgs e)
	{
		if (_session is not { } session) return;
		try
		{
			// not every app implements the toggle; fall back to the explicit command
			if (!await session.TryTogglePlayPauseAsync())
				await (_playing ? session.TryPauseAsync() : session.TryPlayAsync());
		}
		catch { /* the app closed between the click and the call */ }
	}

	private async void ButtonNext_OnClick(object sender, RoutedEventArgs e)
	{
		if (_session is not { } session) return;
		try { await session.TrySkipNextAsync(); }
		catch { /* the app closed between the click and the call */ }
	}

	private static void SetButtonEnabled(FIconButton button, bool enabled)
	{
		// FIconButton raises clicks from raw pointer events, so stop hit-testing rather than relying on IsEnabled
		button.IsHitTestVisible = enabled;
		button.Opacity = enabled ? 1 : 0.35;
	}

	#endregion

	#region Formatting

	private void SetArt(ImageBrush? art)
	{
		_art = art;
		ArtBorder.Background = (Brush?)art ?? new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorVeryTransparent);
		ArtIcon.Visibility = art is null ? Visibility.Visible : Visibility.Collapsed;
	}

	private static string FormatTime(TimeSpan time) => time.TotalHours >= 1
		? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
		: $"{time.Minutes}:{time.Seconds:00}";

	private static string FirstNonEmpty(params string?[] values) =>
		values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

	/// <summary>"SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify" / "Spotify.exe" / "MSEdge" → a readable app name.</summary>
	private static string PrettifyAppId(string? appId)
	{
		if (string.IsNullOrWhiteSpace(appId)) return "";
		foreach (var (key, friendly) in KnownApps)
			if (appId.Contains(key, StringComparison.OrdinalIgnoreCase)) return friendly;

		var id = appId;
		var bang = id.IndexOf('!');
		if (bang >= 0)
		{
			// packaged app: "Publisher.AppName_publisherhash!EntryPoint"
			id = id[..bang];
			var underscore = id.IndexOf('_');
			if (underscore > 0) id = id[..underscore];
			var dot = id.LastIndexOf('.');
			if (dot >= 0 && dot < id.Length - 1) id = id[(dot + 1)..];
		}
		else
		{
			// desktop app: an exe name, sometimes behind a (known-folder) path
			id = id[(id.LastIndexOfAny(['\\', '/']) + 1)..];
			if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = id[..^4];
		}
		if (id.Length == 0) return appId;

		// "SpotifyMusic" -> "Spotify Music"
		var pretty = new StringBuilder(id.Length + 4);
		for (var i = 0; i < id.Length; i++)
		{
			if (i > 0 && char.IsUpper(id[i]) && char.IsLower(id[i - 1])) pretty.Append(' ');
			pretty.Append(id[i]);
		}
		pretty[0] = char.ToUpperInvariant(pretty[0]);
		return pretty.ToString();
	}

	#endregion

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);
		SetArt(_art);
		ArtIcon.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);

		TitleText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		ArtistText.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		SourceText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);

		var times = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		ElapsedText.Foreground = times;
		DurationText.Foreground = times;
		ProgressTrack.Fill = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
		ProgressFill.Fill = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);

		ButtonPrevious.CurrentTheme = CurrentTheme;
		ButtonPlayPause.CurrentTheme = CurrentTheme;
		ButtonNext.CurrentTheme = CurrentTheme;
	}
}

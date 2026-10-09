using System.Globalization;
using FoxyBrowser716.DataObjects.Settings;
using Microsoft.Web.WebView2.Core;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("Web Page Widget", MaterialIconKind.Web, WidgetCategory.WebsiteNavigation)]
public partial class WebPageWidget : WidgetBase
{
	private const string MobileUserAgent =
		"Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36";
	// below this the toolbar would eat most of the widget, so it hides itself
	private const double ToolbarMinHeight = 110;

	private WebView2? _web;
	private string? _defaultUserAgent;

	private string _url = "https://en.wikipedia.org/wiki/Main_Page";
	private bool _mobileLayout = true;
	private double _zoom = 100;
	private bool _linksInNewTab;
	private int _refreshMinutes;
	private bool _muted;
	private bool _showToolbar = true;
	private DateTime _lastNavigation = DateTime.Now;

	protected WebPageWidget()
	{
		InitializeComponent();
		WidgetSettings =
		[
			new StringSetting("URL", "The page to show", _url, v => { _url = v; Navigate(); }),
			new BoolSetting("Mobile Layout", "Ask sites for their phone layout, which usually fits a widget better", true, v =>
			{
				_mobileLayout = v;
				ApplyUserAgent();
				Navigate();
			}),
			new SliderSetting("Zoom", "Page zoom in percent", _zoom, v => { _zoom = v; _ = ApplyZoom(); }, 25, 300, 5),
			new BoolSetting("Open Links In New Tab", "Clicked links open as browser tabs and the widget stays on its page", false, v => _linksInNewTab = v),
			new IntSetting("Auto Refresh Minutes", "0 = never", 0, v => _refreshMinutes = v, 0, 1440),
			new BoolSetting("Mute", "", false, v =>
			{
				_muted = v;
				if (_web?.CoreWebView2 is { } core) core.IsMuted = v;
			}),
			new BoolSetting("Show Toolbar", "Back, reload, page title and open-in-tab buttons", true, v => { _showToolbar = v; UpdateToolbarVisibility(); }),
		];
	}

	protected override Task Initialize()
	{
		// the home page is only hidden (not unloaded) when a tab is shown, so Unloaded means the widget was removed
		Loaded += async (_, _) => await EnsureWebView();
		Unloaded += (_, _) => CloseWebView();
		CreateLiveTimer(TimeSpan.FromMinutes(1), AutoRefresh);
		ApplyTheme();
		return Task.CompletedTask;
	}

	private async Task EnsureWebView()
	{
		if (_web is not null) return;
		if (TabManager.WebsiteEnvironment is not { } environment)
		{
			ShowStatus("The browser engine isn't ready yet");
			return;
		}

		var web = new WebView2();
		_web = web;
		WebHost.Children.Add(web);
		ShowStatus("Loading...");

		try
		{
			// same profile as the window's tabs (so sites are signed in), and off-the-record in a private instance
			if (TabManager.Instance.IsPrivate)
			{
				var options = environment.CreateCoreWebView2ControllerOptions();
				options.IsInPrivateModeEnabled = true;
				await web.EnsureCoreWebView2Async(environment, options);
			}
			else
				await web.EnsureCoreWebView2Async(environment);
		}
		catch (Exception)
		{
			if (_web == web) ShowStatus("Couldn't start the page view");
			return;
		}
		if (_web != web) return; // removed while it was starting up

		var core = web.CoreWebView2;
		core.IsMuted = _muted;
		core.NewWindowRequested += (_, args) =>
		{
			args.Handled = true;
			OpenInNewTab(args.Uri);
		};
		core.NavigationStarting += Core_OnNavigationStarting;
		core.NavigationCompleted += (_, _) => StatusView.Visibility = Visibility.Collapsed;
		core.DOMContentLoaded += (_, _) => _ = ApplyZoom();
		core.DocumentTitleChanged += (_, _) => TitleText.Text = core.DocumentTitle;
		core.HistoryChanged += (_, _) => ButtonBack.Opacity = core.CanGoBack ? 1 : 0.4;
		ButtonBack.Opacity = 0.4;

		ApplyUserAgent();
		Navigate();
	}

	private void CloseWebView()
	{
		if (_web is null) return;
		WebHost.Children.Remove(_web);
		_web.Close();
		_web = null;
	}

	private void Core_OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
	{
		// programmatic navigations (our URL, reload, auto refresh) aren't user initiated, so they stay in the widget
		if (!_linksInNewTab || !args.IsUserInitiated) return;
		args.Cancel = true;
		OpenInNewTab(args.Uri);
	}

	private void Navigate()
	{
		if (_web?.CoreWebView2 is not { } core) return; // settings load before the view exists; EnsureWebView navigates
		if (NormalizeUrl(_url) is not { } uri)
		{
			ShowStatus("Set a URL in this widget's settings");
			return;
		}
		_lastNavigation = DateTime.Now;
		core.Navigate(uri.ToString());
	}

	private void ApplyUserAgent()
	{
		if (_web?.CoreWebView2 is not { } core) return;
		_defaultUserAgent ??= core.Settings.UserAgent;
		core.Settings.UserAgent = _mobileLayout ? MobileUserAgent : _defaultUserAgent;
	}

	private async Task ApplyZoom()
	{
		if (_web?.CoreWebView2 is not { } core) return;
		try
		{
			// CSS zoom rather than the controller's zoom factor, which the WinUI control doesn't expose
			var factor = (_zoom / 100).ToString(CultureInfo.InvariantCulture);
			await core.ExecuteScriptAsync($"document.documentElement.style.zoom = '{factor}'");
		}
		catch { /* the page navigated away mid-call; DOMContentLoaded will apply it again */ }
	}

	private void AutoRefresh()
	{
		if (_refreshMinutes <= 0 || _web?.CoreWebView2 is not { } core) return;
		if (DateTime.Now - _lastNavigation < TimeSpan.FromMinutes(_refreshMinutes)) return;
		_lastNavigation = DateTime.Now;
		core.Reload();
	}

	private void ShowStatus(string text)
	{
		StatusText.Text = text;
		StatusView.Visibility = Visibility.Visible;
	}

	private void Card_OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateToolbarVisibility();

	private void UpdateToolbarVisibility() =>
		Toolbar.Visibility = _showToolbar && Card.ActualHeight >= ToolbarMinHeight ? Visibility.Visible : Visibility.Collapsed;

	private void ButtonBack_OnClick(object sender, RoutedEventArgs e)
	{
		if (_web?.CoreWebView2 is { CanGoBack: true } core) core.GoBack();
	}

	private void ButtonReload_OnClick(object sender, RoutedEventArgs e)
	{
		_lastNavigation = DateTime.Now;
		_web?.CoreWebView2?.Reload();
	}

	private void ButtonOpenInTab_OnClick(object sender, RoutedEventArgs e)
	{
		var current = _web?.CoreWebView2?.Source;
		if (!string.IsNullOrEmpty(current)) OpenInNewTab(current);
		else if (NormalizeUrl(_url) is { } uri) OpenInNewTab(uri.ToString());
	}

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);
		TitleText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		StatusText.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		ButtonBack.CurrentTheme = CurrentTheme;
		ButtonReload.CurrentTheme = CurrentTheme;
		ButtonOpenInTab.CurrentTheme = CurrentTheme;
	}
}

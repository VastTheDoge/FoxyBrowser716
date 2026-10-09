using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("Quick Link Widget", MaterialIconKind.LinkVariant, WidgetCategory.WebsiteNavigation)]
public partial class QuickLinkWidget : WidgetBase
{
	private string _title = "Wikipedia";
	private string _url = "https://www.wikipedia.org";
	private string _iconUrl = "";
	private bool _showTitle = true;
	private string? _shownIconUrl;

	protected QuickLinkWidget()
	{
		InitializeComponent();
		WidgetSettings =
		[
			new StringSetting("Title", "Leave empty to show the site's address", _title, v => { _title = v; Refresh(); }),
			new StringSetting("URL", "Where the link goes, e.g. github.com", _url, v => { _url = v; Refresh(); }),
			new StringSetting("Icon URL", "Optional; defaults to the site's /favicon.ico", _iconUrl, v => { _iconUrl = v; Refresh(); }),
			new BoolSetting("Show Title", "", true, v => { _showTitle = v; Refresh(); }),
		];
	}

	protected override Task Initialize()
	{
		ApplyTheme();
		return Task.CompletedTask;
	}

	private void Refresh()
	{
		var uri = NormalizeUrl(_url);
		TitleText.Text = !string.IsNullOrWhiteSpace(_title) ? _title : uri?.Host ?? "Set a URL in settings";
		TitleText.Visibility = _showTitle ? Visibility.Visible : Visibility.Collapsed;
		TitleText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);

		var iconUrl = !string.IsNullOrWhiteSpace(_iconUrl) ? _iconUrl.Trim()
			: uri is null ? null
			: $"{uri.Scheme}://{uri.Host}/favicon.ico";
		// settings callbacks fire once each on load; only refetch the icon when it actually changes
		if (IconHost.Child is null || iconUrl != _shownIconUrl)
			IconHost.Child = CreateFavicon(iconUrl, 64, new SolidColorBrush(CurrentTheme.SecondaryForegroundColor));
		_shownIconUrl = iconUrl;
		ToolTipService.SetToolTip(Card, uri?.ToString());
	}

	private void Card_OnTapped(object sender, TappedRoutedEventArgs e)
	{
		if (NormalizeUrl(_url) is { } uri) OpenInNewTab(uri.ToString());
	}

	private void Card_OnPointerEntered(object sender, PointerRoutedEventArgs e) =>
		Card.Background = new SolidColorBrush(CurrentTheme.PrimaryHighlightColorVeryTransparent);

	private void Card_OnPointerExited(object sender, PointerRoutedEventArgs e) => ApplyCardTheme(Card);

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);
		IconHost.Child = null; // rebuild so the fallback icon picks up the new colors
		Refresh();
	}
}

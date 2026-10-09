using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("Search Widget", MaterialIconKind.Magnify, WidgetCategory.WebsiteNavigation)]
public partial class SearchWidget : WidgetBase
{
	private bool _showEngineIcon = true;
	private int _engine = -1; // -1 = whatever the window's search engine currently is

	protected SearchWidget()
	{
		InitializeComponent();
		WidgetSettings =
		[
			new StringSetting("Placeholder", "Text shown in the empty box", "Search the web", v => SearchInput.PlaceHolderText = v),
			new BoolSetting("Show Search Engine Icon", "", true, v => { _showEngineIcon = v; UpdateLayoutForSize(); }),
			new ComboSetting("Search With", "Pick a site to make this a dedicated search box (e.g. YouTube)", -1, v => { _engine = v; RefreshEngineIcon(); },
				[("Browser Default", -1), ..Enum.GetValues<InfoGetter.SearchEngine>().Select(e => (InfoGetter.GetSearchEngineName(e), (int)e))]),
		];
	}

	protected override Task Initialize()
	{
		// the search engine can be switched from the top bar at any time, so re-read it whenever we're used
		Loaded += (_, _) => RefreshEngineIcon();
		SearchInput.GotFocus += (_, _) => RefreshEngineIcon();
		ApplyTheme();
		return Task.CompletedTask;
	}

	protected override void ApplyTheme()
	{
		SearchInput.CurrentTheme = CurrentTheme;
	}

	private void RefreshEngineIcon()
	{
		try
		{
			EngineIcon.Source = new BitmapImage(new Uri(InfoGetter.GetSearchEngineIcon(CurrentEngine)));
		}
		catch { EngineIcon.Source = null; }
	}

	private InfoGetter.SearchEngine CurrentEngine => _engine >= 0 ? (InfoGetter.SearchEngine)_engine : TabManager.Instance.Cache.CurrentSearchEngine;

	private void SearchInput_OnEnterPressed()
	{
		var text = SearchInput.CurrentText.Trim();
		if (text.Length == 0) return;
		// a dedicated engine always searches; the default box also accepts addresses, like the top bar
		OpenInNewTab(_engine >= 0 ? InfoGetter.GetSearchUrl(CurrentEngine, text) : text);
		SearchInput.SetText("");
	}

	private void RootGrid_OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutForSize();

	private void UpdateLayoutForSize()
	{
		var height = RootGrid.ActualHeight;
		var width = RootGrid.ActualWidth;
		if (height <= 0 || width <= 0) return;

		// the text box adds ~10px of border/padding around one line of text (~1.35x the font size);
		// also cap by width so a tall, narrow widget still fits a few words
		var fontSize = Math.Clamp(Math.Min((height - 10) / 1.35, width / 9), 8, 48);
		SearchInput.FontSize = fontSize;

		var iconSize = fontSize * 1.3;
		EngineIcon.Width = EngineIcon.Height = iconSize;
		// drop the icon before it squeezes the box into uselessness
		EngineIcon.Visibility = _showEngineIcon && width > iconSize + fontSize * 8 ? Visibility.Visible : Visibility.Collapsed;
	}
}

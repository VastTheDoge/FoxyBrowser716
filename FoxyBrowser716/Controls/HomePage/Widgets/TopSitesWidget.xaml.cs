using FoxyBrowser716.DataManagement;
using FoxyBrowser716.DataObjects.Settings;
using FoxyBrowser716.ErrorHandeler;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

// Chrome-style "most visited" tiles: this profile's history grouped by site and ranked by visits.
// A tile opens the site's root rather than one of its pages: a site's total usually comes from many different
// pages (searches, videos, threads), so its single most-visited URL is often an arbitrary old one.
[WidgetInfo("Top Sites Widget", MaterialIconKind.TrendingUp, WidgetCategory.WebsiteNavigation)]
public partial class TopSitesWidget : WidgetBase
{
	private const double IconDesignSize = 32;
	private const int MaxSitesLimit = 40;
	// a page load records the visit and then its title and icon a moment later; re-rank once history goes quiet
	private static readonly TimeSpan ChangeSettleTime = TimeSpan.FromSeconds(1.5);

	private sealed record TopSite(string Label, string Url, string Title, string FavIconUrl);

	private sealed class SiteTally(string label, string url, DateTime lastVisited)
	{
		public string Label { get; } = label;
		public string Url { get; } = url;
		public DateTime LastVisited { get; } = lastVisited;
		public int Visits { get; set; }
		public string FavIconUrl { get; set; } = "";
		public string Title { get; set; } = "";
	}

	private readonly List<Border> _tiles = [];
	// best first, always up to MaxSitesLimit, so changing Max Sites never re-reads history
	private List<TopSite> _sites = [];
	private HistoryManager? _history;
	private bool _ranked;   // false until the first ranking finishes, so the empty message doesn't flash first
	private bool _dirty;
	private bool _ranking;
	private DateTime _lastChange = DateTime.MinValue;

	private int _maxSites = 8;
	private double _tileSize = 80;
	private bool _showTitles = true;

	protected TopSitesWidget()
	{
		InitializeComponent();
		WidgetSettings =
		[
			new IntSetting("Max Sites", "", _maxSites, v => { _maxSites = Math.Clamp(v, 4, MaxSitesLimit); RebuildTiles(); }, 4, MaxSitesLimit),
			new IntSetting("Tile Size", "Target tile width in pixels; tiles stretch to fill each row", 80, v => { _tileSize = Math.Clamp(v, 40, 240); LayoutTiles(); }, 40, 240),
			new BoolSetting("Show Titles", "", true, v => { _showTitles = v; RebuildTiles(); }),
		];
	}

	protected override Task Initialize()
	{
		// registered before the live timer so its immediate tick on Loaded already sees the fresh subscription
		Loaded += (_, _) => Subscribe();
		Unloaded += (_, _) => Unsubscribe();
		CreateLiveTimer(TimeSpan.FromSeconds(1), Tick);
		ApplyTheme();
		return Task.CompletedTask;
	}

	private void Subscribe()
	{
		Unsubscribe();
		_history = TabManager.Instance.History;
		if (_history is not null) _history.Changed += History_OnChanged;
		_dirty = true;
		_lastChange = DateTime.MinValue;
	}

	private void Unsubscribe()
	{
		if (_history is not null) _history.Changed -= History_OnChanged;
		_history = null;
	}

	// Changed is raised on whichever thread changed history; only touch our state on the UI thread
	private void History_OnChanged() => DispatcherQueue.TryEnqueue(() =>
	{
		_dirty = true;
		_lastChange = DateTime.Now;
	});

	private void Tick()
	{
		if (!_dirty || _ranking || DateTime.Now - _lastChange < ChangeSettleTime) return;
		_dirty = false;
		_ = RankAsync();
	}

	private async Task RankAsync()
	{
		_ranking = true;
		try
		{
			// ranking walks every history entry, so it runs off the UI thread and only after history changed
			var sites = new List<TopSite>();
			if (_history is { } history)
				sites = await Task.Run(() => Rank(history.GetEntries()));
			// most changes just bump a count without moving the visible tiles; don't rebuild (and reload favicons) for those
			var unchanged = _ranked && sites.Take(_maxSites).SequenceEqual(_sites.Take(_maxSites));
			_sites = sites;
			_ranked = true;
			if (!unchanged) RebuildTiles();
		}
		catch (Exception e)
		{
			FoxyLogger.AddError(e);
		}
		finally
		{
			_ranking = false;
		}
	}

	/// <param name="entries">Newest first (as <see cref="HistoryManager.GetEntries"/> returns them).</param>
	private static List<TopSite> Rank(List<HistoryEntry> entries)
	{
		// entries are live objects the UI thread may update meanwhile; a slightly stale title or count is harmless here
		var now = DateTime.Now;
		var tallies = new Dictionary<string, SiteTally>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in entries)
		{
			if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0)
				continue;

			var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
			// keep local dev servers apart (localhost:3000 vs localhost:8080)
			var label = uri.IsDefaultPort ? host : $"{host}:{uri.Port}";
			if (!tallies.TryGetValue(label, out var tally))
				// newest first, so the first entry seen is the latest visit: its scheme and www-ness are what works today
				tallies[label] = tally = new SiteTally(label, $"{uri.Scheme}://{uri.Authority}/", entry.LastVisited);

			tally.Visits += Math.Max(1, entry.VisitCount);
			if (tally.FavIconUrl.Length == 0 && !string.IsNullOrWhiteSpace(entry.FavIconUrl)) tally.FavIconUrl = entry.FavIconUrl;
			// the home page's title names the site; a deeper page's title only names that page
			if (tally.Title.Length == 0 && uri.AbsolutePath == "/" && !string.IsNullOrWhiteSpace(entry.Title)) tally.Title = entry.Title;
		}

		return tallies.Values
			// visits dominate; a site used today gets up to 1.5x, fading back to 1x with a two-week half-life
			.OrderByDescending(t => t.Visits * (1 + 0.5 * Math.Pow(0.5, Math.Max(0, (now - t.LastVisited).TotalDays) / 14)))
			.ThenByDescending(t => t.LastVisited)
			.Take(MaxSitesLimit)
			.Select(t => new TopSite(t.Label, t.Url, t.Title, t.FavIconUrl))
			.ToList();
	}

	private void RebuildTiles()
	{
		TilesGrid.Children.Clear();
		_tiles.Clear();

		foreach (var site in _sites.Take(_maxSites))
		{
			var title = new TextBlock
			{
				Text = site.Label,
				TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
				HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
				Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor),
				Visibility = _showTitles ? Visibility.Visible : Visibility.Collapsed,
			};
			var content = new Grid { RowSpacing = 2 };
			content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
			content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			// fixed design size inside a Viewbox so the icon scales with the tile instead of being rebuilt
			content.Children.Add(new Viewbox
			{
				Stretch = Stretch.Uniform, Margin = new Thickness(4),
				Child = CreateFavicon(site.FavIconUrl, IconDesignSize, new SolidColorBrush(CurrentTheme.SecondaryForegroundColor)),
			});
			Grid.SetRow(title, 1);
			content.Children.Add(title);

			var tile = new Border
			{
				Margin = new Thickness(2), Padding = new Thickness(4), CornerRadius = new CornerRadius(8),
				Background = new SolidColorBrush(Colors.Transparent), Child = content,
			};
			ToolTipService.SetToolTip(tile, site.Title.Length > 0 ? $"{site.Title}\n{site.Url}" : site.Url);
			tile.PointerEntered += (_, _) => tile.Background = new SolidColorBrush(CurrentTheme.PrimaryHighlightColorVeryTransparent);
			tile.PointerExited += (_, _) => tile.Background = new SolidColorBrush(Colors.Transparent);
			tile.Tapped += (_, _) => OpenInNewTab(site.Url);

			_tiles.Add(tile);
			TilesGrid.Children.Add(tile);
		}

		EmptyText.Text = "Sites you visit often will show up here";
		EmptyView.Visibility = _ranked && _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

		LayoutTiles();
	}

	private void Scroller_OnSizeChanged(object sender, SizeChangedEventArgs e) => LayoutTiles();

	private void LayoutTiles()
	{
		var width = Scroller.ActualWidth;
		if (width <= 0) return;

		var columns = Math.Max(1, (int)(width / _tileSize));
		var tileWidth = width / columns;
		// square-ish tiles, a little taller when a title sits under the icon
		var tileHeight = _showTitles ? tileWidth * 1.05 : tileWidth;
		var rows = (_tiles.Count + columns - 1) / columns;

		TilesGrid.ColumnDefinitions.Clear();
		TilesGrid.RowDefinitions.Clear();
		for (var c = 0; c < columns; c++)
			TilesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		for (var r = 0; r < rows; r++)
			TilesGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(tileHeight) });

		var fontSize = Math.Clamp(tileWidth * 0.13, 8, 14);
		for (var i = 0; i < _tiles.Count; i++)
		{
			Grid.SetColumn(_tiles[i], i % columns);
			Grid.SetRow(_tiles[i], i / columns);
			if (_tiles[i].Child is Grid content && content.Children[1] is TextBlock title) title.FontSize = fontSize;
		}
	}

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);
		EmptyText.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		RebuildTiles();
	}
}

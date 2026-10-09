using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("Bookmarks Widget", MaterialIconKind.BookmarkMultiple, WidgetCategory.WebsiteNavigation)]
public partial class BookmarksWidget : WidgetBase
{
	private const double IconDesignSize = 32;

	private readonly List<Border> _tiles = [];
	private ObservableCollection<WebsiteInfo>? _source;

	private int _sourceKind; // 0 = bookmarks, 1 = pins
	private double _tileSize = 80;
	private bool _showTitles = true;

	protected BookmarksWidget()
	{
		InitializeComponent();
		WidgetSettings =
		[
			new ComboSetting("Show", "", 0, v => { _sourceKind = v; RebindSource(); }, ("Bookmarks", 0), ("Pinned Sites", 1)),
			new IntSetting("Tile Size", "Target tile width in pixels; tiles stretch to fill each row", 80, v => { _tileSize = v; LayoutTiles(); }, 40, 240),
			new BoolSetting("Show Titles", "", true, v => { _showTitles = v; RebuildTiles(); }),
		];
	}

	protected override Task Initialize()
	{
		// the instance can swap its collections when it reloads, so re-resolve every time we come on screen
		Loaded += (_, _) => RebindSource();
		Unloaded += (_, _) => SetSource(null);
		ApplyTheme();
		return Task.CompletedTask;
	}

	private void RebindSource()
	{
		if (TabManager is null) return;
		SetSource(_sourceKind == 1 ? TabManager.Instance.Pins : TabManager.Instance.Bookmarks);
		RebuildTiles();
	}

	private void SetSource(ObservableCollection<WebsiteInfo>? source)
	{
		if (ReferenceEquals(source, _source)) return;
		if (_source is not null) _source.CollectionChanged -= Source_OnCollectionChanged;
		_source = source;
		if (_source is not null) _source.CollectionChanged += Source_OnCollectionChanged;
	}

	private void Source_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => DispatcherQueue.TryEnqueue(RebuildTiles);

	private void RebuildTiles()
	{
		TilesGrid.Children.Clear();
		_tiles.Clear();

		var items = _source?.ToList() ?? [];
		EmptyText.Text = _sourceKind == 1 ? "No pinned sites yet" : "No bookmarks yet";
		EmptyView.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

		foreach (var site in items)
		{
			var title = new TextBlock
			{
				Text = string.IsNullOrWhiteSpace(site.Title) ? site.Url : site.Title,
				TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
				HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
				Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor),
				Visibility = _showTitles ? Visibility.Visible : Visibility.Collapsed,
			};
			var content = new Grid { RowSpacing = 2 };
			content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
			content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			// the favicon is a fixed design size inside a Viewbox so it scales with the tile instead of being rebuilt
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
			ToolTipService.SetToolTip(tile, site.Url);
			tile.PointerEntered += (_, _) => tile.Background = new SolidColorBrush(CurrentTheme.PrimaryHighlightColorVeryTransparent);
			tile.PointerExited += (_, _) => tile.Background = new SolidColorBrush(Colors.Transparent);
			tile.Tapped += (_, _) => OpenInNewTab(site.Url);

			_tiles.Add(tile);
			TilesGrid.Children.Add(tile);
		}

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

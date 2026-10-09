using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using FoxyBrowser716.DataObjects.Settings;
using FoxyBrowser716.ErrorHandeler;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

// RSS 2.0, RSS 1.0 (RDF) and Atom. Elements are matched by local name only, so namespaced and sloppy feeds still parse.
[WidgetInfo("News Feed Widget", MaterialIconKind.Rss, WidgetCategory.WebsiteNavigation)]
public partial class NewsFeedWidget : WidgetBase
{
	private const string DefaultFeedUrl = "https://hnrss.org/frontpage";
	private const int MaxItemsLimit = 50;
	private const long MaxFeedBytes = 4 * 1024 * 1024;

	// the settings box reports every keystroke, so a typed address is only fetched once the user pauses
	private static readonly TimeSpan UrlEditPause = TimeSpan.FromSeconds(1.2);
	private static readonly TimeSpan FailedRetryDelay = TimeSpan.FromMinutes(2);

	private static readonly HttpClient Http = CreateHttpClient();

	// only well-known inline tags: titles like "Vec<T> in Rust" must survive
	private static readonly Regex HtmlTag = new(@"</?(a|b|i|u|s|em|strong|span|code|small|sup|sub|br|p|div|font|img|mark|q|cite|abbr)\b[^>]*>",
		RegexOptions.Compiled | RegexOptions.IgnoreCase);
	private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
	private static readonly Regex CompactOffset = new(@"([+-])(\d{2})(\d{2})$", RegexOptions.Compiled);
	private static readonly Dictionary<string, string> ZoneOffsets = new(StringComparer.OrdinalIgnoreCase)
	{
		["UT"] = "+00:00", ["UTC"] = "+00:00", ["GMT"] = "+00:00", ["Z"] = "+00:00",
		["EST"] = "-05:00", ["EDT"] = "-04:00", ["CST"] = "-06:00", ["CDT"] = "-05:00",
		["MST"] = "-07:00", ["MDT"] = "-06:00", ["PST"] = "-08:00", ["PDT"] = "-07:00",
	};
	private static readonly string[] Rfc822Formats =
		["d MMM yyyy HH:mm:ss zzz", "d MMM yyyy HH:mm zzz", "d MMM yy HH:mm:ss zzz", "d MMM yy HH:mm zzz"];

	private sealed record FeedItem(string Title, Uri? Link, string Source, DateTimeOffset? Published);
	private sealed record Feed(string Title, List<FeedItem> Items);

	private string _feedUrl = DefaultFeedUrl;
	private int _maxItems = 15;
	private int _refreshMinutes = 30;
	private bool _showTimes = true;

	private List<FeedItem> _items = [];
	private string _feedTitle = "";
	private string? _itemsUrl;      // the address _items were downloaded from
	private string? _error;         // why the last attempt failed (older items stay up after a failed refresh)
	private readonly List<(FeedItem item, TextBlock meta)> _metaLines = [];

	private bool _initialized;
	private bool _loading;
	private int _fetchGeneration;   // bumped by every fetch and URL edit, so responses that arrive late are dropped
	private CancellationTokenSource? _fetchCancel;
	private DateTime _lastAttempt = DateTime.MinValue;
	private DateTime? _urlEditedAt;
	private DateTime _metaShownAt;

	protected NewsFeedWidget()
	{
		InitializeComponent();
		WidgetSettings =
		[
			new StringSetting("Feed URL", "Address of an RSS or Atom feed", DefaultFeedUrl, v => { _feedUrl = v ?? ""; OnFeedUrlEdited(); }),
			new IntSetting("Max Items", "", _maxItems, v => { _maxItems = Math.Clamp(v, 3, MaxItemsLimit); RenderItems(); }, 3, MaxItemsLimit),
			new IntSetting("Refresh Minutes", "How often the feed is downloaded again", _refreshMinutes, v => _refreshMinutes = Math.Clamp(v, 5, 240), 5, 240),
			new BoolSetting("Show Times", "Show how long ago each item was posted", true, v => { _showTimes = v; RenderItems(); }),
		];
	}

	protected override Task Initialize()
	{
		_initialized = true;
		StartFetch();
		// one cheap tick drives the periodic refresh, the URL-edit pause and the "3h ago" labels
		CreateLiveTimer(TimeSpan.FromSeconds(1), Tick);
		ApplyTheme();
		return Task.CompletedTask;
	}

	private void OnFeedUrlEdited()
	{
		if (!_initialized) return; // Initialize fetches whatever address was loaded

		// anything in flight is for the old address
		_fetchCancel?.Cancel();
		_fetchGeneration++;
		_loading = false;
		_urlEditedAt = DateTime.Now;
		UpdateStatus();
	}

	private void Tick()
	{
		var now = DateTime.Now;
		if (_urlEditedAt is { } edited)
		{
			if (now - edited >= UrlEditPause) StartFetch();
		}
		else if (!_loading)
		{
			var interval = TimeSpan.FromMinutes(_refreshMinutes);
			if (_error is not null && FailedRetryDelay < interval) interval = FailedRetryDelay;
			if (now - _lastAttempt >= interval) StartFetch();
		}

		if (_showTimes && now - _metaShownAt >= TimeSpan.FromMinutes(1)) UpdateMetaLines();
	}

	private void RefreshButton_OnClick(object sender, RoutedEventArgs e) => StartFetch();

	private void StartFetch() => _ = FetchAsync();

	private async Task FetchAsync()
	{
		_urlEditedAt = null;
		_lastAttempt = DateTime.Now;
		_fetchCancel?.Cancel();
		var cancel = _fetchCancel = new CancellationTokenSource();
		var generation = ++_fetchGeneration;

		var uri = ParseFeedUrl(_feedUrl);
		if (uri is null)
		{
			_loading = false;
			_items = [];
			_itemsUrl = null;
			_feedTitle = "";
			_error = "Add a feed URL in this widget's settings";
			RenderItems();
			return;
		}

		_loading = true;
		if (uri.AbsoluteUri != _itemsUrl)
		{
			// a different feed: don't leave the old one's items up while the new one loads
			_items = [];
			_itemsUrl = null;
			_feedTitle = "";
			RenderItems();
		}
		else UpdateStatus();

		try
		{
			var feed = await DownloadFeedAsync(uri, cancel.Token);
			if (generation != _fetchGeneration) return; // superseded by a newer fetch or a URL edit
			_items = feed.Items;
			_feedTitle = feed.Title;
			_itemsUrl = uri.AbsoluteUri;
			_error = null;
		}
		catch (Exception e)
		{
			if (generation != _fetchGeneration) return;
			_error = Describe(e);
			if (e is not (OperationCanceledException or HttpRequestException or XmlException or InvalidDataException))
				FoxyLogger.AddError(e);
		}

		_loading = false;
		RenderItems();
	}

	private static async Task<Feed> DownloadFeedAsync(Uri uri, CancellationToken token)
	{
		// buffered read: the client's Timeout and MaxResponseContentBufferSize then cover the whole body
		using var response = await Http.GetAsync(uri, token).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();
		var bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
		// a big feed takes a moment to parse; keep that off the UI thread too
		return await Task.Run(() => ParseFeed(bytes, uri), token).ConfigureAwait(false);
	}

	private static HttpClient CreateHttpClient()
	{
		var client = new HttpClient(new SocketsHttpHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
			// the client lives as long as the app; recycle connections so DNS changes are noticed
			PooledConnectionLifetime = TimeSpan.FromMinutes(10),
		})
		{
			Timeout = TimeSpan.FromSeconds(10),
			MaxResponseContentBufferSize = MaxFeedBytes,
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd("FoxyBrowser716/1.0 (News Feed Widget)");
		client.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml");
		client.DefaultRequestHeaders.Accept.ParseAdd("application/atom+xml");
		client.DefaultRequestHeaders.Accept.ParseAdd("application/xml;q=0.9");
		client.DefaultRequestHeaders.Accept.ParseAdd("text/xml;q=0.9");
		client.DefaultRequestHeaders.Accept.ParseAdd("*/*;q=0.5");
		return client;
	}

	private static string Describe(Exception e) => e switch
	{
		OperationCanceledException => "The feed took too long to respond",
		HttpRequestException { StatusCode: { } status } => $"The feed's server answered {(int)status}",
		HttpRequestException => "Couldn't reach the feed",
		XmlException or InvalidDataException => "That address isn't an RSS or Atom feed",
		_ => "Couldn't load the feed",
	};

	private static Uri? ParseFeedUrl(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return null;
		text = text.Trim();
		// "feed://" and "feed:https://" are ordinary feeds announced to feed readers
		if (text.StartsWith("feed:", StringComparison.OrdinalIgnoreCase)) text = text[5..].TrimStart('/');
		return NormalizeUrl(text);
	}

	#region Parsing

	private static Feed ParseFeed(byte[] bytes, Uri feedUri)
	{
		var settings = new XmlReaderSettings
		{
			// old RSS 0.91 feeds still carry a DOCTYPE: skip it instead of failing, and never resolve or expand it
			DtdProcessing = DtdProcessing.Ignore,
			IgnoreComments = true,
			IgnoreProcessingInstructions = true,
		};
		using var stream = new MemoryStream(bytes);
		using var reader = XmlReader.Create(stream, settings);
		var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("Empty document");

		XElement? channel;
		IEnumerable<XElement> entries;
		var isAtom = false;
		switch (root.Name.LocalName)
		{
			case "rss":
				channel = Child(root, "channel");
				entries = channel is null ? Enumerable.Empty<XElement>() : Children(channel, "item");
				break;
			case "RDF": // RSS 1.0 keeps its items next to the channel rather than inside it
				channel = Child(root, "channel");
				entries = Children(root, "item");
				break;
			case "feed":
				channel = root;
				entries = Children(root, "entry");
				isAtom = true;
				break;
			default:
				throw new InvalidDataException("Not an RSS or Atom feed");
		}

		var items = new List<FeedItem>();
		foreach (var entry in entries)
		{
			var link = isAtom ? AtomLink(entry, feedUri) : RssLink(entry, feedUri);
			var title = Clean(Child(entry, "title")?.Value);
			if (title.Length == 0)
			{
				if (link is null) continue;
				title = link.Host + link.AbsolutePath;
			}
			var source = Clean((isAtom ? Child(Child(entry, "source"), "title") : Child(entry, "source"))?.Value);
			var published = ParseDate(FirstValue(entry, "pubDate", "published", "updated", "date", "issued", "modified"));
			items.Add(new FeedItem(title, link, source, published));
			if (items.Count >= MaxItemsLimit) break;
		}

		// name the site each story lives on, unless they all live on the same one (then it's just noise)
		var hosts = items.Select(i => DisplayHost(i.Link)).ToList();
		if (hosts.Where(h => h.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
			for (var i = 0; i < items.Count; i++)
				if (items[i].Source.Length == 0)
					items[i] = items[i] with { Source = hosts[i] };

		var feedTitle = Clean(Child(channel, "title")?.Value);
		return new Feed(feedTitle.Length > 0 ? feedTitle : DisplayHost(feedUri), items);
	}

	private static Uri? RssLink(XElement item, Uri feedUri)
	{
		foreach (var link in Children(item, "link"))
		{
			// plain RSS puts the address in the text; an atom:link inside an item puts it in href
			var rel = (string?)link.Attribute("rel");
			if (rel is not (null or "alternate")) continue;
			if (ToWebUri((string?)link.Attribute("href") ?? link.Value, feedUri) is { } uri) return uri;
		}

		var guid = Child(item, "guid");
		if (guid is null || string.Equals((string?)guid.Attribute("isPermaLink"), "false", StringComparison.OrdinalIgnoreCase))
			return null;
		return ToWebUri(guid.Value, null);
	}

	private static Uri? AtomLink(XElement entry, Uri feedUri)
	{
		foreach (var link in Children(entry, "link"))
		{
			var rel = (string?)link.Attribute("rel");
			if (rel is not (null or "alternate")) continue;
			if (ToWebUri((string?)link.Attribute("href"), feedUri) is { } uri) return uri;
		}
		// some feeds only give an id, which is sometimes the page's address (and otherwise a tag: URI we skip)
		return ToWebUri(Child(entry, "id")?.Value, null);
	}

	/// <summary>Only http(s) links are ever opened; relative ones are resolved against the feed's address.</summary>
	private static Uri? ToWebUri(string? text, Uri? baseUri)
	{
		if (string.IsNullOrWhiteSpace(text)) return null;
		text = text.Trim();
		if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return uri;
		if (baseUri is not null && Uri.TryCreate(baseUri, text, out uri) && uri.Scheme is "http" or "https") return uri;
		return null;
	}

	private static XElement? Child(XElement? parent, string localName) =>
		parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

	private static IEnumerable<XElement> Children(XElement parent, string localName) =>
		parent.Elements().Where(e => e.Name.LocalName == localName);

	private static string? FirstValue(XElement parent, params string[] localNames) =>
		localNames.Select(n => Child(parent, n)?.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

	private static string DisplayHost(Uri? uri) =>
		uri is null ? "" : uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;

	private static string Clean(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return "";
		// titles are often HTML-escaped twice or carry inline markup (Atom type="html")
		text = WebUtility.HtmlDecode(HtmlTag.Replace(text, " "));
		return Whitespace.Replace(text, " ").Trim();
	}

	private static DateTimeOffset? ParseDate(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return null;
		text = text.Trim();

		// RFC 822 (RSS), e.g. "Tue, 08 Oct 2026 14:00:00 GMT": drop the weekday, turn named and +hhmm zones into +hh:mm
		var rfc = text;
		var comma = rfc.IndexOf(',');
		if (comma is > 0 and < 10) rfc = rfc[(comma + 1)..].Trim();
		var lastSpace = rfc.LastIndexOf(' ');
		if (lastSpace > 0 && ZoneOffsets.TryGetValue(rfc[(lastSpace + 1)..], out var offset))
			rfc = rfc[..(lastSpace + 1)] + offset;
		rfc = CompactOffset.Replace(rfc, "$1$2:$3");
		if (DateTimeOffset.TryParseExact(rfc, Rfc822Formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
			return parsed;

		// ISO 8601 (Atom, dc:date) and anything else .NET understands; times without a zone are taken as UTC
		return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out parsed)
			? parsed
			: null;
	}

	private static string Ago(DateTimeOffset when)
	{
		var age = DateTimeOffset.Now - when;
		if (age.TotalMinutes < 1) return "just now"; // also covers feeds whose clocks run ahead
		if (age.TotalHours < 1) return $"{(int)age.TotalMinutes}m ago";
		if (age.TotalDays < 1) return $"{(int)age.TotalHours}h ago";
		if (age.TotalDays < 7) return $"{(int)age.TotalDays}d ago";
		var local = when.ToLocalTime();
		return local.ToString(local.Year == DateTime.Now.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.CurrentCulture);
	}

	#endregion

	#region UI

	private void RenderItems()
	{
		ItemsPanel.Children.Clear();
		_metaLines.Clear();

		var primary = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		var secondary = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		foreach (var item in _items.Take(_maxItems))
			ItemsPanel.Children.Add(CreateRow(item, primary, secondary));

		_metaShownAt = DateTime.Now;
		UpdateStatus();
	}

	private Border CreateRow(FeedItem item, Brush primary, Brush secondary)
	{
		var title = new TextBlock
		{
			Text = item.Title, FontSize = 13, Foreground = primary,
			TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis,
		};
		var meta = new TextBlock
		{
			FontSize = 11, Foreground = secondary,
			TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
		};
		SetMetaText(meta, item);
		_metaLines.Add((item, meta));

		var row = new Border
		{
			Padding = new Thickness(6, 4, 6, 4), CornerRadius = new CornerRadius(6),
			Background = new SolidColorBrush(Colors.Transparent),
			Child = new StackPanel { Spacing = 1, Children = { title, meta } },
		};
		// the title may be cut to two lines, so the tooltip carries all of it
		ToolTipService.SetToolTip(row, item.Link is null ? item.Title : $"{item.Title}\n{item.Link.AbsoluteUri}");
		if (item.Link is not { } link) return row;

		row.PointerEntered += (_, _) => row.Background = new SolidColorBrush(CurrentTheme.PrimaryHighlightColorVeryTransparent);
		row.PointerExited += (_, _) => row.Background = new SolidColorBrush(Colors.Transparent);
		row.Tapped += (_, _) => OpenInNewTab(link.AbsoluteUri);
		return row;
	}

	private void SetMetaText(TextBlock meta, FeedItem item)
	{
		var when = _showTimes && item.Published is { } published ? Ago(published) : "";
		meta.Text = item.Source.Length > 0 && when.Length > 0 ? $"{item.Source} · {when}" : item.Source + when;
		meta.Visibility = meta.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
	}

	private void UpdateMetaLines()
	{
		foreach (var (item, meta) in _metaLines)
			SetMetaText(meta, item);
		_metaShownAt = DateTime.Now;
	}

	private void UpdateStatus()
	{
		FeedTitleText.Text = _feedTitle.Length > 0 ? _feedTitle : ParseFeedUrl(_feedUrl)?.Host ?? "News Feed";
		ToolTipService.SetToolTip(FeedTitleText, _itemsUrl ?? _feedUrl);

		var waiting = _loading || _urlEditedAt is not null;
		StatusText.Text = _items.Count > 0 ? "" : waiting ? "Loading feed..." : _error ?? "This feed has no items right now";
		StatusText.Visibility = _items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

		// after a failed refresh the last good items stay up; the icon and button tooltip say it's stale
		FeedIcon.Foreground = new SolidColorBrush(_error is null ? CurrentTheme.PrimaryHighlightColor : CurrentTheme.NoColor);
		RefreshButton.Opacity = waiting ? 0.45 : 1;
		ToolTipService.SetToolTip(RefreshButton, _error is null ? "Refresh" : $"Refresh (last try failed: {_error})");
	}

	private void RootGrid_OnSizeChanged(object sender, SizeChangedEventArgs e)
	{
		// in a sliver of height the stories matter more than the header; in a sliver of width the title does
		var showHeader = e.NewSize.Height >= 56;
		HeaderGrid.Visibility = showHeader ? Visibility.Visible : Visibility.Collapsed;
		RootGrid.RowSpacing = showHeader ? 4 : 0;
		RefreshButton.Visibility = e.NewSize.Width >= 90 ? Visibility.Visible : Visibility.Collapsed;
		FeedIcon.Visibility = e.NewSize.Width >= 140 ? Visibility.Visible : Visibility.Collapsed;
	}

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);
		FeedTitleText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		StatusText.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		RefreshButton.CurrentTheme = CurrentTheme;
		RenderItems();
	}

	#endregion
}

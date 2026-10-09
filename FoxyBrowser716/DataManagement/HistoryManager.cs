using FoxyBrowser716.DataObjects.Complex;

namespace FoxyBrowser716.DataManagement;

/// <summary>
/// Browsing history for one instance (History.json). One entry per URL; see <see cref="HistoryEntry"/>.
/// Recording happens from <see cref="WebviewTab"/> on the UI thread; saving happens on the auto-saver thread,
/// which is why the store is a <see cref="FoxyAutoSaverLockedList{T}"/>.
/// </summary>
public sealed class HistoryManager
{
	private readonly FoxyAutoSaverLockedList<HistoryEntry> _store;
	private readonly Dictionary<string, HistoryEntry> _byUrl = new(StringComparer.Ordinal);

	/// <summary>Raised after any change (on the thread that made it, normally the UI thread).</summary>
	public event Action? Changed;

	internal IFoxyAutoSaverItem SaverItem => _store;

	public HistoryManager(string instanceName)
	{
		_store = new FoxyAutoSaverLockedList<HistoryEntry>("History.json", FoxyFileManager.FolderType.Data, instanceName, SavePriority.Low);
	}

	/// <summary>Call once the store has loaded: drops expired entries and builds the URL index.</summary>
	internal void Initialize(int retentionDays)
	{
		_store.Read(items =>
		{
			_byUrl.Clear();
			foreach (var entry in items)
				_byUrl[entry.Url] = entry;
			return 0;
		});
		Prune(retentionDays);
	}

	/// <summary>Only real web pages and local files go into history.</summary>
	public static bool IsRecordable(string? url) =>
		Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file";

	public void RecordVisit(string url, string? title, string? favIconUrl)
	{
		if (!IsRecordable(url)) return;

		var now = DateTime.Now;
		_store.Mutate(items =>
		{
			if (_byUrl.TryGetValue(url, out var entry))
			{
				// reloads and quick back/forward hops should not inflate the visit count
				if (now - entry.LastVisited > TimeSpan.FromMinutes(1))
					entry.VisitCount++;
				entry.LastVisited = now;
				if (!string.IsNullOrWhiteSpace(title)) entry.Title = title;
				if (!string.IsNullOrWhiteSpace(favIconUrl)) entry.FavIconUrl = favIconUrl;
			}
			else
			{
				entry = new HistoryEntry
				{
					Url = url,
					Title = title ?? string.Empty,
					FavIconUrl = favIconUrl ?? string.Empty,
					FirstVisited = now,
					LastVisited = now,
					VisitCount = 1,
				};
				items.Add(entry);
				_byUrl[url] = entry;
			}
		});
		Changed?.Invoke();
	}

	/// <summary>Fills in the title/icon of an entry once the page reports them (they arrive after the visit).</summary>
	public void UpdateDetails(string url, string? title, string? favIconUrl)
	{
		var changed = false;
		_store.Mutate(_ =>
		{
			if (!_byUrl.TryGetValue(url, out var entry)) return;
			if (!string.IsNullOrWhiteSpace(title) && entry.Title != title) { entry.Title = title; changed = true; }
			if (!string.IsNullOrWhiteSpace(favIconUrl) && entry.FavIconUrl != favIconUrl) { entry.FavIconUrl = favIconUrl; changed = true; }
		});
		if (changed) Changed?.Invoke();
	}

	/// <summary>Entries newest first, optionally filtered (every word of <paramref name="filter"/> must match the title or URL).</summary>
	public List<HistoryEntry> GetEntries(string? filter = null)
	{
		var words = SplitWords(filter);
		return _store.Read(items => items
			.Where(e => Matches(e, words))
			.OrderByDescending(e => e.LastVisited)
			.ToList());
	}

	/// <summary>Best matches for the address bar: favors URLs that start with the query, frequent and recent visits.</summary>
	public List<HistoryEntry> Search(string query, int max)
	{
		var words = SplitWords(query);
		if (words.Length == 0) return [];

		var now = DateTime.Now;
		var trimmed = query.Trim();
		return _store.Read(items => items
			.Where(e => Matches(e, words))
			.Select(e => (entry: e, score: Score(e, trimmed, now)))
			.OrderByDescending(p => p.score)
			.Take(max)
			.Select(p => p.entry)
			.ToList());
	}

	public void Remove(string url)
	{
		_store.Mutate(items =>
		{
			if (_byUrl.Remove(url, out var entry))
				items.Remove(entry);
		});
		Changed?.Invoke();
	}

	public void Clear()
	{
		_store.Mutate(items =>
		{
			items.Clear();
			_byUrl.Clear();
		});
		Changed?.Invoke();
	}

	/// <summary>Removes entries not visited within <paramref name="retentionDays"/> days (0 or less keeps everything).</summary>
	public void Prune(int retentionDays)
	{
		if (retentionDays <= 0) return;

		var cutoff = DateTime.Now.AddDays(-retentionDays);
		var removed = 0;
		_store.Mutate(items =>
		{
			removed = items.RemoveAll(e => e.LastVisited < cutoff);
			if (removed > 0)
				foreach (var url in _byUrl.Where(p => p.Value.LastVisited < cutoff).Select(p => p.Key).ToList())
					_byUrl.Remove(url);
		});
		if (removed > 0) Changed?.Invoke();
	}

	private static string[] SplitWords(string? text) =>
		string.IsNullOrWhiteSpace(text)
			? []
			: text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	private static bool Matches(HistoryEntry entry, string[] words) =>
		words.All(w => entry.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase)
		               || entry.Url.Contains(w, StringComparison.OrdinalIgnoreCase));

	private static double Score(HistoryEntry entry, string query, DateTime now)
	{
		var score = Math.Log(1 + entry.VisitCount) - (now - entry.LastVisited).TotalDays / 7;

		if (Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri))
		{
			var host = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;
			if (host.StartsWith(query, StringComparison.OrdinalIgnoreCase)) score += 5;
			else if (uri.Host.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 2;
		}
		if (entry.Title.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) score += 1;

		return score;
	}
}

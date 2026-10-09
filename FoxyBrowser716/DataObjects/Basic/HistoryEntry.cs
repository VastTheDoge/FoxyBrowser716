namespace FoxyBrowser716.DataObjects.Basic;

/// <summary>
/// One visited URL. History keeps a single entry per URL (updated on every visit) rather than one per visit,
/// which keeps History.json small and makes search ranking by visit count cheap.
/// </summary>
public class HistoryEntry
{
	public string Url { get; set; } = string.Empty;
	public string Title { get; set; } = string.Empty;
	public string FavIconUrl { get; set; } = string.Empty;
	public DateTime FirstVisited { get; set; }
	public DateTime LastVisited { get; set; }
	public int VisitCount { get; set; }
}

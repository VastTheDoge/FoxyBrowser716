using FoxyBrowser716.ErrorHandeler;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace FoxyBrowser716.Controls.WebUi;

/// <summary>
/// Shows site prompts (<see cref="WebPromptSpec"/>) for one window. Prompts are queued per tab and only the
/// active tab's oldest prompt is visible, so a background tab's alert waits until you switch to it (the page
/// stays paused meanwhile, as in other browsers). Prompts never light-dismiss; closing the tab cancels them.
/// </summary>
public sealed class WebPromptHost
{
	private const double CardWidth = 420;

	private readonly Popup _popup;
	private readonly Func<Rect> _getBounds;
	private readonly Dictionary<int, List<WebPromptSpec>> _queues = [];
	private int _activeTabId = -1;
	private WebPromptSpec? _shownSpec;
	private WebPromptCard? _shownCard;

	public Theme CurrentTheme
	{
		get;
		set
		{
			field = value;
			if (_shownCard is not null) _shownCard.CurrentTheme = value;
		}
	} = DefaultThemes.DarkMode;

	/// <param name="host">Panel the popup is added to (popup offsets are relative to it).</param>
	/// <param name="getBounds">Area to center prompts in, in <paramref name="host"/> coordinates (the tab content area).</param>
	public WebPromptHost(Panel host, Func<Rect> getBounds)
	{
		_getBounds = getBounds;
		_popup = new Popup
		{
			IsLightDismissEnabled = false,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
		};
		host.Children.Add(_popup);
	}

	public bool HasPrompts(int tabId) => _queues.TryGetValue(tabId, out var queue) && queue.Count > 0;

	public void Enqueue(int tabId, WebPromptSpec spec)
	{
		if (!_queues.TryGetValue(tabId, out var queue))
			_queues[tabId] = queue = [];
		queue.Add(spec);
		Refresh();
	}

	public void SetActiveTab(int tabId)
	{
		_activeTabId = tabId;
		Refresh();
	}

	/// <summary>Cancels every prompt of a tab that is closing.</summary>
	public void CancelTab(int tabId)
	{
		if (!_queues.Remove(tabId, out var queue)) return;
		foreach (var spec in queue)
			SafeCancel(spec);
		Refresh();
	}

	public void CancelAll()
	{
		foreach (var tabId in _queues.Keys.ToList())
			CancelTab(tabId);
	}

	/// <summary>Re-centers the visible prompt (call when the window or tab area resizes).</summary>
	public void Reposition()
	{
		if (!_popup.IsOpen || _shownCard is null) return;

		var bounds = _getBounds();
		var width = Math.Min(CardWidth, Math.Max(200, bounds.Width - 16));
		_shownCard.Width = width;
		_popup.HorizontalOffset = bounds.X + (bounds.Width - width) / 2;
		_popup.VerticalOffset = bounds.Y + 8;
	}

	private void Refresh()
	{
		var next = _queues.TryGetValue(_activeTabId, out var queue) && queue.Count > 0 ? queue[0] : null;

		if (ReferenceEquals(next, _shownSpec))
		{
			Reposition();
			return;
		}

		_popup.IsOpen = false;
		_popup.Child = null;
		_shownSpec = next;
		_shownCard = null;

		if (next is null) return;

		var tabId = _activeTabId;
		var card = new WebPromptCard(next) { CurrentTheme = CurrentTheme };
		card.ButtonClicked += (button, result) =>
		{
			if (_queues.TryGetValue(tabId, out var tabQueue))
			{
				tabQueue.Remove(next);
				if (tabQueue.Count == 0) _queues.Remove(tabId);
			}

			try { button.Clicked(result); }
			catch (Exception e) { FoxyLogger.AddError(e); }

			Refresh();
		};

		_shownCard = card;
		_popup.Child = card;
		_popup.IsOpen = true;
		Reposition();
	}

	private static void SafeCancel(WebPromptSpec spec)
	{
		// the WebView may already be closed, which makes completing its deferral throw
		try { spec.Cancelled(); }
		catch (Exception e) { FoxyLogger.AddWarning("Cancelling a site prompt failed", e.Message); }
	}
}

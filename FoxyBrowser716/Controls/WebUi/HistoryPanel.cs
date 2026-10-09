using System.Globalization;
using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.Controls.Helpers;
using FoxyBrowser716.DataManagement;
using FoxyBrowser716.DataObjects.Settings;
using Material.Icons.WinUI3;
using Microsoft.UI.Dispatching;

namespace FoxyBrowser716.Controls.WebUi;

/// <summary>Browsable, searchable history for one instance, grouped by day (newest first).</summary>
public sealed partial class HistoryPanel : ThemedUserControl
{
	private const int PageSize = 150;

	private readonly HistoryManager _history;
	private readonly Action<string> _openUrl;
	private readonly Border _card;
	private readonly TextBlock _title;
	private readonly FTextInput _search;
	private readonly FTextButton _clearButton;
	private readonly TextBlock _emptyText;
	private readonly StackPanel _list;
	private readonly FTextButton _moreButton;
	private readonly DispatcherQueueTimer _refreshTimer;
	private readonly List<ThemedUserControl> _rows = [];
	private readonly List<TextBlock> _dayHeaders = [];
	private int _shownCount = PageSize;

	/// <param name="openUrl">Opens a history entry (the window opens it in a new tab).</param>
	/// <param name="clearEngineHistory">Also clears WebView2's own history (visited-link colors) on "Clear history".</param>
	public HistoryPanel(HistoryManager history, Action<string> openUrl, Func<Task>? clearEngineHistory = null)
	{
		_history = history;
		_openUrl = openUrl;

		_title = WebUiStyle.Text("History", 16, bold: true, wrap: false);
		_clearButton = WebUiStyle.TextButton("Clear history", () => { }, MaterialIconKind.DeleteSweep);
		WebUiStyle.MakeConfirming(_clearButton, "Click again to clear all", async () =>
		{
			_history.Clear();
			try
			{
				if (clearEngineHistory is not null) await clearEngineHistory();
			}
			catch (Exception e)
			{
				ErrorHandeler.FoxyLogger.AddError(e);
			}
		});

		var header = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
				new ColumnDefinition { Width = GridLength.Auto },
			},
		};
		Grid.SetColumn(_clearButton, 1);
		header.Children.Add(_title);
		header.Children.Add(_clearButton);

		_search = new FTextInput { AcceptsReturn = false, PlaceHolderText = "Search history", MinHeight = 28 };
		_search.OnTextChanged += _ =>
		{
			_shownCount = PageSize;
			Rebuild();
		};

		_emptyText = WebUiStyle.Text(string.Empty, 13);
		_emptyText.Margin = new Thickness(4, 8, 4, 8);

		_list = new StackPanel { Spacing = 2 };
		_moreButton = WebUiStyle.TextButton("Show more", () =>
		{
			_shownCount += PageSize;
			Rebuild();
		});
		_moreButton.HorizontalAlignment = HorizontalAlignment.Center;

		var listHost = new StackPanel();
		listHost.Children.Add(_list);
		listHost.Children.Add(_moreButton);

		var layout = new Grid
		{
			RowSpacing = 6,
			RowDefinitions =
			{
				new RowDefinition { Height = GridLength.Auto },
				new RowDefinition { Height = GridLength.Auto },
				new RowDefinition { Height = GridLength.Auto },
				new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
			},
		};
		var scroll = new ScrollViewer
		{
			Content = listHost,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
		};
		Grid.SetRow(_search, 1);
		Grid.SetRow(_emptyText, 2);
		Grid.SetRow(scroll, 3);
		layout.Children.Add(header);
		layout.Children.Add(_search);
		layout.Children.Add(_emptyText);
		layout.Children.Add(scroll);

		_card = WebUiStyle.Card(layout, 8);
		Content = _card;

		// history changes while you browse in other tabs; batch those into one rebuild
		_refreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
		_refreshTimer.Interval = TimeSpan.FromMilliseconds(400);
		_refreshTimer.IsRepeating = false;
		_refreshTimer.Tick += (_, _) => Rebuild();

		Loaded += (_, _) =>
		{
			_history.Changed += HistoryOnChanged;
			Rebuild();
			_search.FocusInput();
		};
		Unloaded += (_, _) =>
		{
			_history.Changed -= HistoryOnChanged;
			_refreshTimer.Stop();
		};

		ApplyTheme();
	}

	/// <summary>Pre-fills the search box (used by "Search history for ..." in the address bar suggestions).</summary>
	public void SetQuery(string query)
	{
		_search.SetText(query);
		_shownCount = PageSize;
		if (IsLoaded) Rebuild();
	}

	private void HistoryOnChanged()
	{
		// Changed can be raised from any thread that records history
		DispatcherQueue.TryEnqueue(() =>
		{
			_refreshTimer.Stop();
			_refreshTimer.Start();
		});
	}

	private void Rebuild()
	{
		_list.Children.Clear();
		_rows.Clear();
		_dayHeaders.Clear();

		var query = _search.CurrentText;
		var entries = _history.GetEntries(query);

		_emptyText.Text = string.IsNullOrWhiteSpace(query) ? "No history yet." : $"No history matches \"{query.Trim()}\".";
		_emptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

		DateTime? currentDay = null;
		foreach (var entry in entries.Take(_shownCount))
		{
			if (currentDay != entry.LastVisited.Date)
			{
				currentDay = entry.LastVisited.Date;
				var dayHeader = WebUiStyle.Text(FormatDay(currentDay.Value), 12, bold: true, wrap: false);
				dayHeader.Margin = new Thickness(4, _dayHeaders.Count == 0 ? 0 : 8, 0, 2);
				dayHeader.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
				_dayHeaders.Add(dayHeader);
				_list.Children.Add(dayHeader);
			}

			var row = new HistoryRow(entry,
				() => _openUrl(entry.Url),
				() => _history.Remove(entry.Url)) { CurrentTheme = CurrentTheme };
			_rows.Add(row);
			_list.Children.Add(row);
		}

		_moreButton.Visibility = entries.Count > _shownCount ? Visibility.Visible : Visibility.Collapsed;
	}

	private static string FormatDay(DateTime day)
	{
		if (day == DateTime.Today) return "Today";
		if (day == DateTime.Today.AddDays(-1)) return "Yesterday";
		return day.ToString(day.Year == DateTime.Today.Year ? "dddd, MMMM d" : "D", CultureInfo.CurrentCulture);
	}

	protected override void ApplyTheme()
	{
		if (_card is null) return;

		WebUiStyle.ApplyCardTheme(_card, CurrentTheme, solid: false);
		_title.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		_emptyText.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		_search.CurrentTheme = CurrentTheme;
		_clearButton.CurrentTheme = WebUiStyle.DangerTheme(CurrentTheme) with { PrimaryHighlightColor = CurrentTheme.NoColorVeryTransparent };
		WebUiStyle.ThemeIcon(_clearButton.Icon, CurrentTheme.PrimaryForegroundColor);
		_moreButton.CurrentTheme = CurrentTheme;
		foreach (var header in _dayHeaders) header.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		foreach (var row in _rows) row.CurrentTheme = CurrentTheme;
	}
}

/// <summary>One history entry: favicon, title, URL, time and a remove button. Clicking opens it.</summary>
public sealed partial class HistoryRow : ThemedUserControl
{
	private readonly Grid _root;
	private readonly TextBlock _title;
	private readonly TextBlock _url;
	private readonly TextBlock _time;
	private readonly FIconButton _remove;
	private bool _pointerOver;

	public HistoryRow(HistoryEntry entry, Action open, Action remove)
	{
		var icon = new Viewbox
		{
			Width = 18,
			Height = 18,
			Margin = new Thickness(2, 0, 6, 0),
			VerticalAlignment = VerticalAlignment.Center,
			Child = UrlToImageControlConverter.StaticConvert(entry.FavIconUrl),
		};

		_title = WebUiStyle.Text(string.IsNullOrWhiteSpace(entry.Title) ? entry.Url : entry.Title, 13, bold: true, wrap: false);
		_url = WebUiStyle.Text(entry.Url, 11, wrap: false);
		var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
		text.Children.Add(_title);
		text.Children.Add(_url);

		_time = WebUiStyle.Text(entry.LastVisited.ToShortTimeString(), 11, wrap: false);
		_time.Margin = new Thickness(6, 0, 2, 0);

		_remove = WebUiStyle.IconButton(MaterialIconKind.Close, remove, 22, 3);

		_root = new Grid
		{
			Padding = new Thickness(3),
			CornerRadius = new CornerRadius(6),
			BorderThickness = new Thickness(2),
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = GridLength.Auto },
			},
		};
		Grid.SetColumn(text, 1);
		Grid.SetColumn(_time, 2);
		Grid.SetColumn(_remove, 3);
		_root.Children.Add(icon);
		_root.Children.Add(text);
		_root.Children.Add(_time);
		_root.Children.Add(_remove);
		Content = _root;

		_root.PointerEntered += (_, _) => { _pointerOver = true; ChangeColorAnimation(_root.Background, CurrentTheme.PrimaryAccentColorSlightTransparent); };
		_root.PointerExited += (_, _) => { _pointerOver = false; ChangeColorAnimation(_root.Background, CurrentTheme.PrimaryBackgroundColorVeryTransparent); };
		_root.PointerReleased += (_, e) =>
		{
			if (_remove.PointerOver) return;
			if (e.GetCurrentPoint(_root).Properties.PointerUpdateKind != Microsoft.UI.Input.PointerUpdateKind.LeftButtonReleased) return;
			open();
		};

		ApplyTheme();
	}

	protected override void ApplyTheme()
	{
		if (_root is null) return;

		_root.Background = new SolidColorBrush(_pointerOver ? CurrentTheme.PrimaryAccentColorSlightTransparent : CurrentTheme.PrimaryBackgroundColorVeryTransparent);
		_root.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColor);
		_title.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		_url.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		_time.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		_remove.CurrentTheme = WebUiStyle.DangerTheme(CurrentTheme);
	}
}

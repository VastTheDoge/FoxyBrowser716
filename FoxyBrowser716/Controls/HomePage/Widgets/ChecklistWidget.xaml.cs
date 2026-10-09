using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.DataObjects.Settings;
using Material.Icons.WinUI3;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

// The list lives in the "Items" setting as "[ ] text" / "[x] text" lines, which doubles as a bulk editor in the settings popup.
[WidgetInfo("Checklist Widget", MaterialIconKind.FormatListChecks, WidgetCategory.Tools)]
public partial class ChecklistWidget : WidgetBase
{
	private static readonly char[] LineBreaks = ['\r', '\n'];

	private sealed class ChecklistItem(string text, bool done)
	{
		public string Text { get; } = text;
		public bool Done { get; set; } = done;
	}

	private readonly List<ChecklistItem> _items = [];
	private readonly StringSetting _itemsSetting;
	private string _itemsText = ""; // the Items text that _items currently reflects

	protected ChecklistWidget()
	{
		InitializeComponent();
		// Enter adds the item rather than starting a new line
		AddInput.AcceptsReturn = false;

		_itemsSetting = new StringSetting("Items", "One item per line: \"[ ] text\", or \"[x] text\" once it's done. You can also edit the list right on the widget.", "", v =>
		{
			// the widget writes this setting itself (Commit); only re-read text that came from elsewhere (a saved layout, the settings box)
			v ??= "";
			if (v == _itemsText) return;
			_itemsText = v;
			_items.Clear();
			_items.AddRange(Parse(v));
			RebuildRows();
		}, multiline: true);

		WidgetSettings =
		[
			new StringSetting("Title", "", "To-do", v => TitleText.Text = v ?? ""),
			_itemsSetting,
		];
	}

	protected override Task Initialize()
	{
		ApplyTheme();
		return Task.CompletedTask;
	}

	#region Items text

	private static IEnumerable<ChecklistItem> Parse(string text)
	{
		// a TextBox breaks lines with "\r", the saved text uses "\n": accept either (and "\r\n")
		foreach (var rawLine in text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries))
		{
			var line = rawLine.Trim();
			// tolerate pasted markdown ("- [ ] milk", "* eggs")
			if (line.Length > 1 && (line[0] is '-' or '*' or '+') && line[1] == ' ') line = line[2..].TrimStart();

			var done = false;
			if (line.StartsWith('['))
			{
				var close = line.IndexOf(']');
				var mark = close > 0 ? line[1..close].Trim() : null;
				// only a real checkbox is syntax: "[WIP] fix the build" keeps its text
				if (mark is "" or "x" or "X" or "*")
				{
					done = mark != "";
					line = line[(close + 1)..].Trim();
				}
			}

			if (line.Length > 0) yield return new ChecklistItem(line, done);
		}
	}

	private static string Serialize(IEnumerable<ChecklistItem> items) =>
		string.Join("\n", items.Select(i => (i.Done ? "[x] " : "[ ] ") + i.Text));

	/// <summary>Writes the list back to the Items setting and saves it (changes on the widget happen outside edit mode).</summary>
	private void Commit()
	{
		_itemsText = Serialize(_items);
		_itemsSetting.Value = _itemsText; // the setting callback sees its own text and skips the re-parse
		UpdateHeader();
		RequestSave();
	}

	#endregion

	#region UI

	private void AddInput_OnEnterPressed()
	{
		// items are one line each; a pasted multi-line string becomes one item
		var text = AddInput.CurrentText.Replace('\r', ' ').Replace('\n', ' ').Trim();
		AddInput.SetText("");
		if (text.Length == 0) return;

		_items.Add(new ChecklistItem(text, false));
		RebuildRows();
		Commit();
		// new items go to the bottom; bring the latest into view
		Scroller.UpdateLayout();
		Scroller.ChangeView(null, Scroller.ScrollableHeight, null);
	}

	private void ClearButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (_items.RemoveAll(i => i.Done) == 0) return;
		RebuildRows();
		Commit();
	}

	private void RebuildRows()
	{
		ItemsPanel.Children.Clear();
		foreach (var item in _items)
			ItemsPanel.Children.Add(CreateRow(item));
		UpdateHeader();
	}

	private Border CreateRow(ChecklistItem item)
	{
		var check = new MaterialIcon { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
		var text = new TextBlock
		{
			Text = item.Text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
			TextWrapping = TextWrapping.Wrap, MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis,
		};
		ApplyItemState(check, text, item.Done);

		// checkbox and text share one hit area so clicking the words toggles too; the delete button sits outside it
		var toggleArea = new Grid
		{
			ColumnSpacing = 6, Background = new SolidColorBrush(Colors.Transparent),
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
			},
		};
		Grid.SetColumn(text, 1);
		toggleArea.Children.Add(check);
		toggleArea.Children.Add(text);
		toggleArea.Tapped += (_, _) =>
		{
			item.Done = !item.Done;
			ApplyItemState(check, text, item.Done);
			Commit();
		};

		var delete = new FIconButton
		{
			Width = 22, Height = 22, Padding = new Thickness(3), VerticalAlignment = VerticalAlignment.Top,
			Content = new MaterialIcon { Kind = MaterialIconKind.Close },
			CurrentTheme = CurrentTheme, Visibility = Visibility.Collapsed,
		};
		ToolTipService.SetToolTip(delete, "Delete");
		delete.OnClick += (_, _) =>
		{
			_items.Remove(item);
			RebuildRows();
			Commit();
		};

		var layout = new Grid
		{
			ColumnSpacing = 2,
			// the delete button's slot is reserved so the text doesn't re-wrap when it appears on hover
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
				new ColumnDefinition { Width = new GridLength(22) },
			},
		};
		Grid.SetColumn(delete, 1);
		layout.Children.Add(toggleArea);
		layout.Children.Add(delete);

		var row = new Border
		{
			Padding = new Thickness(4, 3, 2, 3), CornerRadius = new CornerRadius(6),
			Background = new SolidColorBrush(Colors.Transparent), Child = layout,
		};
		row.PointerEntered += (_, _) =>
		{
			row.Background = new SolidColorBrush(CurrentTheme.PrimaryHighlightColorVeryTransparent);
			delete.Visibility = Visibility.Visible;
		};
		row.PointerExited += (_, _) =>
		{
			row.Background = new SolidColorBrush(Colors.Transparent);
			delete.Visibility = Visibility.Collapsed;
		};
		return row;
	}

	private void ApplyItemState(MaterialIcon check, TextBlock text, bool done)
	{
		check.Kind = done ? MaterialIconKind.CheckboxMarked : MaterialIconKind.CheckboxBlankOutline;
		check.Foreground = new SolidColorBrush(done ? CurrentTheme.PrimaryHighlightColor : CurrentTheme.SecondaryForegroundColor);
		text.TextDecorations = done ? global::Windows.UI.Text.TextDecorations.Strikethrough : global::Windows.UI.Text.TextDecorations.None;
		text.Foreground = new SolidColorBrush(done ? CurrentTheme.SecondaryForegroundColor : CurrentTheme.PrimaryForegroundColor);
		text.Opacity = done ? 0.75 : 1;
	}

	private void UpdateHeader()
	{
		var done = _items.Count(i => i.Done);
		CountText.Text = _items.Count == 0 ? "" : $"{done}/{_items.Count}";
		ClearButton.Opacity = done > 0 ? 1 : 0.35;
		UpdateLayoutForSize();
	}

	private void RootGrid_OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutForSize();

	private void UpdateLayoutForSize()
	{
		var width = RootGrid.ActualWidth;
		var height = RootGrid.ActualHeight;
		if (width <= 0 || height <= 0) return;

		// as the widget gets shorter the add box goes first, then the list, and the header stays longest;
		// an empty list keeps the add box instead, since that's the only useful thing to show
		var hasItems = _items.Count > 0;
		var showList = hasItems && height >= 60;
		var showInput = height >= (hasItems ? 96 : 60);
		AddInput.Visibility = showInput ? Visibility.Visible : Visibility.Collapsed;
		Scroller.Visibility = showList ? Visibility.Visible : Visibility.Collapsed;
		EmptyText.Visibility = !hasItems && height >= 100 ? Visibility.Visible : Visibility.Collapsed;
		CountText.Visibility = width >= 80 ? Visibility.Visible : Visibility.Collapsed;
		ClearButton.Visibility = width >= 110 ? Visibility.Visible : Visibility.Collapsed;
	}

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);
		TitleText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		CountText.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		EmptyText.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		ClearButton.CurrentTheme = CurrentTheme;
		AddInput.CurrentTheme = CurrentTheme;
		RebuildRows();
	}

	#endregion
}

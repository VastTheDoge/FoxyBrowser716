using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.Controls.WebUi;
using FoxyBrowser716.DataManagement;
using FoxyBrowser716.DataObjects.Settings;
using Material.Icons.WinUI3;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Storage.Pickers;
using WinRT.Interop;
using WinUIEx;

namespace FoxyBrowser716.Controls.SettingsPage.SettingsCustomControls;

// Claude Doc
/// <summary>
/// Settings section for themes: a gallery of built-in and custom themes, each shown as a miniature window
/// over this window's home picture (click to use), and an editor for custom ones. Editing changes nothing
/// outside the editor until the theme is saved, and a saved theme only shows once it is the one in use.
/// </summary>
public sealed partial class ThemeController : ThemedUserControl
{
	private static readonly (string Label, string Hint, Func<Theme, Color> Get, Func<Theme, Color, Theme> Set)[] Slots =
	[
		("Primary background", "Bars, cards and panels", t => t.PrimaryBackgroundColor, (t, c) => t with { PrimaryBackgroundColor = c }),
		("Secondary background", "Borders of bars and cards", t => t.SecondaryBackgroundColor, (t, c) => t with { SecondaryBackgroundColor = c }),
		("Primary foreground", "Text and icons", t => t.PrimaryForegroundColor, (t, c) => t with { PrimaryForegroundColor = c }),
		("Secondary foreground", "Dimmer text: descriptions, placeholders", t => t.SecondaryForegroundColor, (t, c) => t with { SecondaryForegroundColor = c }),
		("Primary accent", "Buttons and input boxes", t => t.PrimaryAccentColor, (t, c) => t with { PrimaryAccentColor = c }),
		("Secondary accent", "Borders of buttons and inputs", t => t.SecondaryAccentColor, (t, c) => t with { SecondaryAccentColor = c }),
		("Primary highlight", "Window frame, selected and pressed buttons", t => t.PrimaryHighlightColor, (t, c) => t with { PrimaryHighlightColor = c }),
		("Secondary highlight", "Selected text, the update button", t => t.SecondaryHighlightColor, (t, c) => t with { SecondaryHighlightColor = c }),
		("Yes", "Allow, success", t => t.YesColor, (t, c) => t with { YesColor = c }),
		("No", "Block, delete, errors", t => t.NoColor, (t, c) => t with { NoColor = c }),
	];

	private const double CardPreviewWidth = 212;
	private const double EditorPreviewWidth = 400;

	private enum View { Gallery, Editor }

	private readonly MainWindow.MainWindow _mainWindow;
	private readonly StackPanel _root = new() { Spacing = 8, Margin = new Thickness(0, 6, 0, 6) };
	private ImageSource? _homeImage;
	private View _view = View.Gallery;
	private bool _refreshQueued;

	//C each view is rebuilt from scratch, so only the elements on screen are tracked for theming
	private readonly List<(FTextButton button, bool danger)> _buttons = [];
	private readonly List<FIconButton> _iconButtons = [];
	private readonly List<TextBlock> _primaryText = [];
	private readonly List<TextBlock> _secondaryText = [];
	private readonly List<(Border card, bool selected)> _cards = [];
	private readonly List<Border> _borders = [];
	private readonly List<ThemePreview> _previews = [];
	private readonly List<FTextInput> _inputs = [];
	private readonly List<MaterialIcon> _icons = [];

	private string? _editingId;
	private string _draftName = "";
	private Theme _draft;
	private ThemePreview? _editorPreview;
	private readonly Border[] _swatches = new Border[Slots.Length];
	private readonly TextBlock[] _hexLabels = new TextBlock[Slots.Length];
	private FColorPicker? _picker;
	private Border? _pickerCard;
	private Flyout? _pickerFlyout;
	private int _activeSlot;

	public ThemeController(MainWindow.MainWindow mainWindow)
	{
		_mainWindow = mainWindow;
		Content = _root;
		SetHomeImage(mainWindow.HomeImageUri);

		//C only listen while shown, so closed windows do not stay referenced by the static event
		Loaded += (_, _) =>
		{
			ThemeManager.Changed += QueueGalleryRefresh;
			_mainWindow.Instance.Settings.PropertyChanged += OnSettingsChanged;
			_mainWindow.HomeImageChanged += SetHomeImage;
			//C the home page may have loaded its picture between construction and now, while nothing was listening
			SetHomeImage(_mainWindow.HomeImageUri);
			ShowGallery();
		};
		Unloaded += (_, _) =>
		{
			ThemeManager.Changed -= QueueGalleryRefresh;
			_mainWindow.Instance.Settings.PropertyChanged -= OnSettingsChanged;
			_mainWindow.HomeImageChanged -= SetHomeImage;
		};
	}

	private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(BrowserSettings.ThemeName)) QueueGalleryRefresh();
	}

	//C a save and a theme switch can land together; one rebuild after both is enough
	private void QueueGalleryRefresh()
	{
		if (_refreshQueued) return;
		_refreshQueued = true;
		DispatcherQueue.TryEnqueue(() =>
		{
			_refreshQueued = false;
			if (_view == View.Gallery) ShowGallery();
		});
	}

	private void SetHomeImage(Uri? uri)
	{
		_homeImage = uri is null ? null : new BitmapImage(uri);
		foreach (var preview in _previews) preview.BackgroundImage = _homeImage;
	}

	private void Clear()
	{
		_root.Children.Clear();
		_buttons.Clear();
		_iconButtons.Clear();
		_primaryText.Clear();
		_secondaryText.Clear();
		_cards.Clear();
		_borders.Clear();
		_previews.Clear();
		_inputs.Clear();
		_icons.Clear();
		_editorPreview = null;
	}

	#region Gallery
	private void ShowGallery()
	{
		_view = View.Gallery;
		Clear();

		var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
		toolbar.Children.Add(Button("New theme", () => OpenEditor(null, "My theme", _mainWindow.Instance.CurrentTheme), MaterialIconKind.Plus));
		toolbar.Children.Add(Button("Import…", Import, MaterialIconKind.Import));
		_root.Children.Add(toolbar);

		//C card = preview + name, kind and buttons rows + padding, border and margin; the grid needs the total up front
		var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = CardPreviewWidth + 28, ItemHeight = CardPreviewWidth * DesignRatio + 108 };
		var current = _mainWindow.Instance.Settings.ThemeName;
		foreach (var entry in ThemeManager.All)
			grid.Children.Add(Card(entry, string.Equals(entry.Id, current, StringComparison.OrdinalIgnoreCase)));
		_root.Children.Add(grid);

		ApplyTheme();
	}

	private static double DesignRatio => ThemePreview.DesignHeight / ThemePreview.DesignWidth;

	private Border Card(ThemeManager.Entry entry, bool selected)
	{
		var preview = Preview(entry.Theme, CardPreviewWidth);
		preview.PointerReleased += (_, _) => Use(entry);

		var nameRow = new Grid { ColumnSpacing = 6 };
		nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		var name = Primary(entry.Name, 14, bold: true);
		name.TextTrimming = TextTrimming.CharacterEllipsis;
		nameRow.Children.Add(name);
		if (selected)
		{
			var check = new MaterialIcon { Kind = MaterialIconKind.Check, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
			Grid.SetColumn(check, 1);
			_icons.Add(check);
			nameRow.Children.Add(check);
		}

		var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
		var use = Button(selected ? "In use" : "Use", () => Use(entry));
		use.ForceHighlight = selected;
		actions.Children.Add(use);
		if (!entry.IsBuiltIn)
			actions.Children.Add(IconButton(MaterialIconKind.Pencil, () => OpenEditor(entry.Id, entry.Name, entry.Theme), "Edit"));
		actions.Children.Add(IconButton(MaterialIconKind.ContentCopy, () => OpenEditor(null, $"{entry.Name} copy", entry.Theme), entry.IsBuiltIn ? "Make an editable copy" : "Make a copy"));
		actions.Children.Add(IconButton(MaterialIconKind.Export, () => Export(entry), "Export as a .foxytheme file"));
		if (!entry.IsBuiltIn)
		{
			var delete = Button("Delete", () => { }, danger: true);
			WebUiStyle.MakeConfirming(delete, "Sure?", () => Delete(entry));
			actions.Children.Add(delete);
		}

		var card = new Border
		{
			CornerRadius = new CornerRadius(10),
			BorderThickness = new Thickness(2),
			Padding = new Thickness(8),
			Margin = new Thickness(4),
			Child = new StackPanel
			{
				Spacing = 4,
				Children = { preview, nameRow, Secondary(entry.IsBuiltIn ? "Built-in" : "Custom", 11), actions },
			},
		};
		_cards.Add((card, selected));
		return card;
	}

	private void Use(ThemeManager.Entry entry) => _mainWindow.Instance.Settings.ThemeName = entry.Id;

	private void Delete(ThemeManager.Entry entry)
	{
		//C every instance using it falls back to the first built-in, so none keeps a dead id in its settings
		foreach (var instance in AppServer.Instances)
			if (string.Equals(instance.Settings.ThemeName, entry.Id, StringComparison.OrdinalIgnoreCase))
				instance.Settings.ThemeName = ThemeManager.BuiltIn[0].Id;
		ThemeManager.Delete(entry.Id);
		_mainWindow.ShowToast("Theme deleted", entry.Name, MaterialIconKind.Palette);
	}

	private async void Import()
	{
		var picker = new FileOpenPicker();
		picker.FileTypeFilter.Add(ThemeManager.FileExtension);
		picker.FileTypeFilter.Add(".json");
		InitializeWithWindow.Initialize(picker, _mainWindow.GetWindowHandle());

		var file = await picker.PickSingleFileAsync();
		if (file is null) return;

		if (ThemeManager.Import(file.Path) is { } entry)
			_mainWindow.ShowToast("Theme imported", entry.Name, MaterialIconKind.Palette);
		else
			_mainWindow.ShowToast("Couldn't import", $"{file.Name} isn't a FoxyBrowser theme.", MaterialIconKind.AlertCircle, isError: true);
	}

	private async void Export(ThemeManager.Entry entry)
	{
		var fileName = string.Concat(entry.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
		var picker = new FileSavePicker { SuggestedFileName = fileName };
		picker.FileTypeChoices.Add("FoxyBrowser theme", new List<string> { ThemeManager.FileExtension });
		InitializeWithWindow.Initialize(picker, _mainWindow.GetWindowHandle());

		var file = await picker.PickSaveFileAsync();
		if (file is null) return;

		if (ThemeManager.Export(entry, file.Path))
			_mainWindow.ShowToast("Theme exported", file.Name, MaterialIconKind.Export);
		else
			_mainWindow.ShowToast("Couldn't export", $"The file {file.Name} could not be written.", MaterialIconKind.AlertCircle, isError: true);
	}
	#endregion

	#region Editor
	private void OpenEditor(string? id, string name, Theme theme)
	{
		_view = View.Editor;
		_editingId = id;
		_draftName = name;
		_draft = theme;
		Clear();

		var header = new Grid { ColumnSpacing = 12 };
		header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		header.Children.Add(Primary(id is null ? "New theme" : "Edit theme", 16, bold: true));
		var nameInput = new FTextInput { Width = 260, AcceptsReturn = false, PlaceHolderText = "Theme name", VerticalAlignment = VerticalAlignment.Center };
		nameInput.Text = name;
		nameInput.OnTextChanged += text => _draftName = text;
		_inputs.Add(nameInput);
		Grid.SetColumn(nameInput, 1);
		header.Children.Add(nameInput);
		_root.Children.Add(header);

		_editorPreview = Preview(theme, EditorPreviewWidth);
		_editorPreview.HorizontalAlignment = HorizontalAlignment.Left;
		_root.Children.Add(_editorPreview);

		var slots = new Grid { ColumnSpacing = 24, RowSpacing = 6, HorizontalAlignment = HorizontalAlignment.Left };
		slots.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		slots.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		var rows = (Slots.Length + 1) / 2;
		for (var i = 0; i < rows; i++) slots.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		for (var i = 0; i < Slots.Length; i++)
		{
			var row = SlotRow(i);
			Grid.SetColumn(row, i / rows);
			Grid.SetRow(row, i % rows);
			slots.Children.Add(row);
		}
		_root.Children.Add(slots);

		var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
		var save = Button("Save", Save, MaterialIconKind.Check);
		save.ForceHighlight = true;
		buttons.Children.Add(save);
		buttons.Children.Add(Button("Cancel", ShowGallery, MaterialIconKind.Close));
		_root.Children.Add(buttons);

		ApplyTheme();
	}

	private Grid SlotRow(int slot)
	{
		var (label, hint, get, _) = Slots[slot];
		var color = get(_draft);

		var swatch = new Border
		{
			Width = 30, Height = 30, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2),
			Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center,
		};
		swatch.PointerReleased += (_, _) => OpenPicker(slot, swatch);
		_swatches[slot] = swatch;
		_borders.Add(swatch);

		var hex = Secondary(SavedTheme.Hex(color), 11);
		hex.VerticalAlignment = VerticalAlignment.Center;
		hex.FontFamily = new FontFamily("Consolas");
		_hexLabels[slot] = hex;

		var row = new Grid { ColumnSpacing = 10, Width = 300 };
		row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Primary(label, 13, bold: true), Secondary(hint, 11) } };
		Grid.SetColumn(text, 1);
		Grid.SetColumn(hex, 2);
		row.Children.Add(swatch);
		row.Children.Add(text);
		row.Children.Add(hex);
		return row;
	}

	private void OpenPicker(int slot, FrameworkElement anchor)
	{
		_activeSlot = slot;
		if (_picker is null)
		{
			_picker = new FColorPicker { CurrentTheme = CurrentTheme };
			_picker.ColorChanged += color =>
			{
				_draft = Slots[_activeSlot].Set(_draft, color);
				if (_editorPreview is not null) _editorPreview.Theme = _draft;
				_swatches[_activeSlot].Background = new SolidColorBrush(color);
				_hexLabels[_activeSlot].Text = SavedTheme.Hex(color);
			};
			_pickerCard = WebUiStyle.Card(_picker);
			WebUiStyle.ApplyCardTheme(_pickerCard, CurrentTheme, solid: true);

			//C the presenter's own chrome follows the Windows theme, so it is blanked and the card draws the frame
			var presenterStyle = new Style(typeof(FlyoutPresenter));
			presenterStyle.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Colors.Transparent)));
			presenterStyle.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
			presenterStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
			presenterStyle.Setters.Add(new Setter(CornerRadiusProperty, new CornerRadius(0)));
			presenterStyle.Setters.Add(new Setter(MarginProperty, new Thickness(0)));
			presenterStyle.Setters.Add(new Setter(MinWidthProperty, 0d));
			presenterStyle.Setters.Add(new Setter(MinHeightProperty, 0d));
			presenterStyle.Setters.Add(new Setter(MaxWidthProperty, double.PositiveInfinity));
			_pickerFlyout = new Flyout { Content = _pickerCard, Placement = FlyoutPlacementMode.Bottom, FlyoutPresenterStyle = presenterStyle };
		}

		_picker.Color = Slots[slot].Get(_draft);
		_pickerFlyout!.ShowAt(anchor);
	}

	private void Save()
	{
		_pickerFlyout?.Hide();
		if (ThemeManager.Save(_editingId, _draftName, _draft) is not { } entry)
		{
			_mainWindow.ShowToast("Couldn't save the theme", "The theme file could not be written. See the log for details.", MaterialIconKind.AlertCircle, isError: true);
			return;
		}
		_mainWindow.ShowToast("Theme saved", entry.Name, MaterialIconKind.Palette);
		ShowGallery();
	}
	#endregion

	#region Building blocks
	private ThemePreview Preview(Theme theme, double width)
	{
		var preview = new ThemePreview { Theme = theme, BackgroundImage = _homeImage, Width = width, Height = width * DesignRatio };
		_previews.Add(preview);
		return preview;
	}

	private FTextButton Button(string text, Action onClick, MaterialIconKind? icon = null, bool danger = false)
	{
		var button = WebUiStyle.TextButton(text, onClick, icon);
		_buttons.Add((button, danger));
		return button;
	}

	private FIconButton IconButton(MaterialIconKind kind, Action onClick, string toolTip)
	{
		var button = WebUiStyle.IconButton(kind, onClick, size: 26, toolTip: toolTip);
		_iconButtons.Add(button);
		return button;
	}

	private TextBlock Primary(string text, double size = 13, bool bold = false)
	{
		var block = WebUiStyle.Text(text, size, bold);
		_primaryText.Add(block);
		return block;
	}

	private TextBlock Secondary(string text, double size = 12)
	{
		var block = WebUiStyle.Text(text, size);
		_secondaryText.Add(block);
		return block;
	}
	#endregion

	protected override void ApplyTheme()
	{
		if (_root is null) return;

		foreach (var (button, danger) in _buttons)
		{
			button.CurrentTheme = danger ? WebUiStyle.DangerTheme(CurrentTheme) : CurrentTheme;
			WebUiStyle.ThemeIcon(button.Icon, CurrentTheme.PrimaryForegroundColor);
		}
		foreach (var button in _iconButtons) button.CurrentTheme = CurrentTheme;
		foreach (var text in _primaryText) text.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		foreach (var text in _secondaryText) text.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		foreach (var icon in _icons) icon.Foreground = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);
		foreach (var input in _inputs) input.CurrentTheme = CurrentTheme;
		foreach (var border in _borders) border.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryAccentColorSlightTransparent);
		foreach (var (card, selected) in _cards)
		{
			card.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
			card.BorderBrush = new SolidColorBrush(selected ? CurrentTheme.PrimaryHighlightColor : CurrentTheme.SecondaryBackgroundColorSlightTransparent);
		}
		if (_picker is not null) _picker.CurrentTheme = CurrentTheme;
		if (_pickerCard is not null) WebUiStyle.ApplyCardTheme(_pickerCard, CurrentTheme, solid: true);
	}
}

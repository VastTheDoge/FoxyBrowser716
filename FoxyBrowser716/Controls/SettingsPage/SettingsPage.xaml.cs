using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.Controls.WebUi;
using FoxyBrowser716.DataObjects.Settings;
using Material.Icons.WinUI3;
using Microsoft.UI.Dispatching;

namespace FoxyBrowser716.Controls.SettingsPage;

// Claude Doc
/// <summary>
/// The settings page: a nav column with one button per category (the one in view is highlighted) and a
/// scrolling column with every category's settings in a card. Typing in the search box swaps the category
/// buttons for the best-matching settings; clicking one scrolls to it and flashes it.
/// </summary>
public sealed partial class SettingsPage : UserControl
{
    private const int MaxResults = 8;
    private const double ActiveHeaderOffset = 48;

    public SettingsPage()
    {
        InitializeComponent();
    }

    internal Theme CurrentTheme { get; set { field = value; ApplyTheme(); } } = DefaultThemes.DarkMode;

    private MainWindow.MainWindow _mainWindow = null!;
    private readonly List<ThemedUserControl> _settingsControls = [];
    private readonly List<(SettingsCategory category, FTextButton button, ThemedUserControl header)> _categories = [];
    private readonly List<FTextButton> _resultButtons = [];
    private readonly List<SettingsSearch.Entry<Border>> _searchable = [];
    private readonly List<Border> _cards = [];
    private readonly List<Border> _separators = [];
    private readonly TextBlock _noResults = new() { Text = "No settings match.", FontSize = 13, Margin = new Thickness(6, 4, 6, 4), TextWrapping = TextWrapping.Wrap };
    private SettingsCategory? _activeCategory;
    private DateTime _holdActiveUntil = DateTime.MinValue;

    public Task Initialize(MainWindow.MainWindow mainWindow)
    {
        _mainWindow = mainWindow;

        var controls = _mainWindow.Instance.Settings.GetSettingControls(_mainWindow);

        InputSearch.PlaceHolderText = "Search settings";
        InputSearch.AcceptsReturn = false;
        InputSearch.OnTextChanged += ApplySearch;
        SettingsScroller.ViewChanged += (_, _) => UpdateActiveCategory();

        foreach (var (category, settings) in controls)
        {
            var button = NavButton(category.GetDisplayName(), category);
            button.OnClick += (_, _) => ScrollToCategory(category);

            var header = new HeaderSetting(category.GetDisplayName()).GetEditor(_mainWindow);
            _settingsControls.Add(header);
            SettingsViewer.Children.Add(header);

            var list = new StackPanel();
            var previousWasHeading = true;
            foreach (var setting in settings)
            {
                if (setting is DividerSetting) continue;

                var editor = setting.GetEditor(_mainWindow);
                _settingsControls.Add(editor);
                var isHeading = setting is SubheadingSetting;
                if (!isHeading && !previousWasHeading) list.Children.Add(Separator());
                previousWasHeading = isHeading;

                var row = new Border { Child = editor, Padding = new Thickness(10, isHeading ? 8 : 8, 10, 8), CornerRadius = new CornerRadius(6) };
                list.Children.Add(row);
                if (!isHeading && setting is not HeaderSetting)
                    _searchable.Add(new SettingsSearch.Entry<Border>(row, setting.Name, DescriptionOf(setting), category.GetDisplayName()));
            }

            var card = new Border
            {
                Child = list,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(2),
                Margin = new Thickness(8, 0, 0, 8),
            };
            _cards.Add(card);
            SettingsViewer.Children.Add(card);

            _categories.Add((category, button, header));
        }

        ShowCategories();
        ApplyTheme();
        return Task.CompletedTask;
    }

    private static string DescriptionOf(ISetting setting) => setting switch
    {
        CustomControlSetting custom => custom.Description,
        ButtonSetting button => button.Description,
        //C Setting<T> is generic, so read its Description without knowing T
        _ => setting.GetType().GetProperty("Description")?.GetValue(setting) as string ?? string.Empty,
    };

    private static MaterialIconKind IconFor(SettingsCategory category) => category switch
    {
        SettingsCategory.General => MaterialIconKind.Cog,
        SettingsCategory.Appearance => MaterialIconKind.Palette,
        SettingsCategory.Privacy => MaterialIconKind.ShieldLock,
        SettingsCategory.Permissions => MaterialIconKind.ShieldCheck,
        SettingsCategory.Downloads => MaterialIconKind.Download,
        SettingsCategory.History => MaterialIconKind.History,
        SettingsCategory.Extensions => MaterialIconKind.PuzzleOutline,
        SettingsCategory.Assistant => MaterialIconKind.Robot,
        SettingsCategory.WebView2 => MaterialIconKind.Engine,
        _ => MaterialIconKind.DotsHorizontal,
    };

    private static FTextButton NavButton(string text, SettingsCategory category) => new()
    {
        ButtonText = text,
        Icon = new MaterialIcon { Kind = IconFor(category), Width = 18, Height = 18 },
        CornerRadius = new CornerRadius(5),
        Height = 32,
        Padding = new Thickness(6, 0, 6, 0),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        ContentHorizontalAlignment = HorizontalAlignment.Left,
    };

    private Border Separator()
    {
        var separator = new Border { Height = 1, Margin = new Thickness(10, 0, 10, 0) };
        _separators.Add(separator);
        return separator;
    }

    private void ShowCategories()
    {
        NavItems.Children.Clear();
        _resultButtons.Clear();
        foreach (var (_, button, _) in _categories) NavItems.Children.Add(button);
        UpdateActiveCategory();
    }

    // Claude Doc
    /// <summary>Scrolls the settings list to a category's header (clearing any search first).</summary>
    public void ScrollToCategory(SettingsCategory category)
    {
        if (_categories.FirstOrDefault(c => c.category == category).header is not { } header) return;

        if (!string.IsNullOrWhiteSpace(InputSearch.CurrentText))
        {
            InputSearch.SetText(string.Empty);
            ShowCategories();
        }

        //C the clicked category stays highlighted while the scroll animates, even if its header can't reach the top
        _holdActiveUntil = DateTime.UtcNow.AddMilliseconds(800);
        SetActiveCategory(category);

        //C the page may have only just been made visible; let it lay out first
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            header.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = true }));
    }

    private void UpdateActiveCategory()
    {
        if (_categories.Count == 0 || DateTime.UtcNow < _holdActiveUntil) return;

        SettingsCategory? active = null;
        foreach (var (category, _, header) in _categories)
        {
            var top = header.TransformToVisual(SettingsScroller).TransformPoint(new Point(0, 0)).Y;
            if (top > ActiveHeaderOffset) break;
            active = category;
        }
        //C the last categories may never reach the top, so the end of the page counts as the last one
        if (SettingsScroller.ScrollableHeight > 0 && SettingsScroller.VerticalOffset >= SettingsScroller.ScrollableHeight - 1)
            active = _categories[^1].category;
        SetActiveCategory(active ?? _categories[0].category);
    }

    private void SetActiveCategory(SettingsCategory category)
    {
        if (_activeCategory == category) return;
        _activeCategory = category;
        foreach (var (c, button, _) in _categories) button.ForceHighlight = c == category;
    }

    private void ApplySearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ShowCategories();
            return;
        }

        NavItems.Children.Clear();
        _resultButtons.Clear();

        var results = SettingsSearch.Rank(query, _searchable, MaxResults);
        if (results.Count == 0)
        {
            NavItems.Children.Add(_noResults);
            return;
        }

        foreach (var row in results)
        {
            var entry = _searchable.First(e => ReferenceEquals(e.Item, row));
            var category = _categories.First(c => c.category.GetDisplayName() == entry.Category).category;
            var button = NavButton(entry.Name, category);
            button.CurrentTheme = CurrentTheme;
            WebUiStyle.ThemeIcon(button.Icon, CurrentTheme.PrimaryForegroundColor);
            button.OnClick += (_, _) => JumpTo(row);
            _resultButtons.Add(button);
            NavItems.Children.Add(button);
        }
    }

    private void JumpTo(Border row)
    {
        row.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.1, AnimationDesired = true });

        var highlight = CurrentTheme.PrimaryHighlightColor;
        var brush = new SolidColorBrush(Color.FromArgb(90, highlight.R, highlight.G, highlight.B));
        row.Background = brush;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(900);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => ChangeColorAnimation(brush, Color.FromArgb(0, highlight.R, highlight.G, highlight.B), 0.8);
        timer.Start();
    }

    private void ApplyTheme()
    {
        _settingsControls.ForEach(c => c.CurrentTheme = CurrentTheme);
        foreach (var button in _categories.Select(c => c.button).Concat(_resultButtons))
        {
            button.CurrentTheme = CurrentTheme;
            WebUiStyle.ThemeIcon(button.Icon, CurrentTheme.PrimaryForegroundColor);
        }

        InputSearch.CurrentTheme = CurrentTheme;
        _noResults.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);

        BorderNav.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
        BorderNav.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
        foreach (var card in _cards)
        {
            card.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
            card.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
        }
        foreach (var separator in _separators)
            separator.Background = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);

        RootGrid.Background = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            Center = new Point(0, 1),
            GradientOrigin = new Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = CurrentTheme.PrimaryHighlightColor, Offset = 0 },
                new GradientStop { Color = CurrentTheme.PrimaryBackgroundColor, Offset = 0.6 },
            },
        };
    }
}

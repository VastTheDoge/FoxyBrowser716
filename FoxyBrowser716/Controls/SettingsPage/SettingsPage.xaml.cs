using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.DataObjects.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FoxyBrowser716.Controls.SettingsPage;

public sealed partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }
    
    internal Theme CurrentTheme { get; set { field = value; ApplyTheme(); } } = DefaultThemes.DarkMode;

    private void ApplyTheme()
    {
        _settingsControls.ForEach(c => c.CurrentTheme = CurrentTheme);
        _categoryButtons.ForEach(c => c.CurrentTheme = CurrentTheme);
        
        InputSearch.CurrentTheme = CurrentTheme;
        ResultBlock.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);

        BorderSearch.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
        BorderSearch.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
        
        BorderCategories.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
        BorderCategories.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
        
        //TODO: temp
        var newGradient = new RadialGradientBrush()
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            Center = new Point(0,1),
            GradientOrigin = new Point(0,1),
        };
        
        newGradient.GradientStops.Add(new GradientStop { Color = CurrentTheme.PrimaryHighlightColor, Offset = 0 });
        newGradient.GradientStops.Add(new GradientStop { Color = CurrentTheme.PrimaryBackgroundColor, Offset = 0.6 });


        RootGrid.Background = newGradient;
        
        //TODO: why is this not updating???
        // BackgroundPrimary.Color = CurrentTheme.PrimaryBackgroundColor;
        // BackgroundSecondary.Color = CurrentTheme.PrimaryHighlightColor;
        // Debug.WriteLine(string.Join(" -> ", BackgroundGradient.GradientStops.Select(gs => gs.Color)));
        // RootGrid.Background = new SolidColorBrush(Colors.Black);
        
        // BackgroundGradient.InterpolationSpace = CompositionColorSpace.Auto;
    }
    
    private MainWindow.MainWindow _mainWindow;
    private List<FTextButton> _categoryButtons = [];
    private List<ThemedUserControl> _settingsControls = [];

    private readonly Dictionary<SettingsCategory, ThemedUserControl> _categoryHeaders = [];
    private readonly Dictionary<SettingsCategory, (FTextButton button, ThemedUserControl header, ThemedUserControl divider)> _categoryChrome = [];
    private readonly List<(SettingsCategory category, string text, ThemedUserControl control)> _searchable = [];

    public async Task Initialize(MainWindow.MainWindow mainWindow)
    {
        _mainWindow = mainWindow;

        var controls = _mainWindow.Instance.Settings.GetSettingControls(_mainWindow);

        InputSearch.PlaceHolderText = "Search settings";
        InputSearch.AcceptsReturn = false;
        InputSearch.OnTextChanged += ApplySearch;
        
        foreach (var pair in controls)
        {
            var category = pair.Key;
            var categoryButton = new FTextButton
            {
                ButtonText = category.GetDisplayName(),
                CurrentTheme = CurrentTheme,
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            categoryButton.OnClick += (_,_) => ScrollToCategory(category);
            
            _categoryButtons.Add(categoryButton);
            CategoryViewer.Children.Add(categoryButton);
            
            var headset = new HeaderSetting(category.GetDisplayName()).GetEditor(_mainWindow);
            _settingsControls.Add(headset);
            headset.CurrentTheme = CurrentTheme;
            SettingsViewer.Children.Add(headset);
            _categoryHeaders[category] = headset;
            
            foreach (var control in pair.Value)
            {
                var editor = control.GetEditor(_mainWindow);
                _settingsControls.Add(editor);
                editor.CurrentTheme = CurrentTheme;
                editor.Margin = new Thickness(8, 4, 8, 4);
                SettingsViewer.Children.Add(editor);
                _searchable.Add((category, $"{control.Name} {DescriptionOf(control)} {category.GetDisplayName()}", editor));
            }

            var divSet = new DividerSetting().GetEditor(_mainWindow);
            _settingsControls.Add(divSet);
            divSet.CurrentTheme = CurrentTheme;
            SettingsViewer.Children.Add(divSet);

            _categoryChrome[category] = (categoryButton, headset, divSet);
        }
    }

    private static string DescriptionOf(ISetting setting) => setting switch
    {
        CustomControlSetting custom => custom.Description,
        ButtonSetting button => button.Description,
        // Setting<T> is generic, so read its Description without knowing T
        _ => setting.GetType().GetProperty("Description")?.GetValue(setting) as string ?? string.Empty,
    };

    /// <summary>Scrolls the settings list to a category's header (clearing any search that hides it).</summary>
    public void ScrollToCategory(SettingsCategory category)
    {
        if (!_categoryHeaders.TryGetValue(category, out var header)) return;

        if (!string.IsNullOrWhiteSpace(InputSearch.CurrentText))
        {
            InputSearch.SetText(string.Empty);
            ApplySearch(string.Empty);
        }

        // the page may have only just been made visible; let it lay out first
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            header.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = true }));
    }

    private void ApplySearch(string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var visibleCategories = new HashSet<SettingsCategory>();
        var matches = 0;

        foreach (var (category, text, control) in _searchable)
        {
            var match = words.All(w => text.Contains(w, StringComparison.CurrentCultureIgnoreCase));
            control.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            if (!match) continue;
            matches++;
            visibleCategories.Add(category);
        }

        foreach (var (category, (button, header, divider)) in _categoryChrome)
        {
            var visible = words.Length == 0 || visibleCategories.Contains(category);
            var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            button.Visibility = visibility;
            header.Visibility = visibility;
            divider.Visibility = visibility;
        }

        ResultBlock.Text = words.Length == 0
            ? string.Empty
            : matches == 0 ? "No settings match." : $"{matches} setting{(matches == 1 ? "" : "s")} found.";
    }
}

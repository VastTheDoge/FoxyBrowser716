

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

using Windows.UI.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Shapes;

namespace FoxyBrowser716.Controls.HomePage;

public sealed partial class WidgetEditOverlay : UserControl
{
    public Theme CurrentTheme { get; set { field = value; ApplyTheme(); } } = DefaultThemes.DarkMode;
    
    internal WidgetBase AttachedWidget;
    
    public Action<WidgetEditOverlay>? SettingsClicked;
    public Action<WidgetEditOverlay>? DeleteClicked;

    public Action<WidgetEditOverlay>? PointerRefreshRequested;

    public Action<WidgetEditOverlay>? RootEntered;
    public Action<WidgetEditOverlay>? RootExited;
    public Action<WidgetEditOverlay, PointerRoutedEventArgs>? MouseDown;
    public Action<WidgetEditOverlay>? MouseUp;

    private bool _showSettings;
    
    public string? CurrentBorder { get; private set; } = "";
    
    private void ApplyTheme()
    {
        Root.BorderBrush = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);
        Root.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
        ButtonTextSettings.CurrentTheme = CurrentTheme;
        ButtonTextRemove.CurrentTheme = CurrentTheme with { PrimaryForegroundColor = CurrentTheme.NoColor };
        ButtonIconSettings.CurrentTheme = CurrentTheme;
        ButtonIconRemove.CurrentTheme = CurrentTheme with { PrimaryForegroundColor = CurrentTheme.NoColor };
    }

    public WidgetEditOverlay()
    {
        InitializeComponent();
    }
    
    public WidgetEditOverlay(WidgetBase widget, bool showSettings)
    {
        InitializeComponent();
        AttachedWidget = widget;
        _showSettings = showSettings;
        ApplyTheme();
    }

    private void ButtonSettings_OnOnClick(object sender, RoutedEventArgs e)
    {
        SettingsClicked?.Invoke(this);
    }

    private void ButtonRemove_OnOnClick(object sender, RoutedEventArgs e)
    {
        DeleteClicked?.Invoke(this);
    }

    private void Border_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Rectangle border || border.Name == CurrentBorder) return;
        CurrentBorder = border.Name;
        PointerRefreshRequested?.Invoke(this);
    }

    private void Border_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Rectangle border || border.Name != CurrentBorder) return;
        CurrentBorder = OverButtons() ? null : "";
        PointerRefreshRequested?.Invoke(this);
    }

    private bool _pointerPressed;
    private void Root_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (OverButtons())
            return;
        
        MouseDown?.Invoke(this, e);
    }

    private bool OverButtons()
    {
        return ButtonIconSettings.PointerOver || ButtonIconRemove.PointerOver ||
               ButtonTextSettings.PointerOver || ButtonTextRemove.PointerOver;
    }

    private void Root_OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        MouseUp?.Invoke(this);
    }

    private void WidgetEditOverlay_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // actual width is zero when collapsed, it must be hard-codded
        // button + button + extra space
        const int buttonMinWidth = 87 + 87 + 10;
        ButtonTextRemove.Visibility = buttonMinWidth > e.NewSize.Width ? Visibility.Collapsed : Visibility.Visible;
        ButtonTextSettings.Visibility = buttonMinWidth > e.NewSize.Width || !_showSettings ? Visibility.Collapsed : Visibility.Visible;
        ButtonIconRemove.Visibility = buttonMinWidth > e.NewSize.Width ? Visibility.Visible : Visibility.Collapsed;
        ButtonIconSettings.Visibility = buttonMinWidth > e.NewSize.Width && _showSettings ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Root_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        RootEntered?.Invoke(this);
    }

    private void Root_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        RootExited?.Invoke(this);
    }

    private void ButtonIconSettings_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        CurrentBorder = "";
        PointerRefreshRequested?.Invoke(this);
    }

    private void ButtonIconSettings_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        CurrentBorder = null;
        PointerRefreshRequested?.Invoke(this);
    }
}

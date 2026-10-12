
using Windows.UI.Text;
using FoxyBrowser716.DataObjects.Basic;
using Microsoft.UI.Input;
using Microsoft.UI.Text;

namespace FoxyBrowser716.Controls.Generic;

public sealed partial class FTextButton : ContentControl
{
    public event RoutedEventHandler? OnClick;
    
    public static readonly DependencyProperty ButtonTextProperty = DependencyProperty.Register(
        nameof(ButtonText), typeof(string), typeof(FTextButton),
        new PropertyMetadata(string.Empty));
    
    public string ButtonText
    {
        get => (string)GetValue(ButtonTextProperty);
        set => SetValue(ButtonTextProperty, value);
    }
    
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(UIElement), typeof(FTextButton),
        new PropertyMetadata(null));
    
    public static readonly DependencyProperty ContentHorizontalAlignmentProperty = DependencyProperty.Register(
        nameof(ContentHorizontalAlignment), typeof(HorizontalAlignment), typeof(FTextButton),
        new PropertyMetadata(HorizontalAlignment.Center));
    
    public HorizontalAlignment ContentHorizontalAlignment
    {
        get => (HorizontalAlignment)GetValue(ContentHorizontalAlignmentProperty);
        set => SetValue(ContentHorizontalAlignmentProperty, value);
    }

    
    public UIElement Icon
    {
        get => (UIElement)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
    
    public static readonly DependencyProperty ForceHighlightProperty = DependencyProperty.Register(
        nameof(ForceHighlight), typeof(bool), typeof(FTextButton),
        new PropertyMetadata(false, ForceHighlightChanged));
    
    public bool ForceHighlight
    {
        get => (bool)GetValue(ForceHighlightProperty);
        set { SetValue(ForceHighlightProperty, value);
            ForceHighlightChanged(this, null);
        }
    }

    private static void ForceHighlightChanged(DependencyObject d, DependencyPropertyChangedEventArgs? e)
    {
        var control = (FTextButton)d;
        control.Background = new SolidColorBrush(control.ForceHighlight 
            ? control.CurrentTheme.PrimaryHighlightColor 
            : control.PointerOver
                ? control.CurrentTheme.PrimaryAccentColorSlightTransparent
                : control.CurrentTheme.PrimaryAccentColorVeryTransparent);
    }

    public static readonly DependencyProperty CurrentThemeProperty = DependencyProperty.Register(
        nameof(CurrentTheme),
        typeof(Theme),
        typeof(FTextButton),
        new PropertyMetadata(DefaultThemes.LightMode, (d, e) => ((FTextButton)d).OnCurrentThemeChanged((Theme)e.NewValue)));

    public Theme CurrentTheme
    {
        get => (Theme)GetValue(CurrentThemeProperty);
        set => SetValue(CurrentThemeProperty, value);
    }
    
    private void OnCurrentThemeChanged(Theme _)
    {
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (ForceHighlight)
        {
            Background = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);
        }
        else
        {
            Background = new SolidColorBrush(PointerOver ? CurrentTheme.PrimaryAccentColorSlightTransparent : CurrentTheme.PrimaryAccentColorVeryTransparent);
        }

        Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
    }
    
    internal bool PointerOver { get; set; }
    
    public FTextButton()
    {
        DefaultStyleKey = typeof(FTextButton);
        
        PointerEntered += (_,_) =>
        {
            PointerOver = true;
            
            if (ForceHighlight) return;

            ChangeColorAnimation(Background, CurrentTheme.PrimaryAccentColorSlightTransparent);
        };

        PointerExited += (_, _) =>
        {
            PointerOver = false;
            
            if (ForceHighlight) return;
            
            ChangeColorAnimation(Background, CurrentTheme.PrimaryAccentColorVeryTransparent);
        };
        
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            if (ForceHighlight) return;
            
            ChangeColorAnimation(Background, CurrentTheme.PrimaryHighlightColor, 0.05);
        };

        PointerReleased += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased) return;

            ChangeColorAnimation(Background, CurrentTheme.PrimaryAccentColorSlightTransparent, 0.3);
            
            OnClick?.Invoke(this, new RoutedEventArgs());
        };

        ApplyTheme();
    }
}
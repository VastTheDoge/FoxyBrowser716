using FoxyBrowser716.DataObjects.Basic;
using Microsoft.UI.Input;

namespace FoxyBrowser716.Controls.Generic;

public sealed partial class FIconButton : ContentControl
{
    public event RoutedEventHandler? OnClick;
    
    public static readonly DependencyProperty ForceHighlightProperty = DependencyProperty.Register(
        nameof(ForceHighlight), typeof(bool), typeof(FIconButton),
        new PropertyMetadata(null, ForceHighlightChanged));
    
    public bool ForceHighlight
    {
        get => (bool)GetValue(ForceHighlightProperty);
        set { SetValue(ForceHighlightProperty, value);
            ForceHighlightChanged(this, null);
        }
    }

    private static void ForceHighlightChanged(DependencyObject d, DependencyPropertyChangedEventArgs? e)
    {
        var control = (FIconButton)d;
        control.Background = new SolidColorBrush(control.ForceHighlight 
            ? control.CurrentTheme.PrimaryHighlightColorSlightTransparent
            : Colors.Transparent);
    }
    
    public static readonly DependencyProperty RoundedProperty = DependencyProperty.Register(
        nameof(Rounded), typeof(bool), typeof(FIconButton),
        new PropertyMetadata(true, RoundedChanged));
    
    public bool Rounded
    {
        get => (bool)GetValue(RoundedProperty);
        set { SetValue(RoundedProperty, value);
            RoundedChanged(this, null);
        }
    }

    private static void RoundedChanged(DependencyObject d, DependencyPropertyChangedEventArgs? e)
    {
        ((FIconButton)d).CornerRadius = new CornerRadius(((FIconButton)d).Rounded ? (Math.Min(((FIconButton)d).ActualWidth, ((FIconButton)d).ActualHeight) / 2 ) : 0);
    }
    
    public static readonly DependencyProperty CurrentThemeProperty = DependencyProperty.Register(
        nameof(CurrentTheme),
        typeof(Theme),
        typeof(FIconButton),
        new PropertyMetadata(DefaultThemes.LightMode, (d, e) => ((FIconButton)d).OnCurrentThemeChanged((Theme)e.NewValue)));

    public Theme CurrentTheme
    {
        get => (Theme)GetValue(CurrentThemeProperty);
        set => SetValue(CurrentThemeProperty, value);
    }
    
    private void OnCurrentThemeChanged(Theme _) => ApplyTheme();

    public static readonly DependencyProperty ToolTipTextProperty = DependencyProperty.Register(
        nameof(ToolTipText), typeof(string), typeof(FIconButton),
        new PropertyMetadata(null, (d, _) => ((FIconButton)d).ApplyToolTip()));

    //C icon-only buttons say what they do on hover, in the button's theme
    public string? ToolTipText
    {
        get => (string?)GetValue(ToolTipTextProperty);
        set => SetValue(ToolTipTextProperty, value);
    }

    private void ApplyToolTip()
    {
        if (ToolTipText is not null || ToolTipService.GetToolTip(this) is ToolTip)
            FToolTip.Set(this, ToolTipText, CurrentTheme);
    }
    
    private void ApplyTheme()
    {
        if (ForceHighlight)
        {
            Background = new SolidColorBrush(CurrentTheme.PrimaryHighlightColorSlightTransparent);
        }
        else
        {
            Background = new SolidColorBrush(Colors.Transparent);;
        }
        Foreground = new SolidColorBrush(PointerOver ? CurrentTheme.PrimaryAccentColorSlightTransparent : CurrentTheme.PrimaryForegroundColor);
        ApplyToolTip();
    }
    
    internal bool PointerOver { get; set; }
    
    public FIconButton()
    {
        DefaultStyleKey = typeof(FIconButton);

        SizeChanged += (_, _) =>
        {
            var minSize = Math.Min(ActualWidth, ActualHeight);
            CornerRadius = new CornerRadius(Rounded ? minSize / 2 : 0);
        };
        
        PointerEntered += (_,_) =>
        {
            PointerOver = true;
            
            ChangeColorAnimation(Foreground, CurrentTheme.SecondaryForegroundColor);
        };

        PointerExited += (_, _) =>
        {
            PointerOver = false;
            
            ChangeColorAnimation(Foreground, CurrentTheme.PrimaryForegroundColor);
        };
        
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            
            ChangeColorAnimation(Foreground, CurrentTheme.PrimaryHighlightColor, 0.05);
            e.Handled = true;
        };

        PointerReleased += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased) return;

            OnClick?.Invoke(this, new RoutedEventArgs());
            
            ChangeColorAnimation(Foreground, PointerOver ? CurrentTheme.SecondaryForegroundColor : CurrentTheme.PrimaryForegroundColor, 0.3);
            e.Handled = true;
        };

        ApplyTheme();
    }
}

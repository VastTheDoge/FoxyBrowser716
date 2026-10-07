using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FoxyBrowser716.Controls.Generic;

[ObservableObject]
public sealed partial class FTextInput : UserControl
{
    public static readonly DependencyProperty PlaceHolderTextProperty = DependencyProperty.Register(
        nameof(PlaceHolderText), typeof(string), typeof(FTextInput),
        new PropertyMetadata(string.Empty, PlaceHolderTextChanged));
    
    public string PlaceHolderText
    {
        get => (string)GetValue(PlaceHolderTextProperty);
        set { SetValue(PlaceHolderTextProperty, value);
            PlaceHolderTextChanged(this, null);
        }
    }
    
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(FTextInput),
        new PropertyMetadata(string.Empty, OnTextPropertyChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (FTextInput)d;
        var newText = e.NewValue as string ?? string.Empty;
        if (control.SearchBox.Text != newText)
            control.SearchBox.Text = newText;
    }
    
    public static readonly DependencyProperty InputMiddlewareProperty = DependencyProperty.Register(
        nameof(InputMiddleware), typeof(Func<string, string, string>), typeof(FTextInput),
        new PropertyMetadata(null));

    public Func<string, string, string>? InputMiddleware
    {
        get => (Func<string, string, string>?)GetValue(InputMiddlewareProperty);
        set => SetValue(InputMiddlewareProperty, value);
    }

    public static readonly DependencyProperty AcceptsReturnProperty = DependencyProperty.Register(
        nameof(AcceptsReturn), typeof(bool), typeof(FTextInput),
        new PropertyMetadata(true, OnAcceptsReturnChanged));

    public bool AcceptsReturn
    {
        get => (bool)GetValue(AcceptsReturnProperty);
        set
        {
            SetValue(AcceptsReturnProperty, value);
            TextWrap = value ? TextWrapping.Wrap : TextWrapping.NoWrap;
            SearchBox.AcceptsReturn = value;
        }
    }

    private static void OnAcceptsReturnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (FTextInput)d;
        control.SearchBox.AcceptsReturn = (bool)e.NewValue;
    }

    [ObservableProperty] private partial TextWrapping TextWrap { get; set; } = TextWrapping.NoWrap;

    private static void PlaceHolderTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs? e)
    {
        var control = (FTextInput)d;
        control.SearchBox.PlaceholderText = control.PlaceHolderText;
    }

    // public void SetText(string text)
    // {
    //     SearchBox.Text = text;
    // }
    // public string GetText()
    // {
    //     return SearchBox.Text;
    // }
    
    public event Action<string>? OnTextChanged;
    public event Action? EnterPressed;

    /// <summary>What is in the box right now (<see cref="Text"/> is not updated while typing).</summary>
    public string CurrentText => SearchBox.Text;

    public void FocusInput()
    {
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
    }
    
    public FTextInput()
    {
        InitializeComponent();
    }
    
    public FTextInput(string CurrentText)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(CurrentText))
            SearchBox.Text = CurrentText;
    }
    
    public static readonly DependencyProperty CurrentThemeProperty = DependencyProperty.Register(
        nameof(CurrentTheme),
        typeof(Theme),
        typeof(FTextInput),
        new PropertyMetadata(DefaultThemes.LightMode, (d, e) => ((FTextInput)d).OnCurrentThemeChanged((Theme)e.NewValue)));

    public Theme CurrentTheme
    {
        get => (Theme)GetValue(CurrentThemeProperty);
        set => SetValue(CurrentThemeProperty, value);
    }
    
    private void OnCurrentThemeChanged(Theme _) => ApplyTheme();

    private void ApplyTheme()
    {
        SearchBox.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryAccentColorSlightTransparent);
        SearchBox.Background = new SolidColorBrush(CurrentTheme.PrimaryAccentColorSlightTransparent);
        SearchBox.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
        SearchBox.PlaceholderForeground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
        SearchBox.SelectionHighlightColor = new SolidColorBrush(CurrentTheme.SecondaryHighlightColor);
        SearchBox.SelectionHighlightColorWhenNotFocused = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);
    }

    private void SearchBox_OnGotFocus(object sender, RoutedEventArgs e)
    {
        ChangeColorAnimation(SearchBox.BorderBrush, CurrentTheme.PrimaryHighlightColor);
        ChangeColorAnimation(SearchBox.Background, CurrentTheme.PrimaryBackgroundColorSlightTransparent);
    }

    private void SearchBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        ChangeColorAnimation(SearchBox.BorderBrush, CurrentTheme.SecondaryAccentColorSlightTransparent);
        ChangeColorAnimation(SearchBox.Background, CurrentTheme.PrimaryAccentColorSlightTransparent);
    }

    private void SearchBox_OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        OnTextChanged?.Invoke(SearchBox.Text);
        
        if (e.Key == Windows.System.VirtualKey.Enter)
            EnterPressed?.Invoke();
    }
    
    private void SearchBox_TextChanging(object sender, TextBoxBeforeTextChangingEventArgs e)
    {
        if (InputMiddleware is null) return; // let the text change like normal
        
        // call the middleware to see what to set the text to
        var newText = InputMiddleware(SearchBox.Text, e.NewText);
        
        if (newText == e.NewText) return; // let the text change like normal. it is valid.
        
        e.Cancel = true;
        SetValue(TextProperty, SearchBox.Text);
        OnTextChanged?.Invoke(SearchBox.Text);    
    }
}

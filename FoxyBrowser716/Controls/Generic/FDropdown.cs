using Material.Icons.WinUI3;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.System;

namespace FoxyBrowser716.Controls.Generic;

// Claude Doc
/// <summary>
/// A themed drop-down list: a box showing the chosen option that opens the full list underneath.
/// The box keeps its width whatever the options say (long names are trimmed), and a long list scrolls.
/// </summary>
public sealed partial class FDropdown : UserControl
{
    public event Action<int>? SelectionChanged;

    private readonly Border _box;
    private readonly TextBlock _label;
    private readonly MaterialIcon _chevron;
    private readonly Flyout _flyout;
    private readonly Border _listBorder;
    private readonly ScrollViewer _listScroller;
    private readonly StackPanel _list;
    private readonly List<FTextButton> _optionButtons = [];
    private bool _pointerOver;

    public Theme CurrentTheme { get; set { field = value; ApplyTheme(); } } = DefaultThemes.DarkMode;

    public IReadOnlyList<string> Options
    {
        get;
        set
        {
            field = value.ToArray();
            RebuildList();
            SelectedIndex = Math.Min(SelectedIndex, field.Count - 1);
            UpdateLabel();
        }
    } = [];

    // Claude Doc
    /// <summary>The chosen option, or -1 for none. Setting it does not raise <see cref="SelectionChanged"/>.</summary>
    public int SelectedIndex
    {
        get;
        set
        {
            field = value >= 0 && value < Options.Count ? value : -1;
            UpdateLabel();
            UpdateHighlight();
        }
    } = -1;

    public string PlaceholderText { get; set { field = value; UpdateLabel(); } } = string.Empty;

    public FDropdown()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;

        _label = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(10, 2, 4, 2),
        };

        _chevron = new MaterialIcon
        {
            Kind = MaterialIconKind.ChevronDown,
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var boxContent = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        Grid.SetColumn(_chevron, 1);
        boxContent.Children.Add(_label);
        boxContent.Children.Add(_chevron);

        _box = new Border
        {
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(5),
            MinHeight = 28,
            Child = boxContent,
            Background = new SolidColorBrush(CurrentTheme.PrimaryAccentColorSlightTransparent),
            BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryAccentColorSlightTransparent),
        };

        _list = new StackPanel { Spacing = 0 };
        _listScroller = new ScrollViewer
        {
            Content = _list,
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            ZoomMode = ZoomMode.Disabled,
        };
        _listBorder = new Border
        {
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(1),
            MaxWidth = 400,
            Child = _listScroller,
        };

        var presenterStyle = new Style(typeof(FlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Colors.Transparent)));
        presenterStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        presenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        presenterStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(0)));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, double.PositiveInfinity));

        _flyout = new Flyout
        {
            Content = _listBorder,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            FlyoutPresenterStyle = presenterStyle,
        };
        _flyout.Opened += (_, _) =>
        {
            if (SelectedIndex >= 0) _optionButtons[SelectedIndex].StartBringIntoView();
        };
        _flyout.Closed += (_, _) => UpdateBoxColors();

        _box.PointerEntered += (_, _) => { _pointerOver = true; UpdateBoxColors(); };
        _box.PointerExited += (_, _) => { _pointerOver = false; UpdateBoxColors(); };
        //C on press rather than release: a click on the box while the list is open only closes it (light dismiss),
        //C where a release would land after the close and open it straight back up
        _box.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(_box).Properties.IsLeftButtonPressed) return;
            Open();
        };

        KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case VirtualKey.Enter or VirtualKey.Space:
                    Open();
                    break;
                case VirtualKey.Up when SelectedIndex > 0:
                    Choose(SelectedIndex - 1);
                    break;
                case VirtualKey.Down when SelectedIndex < Options.Count - 1:
                    Choose(SelectedIndex + 1);
                    break;
                default:
                    return;
            }
            e.Handled = true;
        };

        Content = _box;
        ApplyTheme();
    }

    public void Open()
    {
        if (Options.Count == 0 || _flyout.IsOpen) return;

        _listBorder.MinWidth = ActualWidth;
        UpdateBoxColors(open: true);
        _flyout.ShowAt(_box);
    }

    private void Choose(int index)
    {
        if (index == SelectedIndex) return;
        SelectedIndex = index;
        SelectionChanged?.Invoke(index);
    }

    private void RebuildList()
    {
        _list.Children.Clear();
        _optionButtons.Clear();

        for (var i = 0; i < Options.Count; i++)
        {
            var index = i;
            var button = new FTextButton
            {
                ButtonText = Options[i],
                FontSize = 14,
                MinHeight = 26,
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ContentHorizontalAlignment = HorizontalAlignment.Left,
                CurrentTheme = CurrentTheme,
                ForceHighlight = i == SelectedIndex,
            };
            button.OnClick += (_, _) =>
            {
                _flyout.Hide();
                Choose(index);
            };
            _optionButtons.Add(button);
            _list.Children.Add(button);
        }
    }

    private void UpdateLabel()
    {
        var chosen = SelectedIndex >= 0;
        _label.Text = chosen ? Options[SelectedIndex] : PlaceholderText;
        _label.Foreground = new SolidColorBrush(chosen ? CurrentTheme.PrimaryForegroundColor : CurrentTheme.SecondaryForegroundColor);
        ToolTipService.SetToolTip(this, chosen ? Options[SelectedIndex] : null);
    }

    private void UpdateHighlight()
    {
        for (var i = 0; i < _optionButtons.Count; i++)
            _optionButtons[i].ForceHighlight = i == SelectedIndex;
    }

    private void UpdateBoxColors(bool open = false)
    {
        open |= _flyout.IsOpen;
        ChangeColorAnimation(_box.BorderBrush, open || _pointerOver
            ? CurrentTheme.PrimaryHighlightColor
            : CurrentTheme.SecondaryAccentColorSlightTransparent);
        ChangeColorAnimation(_box.Background, open
            ? CurrentTheme.PrimaryBackgroundColorSlightTransparent
            : CurrentTheme.PrimaryAccentColorSlightTransparent);
    }

    private void ApplyTheme()
    {
        _box.Background = new SolidColorBrush(CurrentTheme.PrimaryAccentColorSlightTransparent);
        _box.BorderBrush = new SolidColorBrush(_pointerOver ? CurrentTheme.PrimaryHighlightColor : CurrentTheme.SecondaryAccentColorSlightTransparent);
        _chevron.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
        _listBorder.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorSlightTransparent);
        _listBorder.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);

        foreach (var button in _optionButtons)
            button.CurrentTheme = CurrentTheme;

        UpdateLabel();
    }
}

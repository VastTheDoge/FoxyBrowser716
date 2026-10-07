using FoxyBrowser716.DataObjects.Basic;
using Material.Icons.WinUI3;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace FoxyBrowser716.Controls.Generic;

public sealed partial class FContextMenu : UserControl
{
    private StackPanel _stackPanel;
    private ScrollViewer _scrollViewer;
    private Border _border;
    private Popup _popup;
    private double _menuWidth;
    public event Action? OnClose;

    internal Theme CurrentTheme { get; set { field = value; ApplyTheme(); } } = DefaultThemes.DarkMode;

    /// <summary>
    /// Use the less transparent background. Set for menus drawn over web content, where the usual
    /// very transparent background makes text hard to read.
    /// </summary>
    public bool UseSolidBackground { get; set { field = value; ApplyTheme(); } }

    public bool IsOpen => _popup.IsOpen;

    public FContextMenu()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        VerticalAlignment = VerticalAlignment.Top;
        HorizontalAlignment = HorizontalAlignment.Left;

        _stackPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        _scrollViewer = new ScrollViewer
        {
            Content = _stackPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            ZoomMode = ZoomMode.Disabled,
        };

        _border = new Border
        {
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _scrollViewer,
        };

        _popup = new Popup
        {
            Child = _border,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsLightDismissEnabled = true,
            LightDismissOverlayMode = LightDismissOverlayMode.On,
        };

        _popup.Closed += (_, _) => OnClose?.Invoke();

        Content = _popup;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (_border is null) return; // UseSolidBackground can be set from an initializer before layout exists

        _border.Background = new SolidColorBrush(UseSolidBackground
            ? CurrentTheme.PrimaryBackgroundColorSlightTransparent
            : CurrentTheme.PrimaryBackgroundColorVeryTransparent);
        _border.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);

        if (_stackPanel.Children is not null)
            foreach (var child in _stackPanel.Children)
            {
                if (child is FIconButton iconButton)
                {
                    iconButton.CurrentTheme = CurrentTheme;
                }
                else if (child is FTextButton textButton)
                {
                    textButton.CurrentTheme = CurrentTheme;
                    if (textButton.Icon is MaterialIcon icon)
                        icon.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
                }
                else if (child is Border separator)
                {
                    separator.Background = new SolidColorBrush(CurrentTheme.SecondaryAccentColorSlightTransparent);
                }
            }
    }

    public void SetItems(IEnumerable<MenuItem> items, double iconWidth = 1000) // quick refactor, but this is changed to only affect icons
    {
        var itemsA = items.ToArray();

        _menuWidth = iconWidth;

        _stackPanel.Children.Clear();

        if (itemsA.Length == 0)
        {
            _popup.IsOpen = false;
            return;
        }

        var allIconOnly = itemsA.All(item => item.IsIconOnly);

        if (allIconOnly)
        {
            CreateIconOnlyLayout(itemsA);
        }
        else
        {
            CreateMixedLayout(itemsA);
        }

        _popup.IsOpen = true;
    }

    public void SetCustomItems(IEnumerable<UserControl> items, double menuWidth = 100)
    {
        var itemsA = items.ToArray();

        _menuWidth = menuWidth;

        _stackPanel.Children.Clear();

        foreach (var item in itemsA)
        {
            _stackPanel.Children.Add(item);
        }

        _popup.IsOpen = true;
    }

    public void Close() => _popup.IsOpen = false;

    /// <summary>
    /// Places the open menu at <paramref name="position"/> (in the coordinates of this control's parent) while
    /// keeping it inside <paramref name="bounds"/>: it shifts left at the right edge and opens upward near the bottom.
    /// </summary>
    public void PlaceWithin(Point position, Rect bounds)
    {
        _scrollViewer.MaxHeight = Math.Max(60, bounds.Height - 8);
        _border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _border.DesiredSize;
        var height = Math.Min(size.Height, _scrollViewer.MaxHeight + 4);

        var x = Math.Max(bounds.Left, Math.Min(position.X, bounds.Right - size.Width));
        var y = position.Y + height > bounds.Bottom
            ? Math.Max(bounds.Top, position.Y - height)
            : position.Y;

        Margin = new Thickness(x, y, 0, 0);
    }

    private void CreateIconOnlyLayout(MenuItem[] items)
    {
        var buttonSize = _menuWidth - 4;

        foreach (var item in items)
        {
            var button = new FIconButton
            {
                Content = item.Icon,
                Padding = new Thickness(item.IconPadding),
                Width = buttonSize,
                Height = buttonSize,
                Margin = new Thickness(0),
                CurrentTheme = CurrentTheme,
            };

            button.OnClick += (_, _) =>
            {
                item.OnClick?.Invoke();
                if (item.CloseOnClick)
                    _popup.IsOpen = false;
            };

            _stackPanel.Children.Add(button);
        }
    }

    private void CreateMixedLayout(MenuItem[] items)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                // a separator at either end, or two in a row, is just noise
                if (_stackPanel.Children.Count == 0 || _stackPanel.Children[^1] is Border) continue;

                _stackPanel.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(8, 3, 8, 3),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Background = new SolidColorBrush(CurrentTheme.SecondaryAccentColorSlightTransparent),
                });
                continue;
            }

            var icon = item.Icon;
            if (icon is null && item.IsChecked == true)
                icon = new MaterialIcon { Kind = MaterialIconKind.Check, Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor) };

            var button = new FTextButton
            {
                Icon = icon,
                Padding = new Thickness(item.IconPadding),
                ButtonText = item.Text ?? string.Empty,
                //Width = _menuWidth - 4,
                Margin = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                CurrentTheme = CurrentTheme,
                ContentHorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(8),
                ForceHighlight = item.IsChecked == true && item.Icon is not null,
            };

            if (!item.IsEnabled)
            {
                button.Opacity = 0.45;
                button.IsHitTestVisible = false;
            }

            button.OnClick += (_, _) =>
            {
                item.OnClick?.Invoke();
                if (item.CloseOnClick)
                    _popup.IsOpen = false;
            };

            _stackPanel.Children.Add(button);
        }

        if (_stackPanel.Children.Count > 0 && _stackPanel.Children[^1] is Border)
            _stackPanel.Children.RemoveAt(_stackPanel.Children.Count - 1);
    }

    public class MenuItem
    {
        public UIElement? Icon { get; set; }
        public string? IconUri { get; set; }

        public double IconPadding { get; set; }

        public string? Text { get; set; }
        public Action? OnClick { get; set; }

        public bool CloseOnClick { get; set; }

        /// <summary>Disabled items are shown dimmed and cannot be clicked.</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>null for a normal item; true/false for a check or radio item.</summary>
        public bool? IsChecked { get; set; }

        public bool IsSeparator { get; private init; }

        public MenuItem(UIElement? icon, double iconPadding, string? text, Action? onClick, bool closeOnClick = true)
        {
            if (icon == null && string.IsNullOrEmpty(text))
            {
                throw new ArgumentException("MenuItem cannot have both null icon and null/empty text");
            }

            Icon = icon;
            IconPadding = iconPadding;
            Text = text;
            OnClick = onClick;
            CloseOnClick = closeOnClick;
        }

        public MenuItem(string text, Action? onClick)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentException("MenuItem cannot have both null icon and null/empty text");
            }

            Text = text;
            OnClick = onClick;
        }

        private MenuItem() { }

        /// <summary>A thin divider line between groups of items.</summary>
        public static MenuItem Separator() => new() { IsSeparator = true };

        public bool IsIconOnly => Icon != null && string.IsNullOrEmpty(Text);
    }
}

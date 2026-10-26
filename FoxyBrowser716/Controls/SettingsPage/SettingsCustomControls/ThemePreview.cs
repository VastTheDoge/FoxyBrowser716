using Material.Icons.WinUI3;
using Microsoft.UI.Xaml.Shapes;

namespace FoxyBrowser716.Controls.SettingsPage.SettingsCustomControls;

// Claude Doc
/// <summary>
/// A browser window in miniature, colored by one theme: frame, top bar, collapsed sidebar with tab cards, and a
/// home page card over the window's background picture. Laid out like the real window at
/// <see cref="DesignWidth"/> × <see cref="DesignHeight"/> with the real control sizes, then scaled to whatever
/// size it is given, so set Width and Height in a 16:10 ratio.
/// </summary>
public sealed partial class ThemePreview : UserControl
{
	public const double DesignWidth = 480;
	public const double DesignHeight = 300;

	private readonly Grid _frame;
	private readonly Image _image;
	private readonly Grid _borderGrid;
	private readonly Border _topBar;
	private readonly Grid _addressPill;
	private readonly TextBlock _addressText;
	private readonly Border _leftBar;
	private readonly Border _sidebarDivider;
	private readonly List<(Border card, bool active)> _tabCards = [];
	private readonly Border _tabArea;
	private readonly Border _card;
	private readonly TextBlock _cardTitle;
	private readonly TextBlock _cardText;
	private readonly Border _primaryButton;
	private readonly TextBlock _primaryButtonText;
	private readonly Border _secondaryButton;
	private readonly TextBlock _secondaryButtonText;
	private readonly Ellipse _yes;
	private readonly Ellipse _no;
	private readonly List<MaterialIcon> _icons = [];

	public Theme Theme
	{
		get => field;
		set
		{
			field = value;
			ApplyTheme();
		}
	} = DefaultThemes.DarkMode;

	public ImageSource? BackgroundImage
	{
		get => _image.Source;
		set => _image.Source = value;
	}

	public ThemePreview()
	{
		_image = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

		//C TopBar.xaml: 26 high with a 2px border, 22px buttons, the address pill 22 high with an 11 radius and 18px buttons inside
		_addressText = new TextBlock { Text = "Search or enter a URL", FontSize = 14, Height = 18, Margin = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.Clip };
		_addressPill = new Grid { Height = 22, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Center };
		AddColumns(_addressPill, auto: 3, star: 1, autoAfter: 2);
		_addressPill.Children.Add(IconButton(MaterialIconKind.Refresh, 18, 14, column: 0));
		_addressPill.Children.Add(IconButton(MaterialIconKind.ArrowBack, 18, 14, column: 1));
		_addressPill.Children.Add(IconButton(MaterialIconKind.ArrowForward, 18, 14, column: 2));
		Grid.SetColumn(_addressText, 3);
		_addressPill.Children.Add(_addressText);
		_addressPill.Children.Add(IconButton(MaterialIconKind.Search, 18, 14, column: 4));
		_addressPill.Children.Add(IconButton(MaterialIconKind.AbTesting, 18, 12, column: 5));

		var topBarGrid = new Grid();
		AddColumns(topBarGrid, auto: 2, star: 1, autoAfter: 5);
		topBarGrid.Children.Add(IconButton(MaterialIconKind.ArrowExpandRight, 22, 16, column: 0));
		topBarGrid.Children.Add(IconButton(MaterialIconKind.Menu, 22, 18, column: 1));
		Grid.SetColumn(_addressPill, 2);
		topBarGrid.Children.Add(_addressPill);
		//C the real drag zone is 150 wide; shorter here so the pill keeps its text at this window size
		var dragZone = new Border { Width = 60 };
		Grid.SetColumn(dragZone, 3);
		topBarGrid.Children.Add(dragZone);
		topBarGrid.Children.Add(IconButton(MaterialIconKind.Minimize, 22, 18, column: 4));
		topBarGrid.Children.Add(IconButton(MaterialIconKind.Maximize, 22, 16, column: 5));
		topBarGrid.Children.Add(IconButton(MaterialIconKind.ArrowExpand, 22, 16, column: 6));
		topBarGrid.Children.Add(IconButton(MaterialIconKind.Close, 22, 18, column: 7));
		_topBar = new Border
		{
			Height = 26, VerticalAlignment = VerticalAlignment.Top,
			BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8, 8, 8, 0),
			Child = topBarGrid,
		};

		//C NewLeftBar.xaml: 30 wide with a 2px border, collapsed so each 26px tab card shows only its icon; home, tabs, divider, a pin
		var sidebar = new StackPanel();
		sidebar.Children.Add(TabCard(MaterialIconKind.Home, active: true));
		sidebar.Children.Add(TabCard(MaterialIconKind.Web, active: false));
		sidebar.Children.Add(TabCard(MaterialIconKind.Web, active: false));
		_sidebarDivider = new Border { Height = 2, Margin = new Thickness(2, 4, 2, 4), CornerRadius = new CornerRadius(1) };
		sidebar.Children.Add(_sidebarDivider);
		sidebar.Children.Add(TabCard(MaterialIconKind.Web, active: false));
		_leftBar = new Border
		{
			Width = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 26, 0, 0),
			BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(0, 8, 8, 8),
			Child = sidebar,
		};

		_cardTitle = new TextBlock { Text = "Good evening", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
		_cardText = new TextBlock { Text = "Text, buttons and the yes/no colors", FontSize = 12, TextWrapping = TextWrapping.Wrap };
		_primaryButtonText = new TextBlock { Text = "Selected", FontSize = 12 };
		_primaryButton = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 4, 10, 4), Child = _primaryButtonText };
		_secondaryButtonText = new TextBlock { Text = "Button", FontSize = 12 };
		_secondaryButton = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 4, 10, 4), Child = _secondaryButtonText };
		_yes = new Ellipse { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
		_no = new Ellipse { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
		_card = new Border
		{
			Width = 250, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
			CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2), Padding = new Thickness(14, 10, 14, 12),
			Child = new StackPanel
			{
				Spacing = 8,
				Children =
				{
					_cardTitle, _cardText,
					new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _primaryButton, _secondaryButton, _yes, _no } },
				},
			},
		};
		//C MainWindow.xaml TabHolder: starts where the two bars meet
		_tabArea = new Border
		{
			Margin = new Thickness(30, 26, 0, 0), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8),
			Child = _card,
		};

		_borderGrid = new Grid
		{
			BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8),
			Children = { _tabArea, _topBar, _leftBar },
		};
		_frame = new Grid
		{
			Width = DesignWidth, Height = DesignHeight, CornerRadius = new CornerRadius(10),
			Children =
			{
				new Border { Margin = new Thickness(2), CornerRadius = new CornerRadius(8), Child = _image },
				_borderGrid,
			},
		};

		Content = new Viewbox { Stretch = Stretch.Uniform, Child = _frame };
		ApplyTheme();
	}

	private static void AddColumns(Grid grid, int auto, int star, int autoAfter)
	{
		for (var i = 0; i < auto; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		for (var i = 0; i < star; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		for (var i = 0; i < autoAfter; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
	}

	private MaterialIcon Icon(MaterialIconKind kind, double size)
	{
		var icon = new MaterialIcon { Kind = kind, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
		_icons.Add(icon);
		return icon;
	}

	private Border IconButton(MaterialIconKind kind, double buttonSize, double iconSize, int column)
	{
		var button = new Border { Width = buttonSize, Height = buttonSize, Child = Icon(kind, iconSize) };
		Grid.SetColumn(button, column);
		return button;
	}

	private Border TabCard(MaterialIconKind kind, bool active)
	{
		var card = new Border
		{
			Height = 26, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(2),
			Child = Icon(kind, 18),
		};
		_tabCards.Add((card, active));
		return card;
	}

	//C mirrors MainWindow, TopBar, NewLeftBar, NewTabCard, Widget.ApplyCardTheme and FTextButton; keep in step when those change
	private void ApplyTheme()
	{
		var theme = Theme;
		_frame.Background = new SolidColorBrush(theme.PrimaryHighlightColor);
		_borderGrid.BorderBrush = new SolidColorBrush(theme.PrimaryHighlightColor);

		_topBar.Background = new SolidColorBrush(theme.PrimaryBackgroundColorVeryTransparent);
		_topBar.BorderBrush = new SolidColorBrush(theme.SecondaryBackgroundColor);
		_addressPill.Background = new SolidColorBrush(theme.PrimaryAccentColorSlightTransparent);
		_addressPill.BorderBrush = new SolidColorBrush(theme.SecondaryAccentColorSlightTransparent);
		_addressText.Foreground = new SolidColorBrush(theme.SecondaryForegroundColor);

		_leftBar.Background = new SolidColorBrush(theme.PrimaryBackgroundColorVeryTransparent);
		_leftBar.BorderBrush = new SolidColorBrush(theme.SecondaryBackgroundColor);
		_sidebarDivider.Background = new SolidColorBrush(theme.SecondaryAccentColorSlightTransparent);
		foreach (var (card, active) in _tabCards)
		{
			card.Background = new SolidColorBrush(theme.PrimaryBackgroundColorVeryTransparent);
			card.BorderBrush = new SolidColorBrush(active ? theme.PrimaryHighlightColor : theme.SecondaryBackgroundColor);
		}

		_tabArea.BorderBrush = new SolidColorBrush(theme.SecondaryBackgroundColor);
		_card.Background = new SolidColorBrush(theme.PrimaryBackgroundColorVeryTransparent);
		_card.BorderBrush = new SolidColorBrush(theme.SecondaryBackgroundColorSlightTransparent);
		_cardTitle.Foreground = new SolidColorBrush(theme.PrimaryForegroundColor);
		_cardText.Foreground = new SolidColorBrush(theme.SecondaryForegroundColor);
		_primaryButton.Background = new SolidColorBrush(theme.PrimaryHighlightColor);
		_primaryButtonText.Foreground = new SolidColorBrush(theme.PrimaryForegroundColor);
		_secondaryButton.Background = new SolidColorBrush(theme.PrimaryAccentColorSlightTransparent);
		_secondaryButtonText.Foreground = new SolidColorBrush(theme.PrimaryForegroundColor);
		_yes.Fill = new SolidColorBrush(theme.YesColor);
		_no.Fill = new SolidColorBrush(theme.NoColor);

		foreach (var icon in _icons) icon.Foreground = new SolidColorBrush(theme.PrimaryForegroundColor);
	}
}

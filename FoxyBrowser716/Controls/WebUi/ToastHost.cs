using Material.Icons.WinUI3;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace FoxyBrowser716.Controls.WebUi;

/// <summary>
/// Small themed notifications in the bottom-right corner of a window ("Extension installed", errors from
/// background work). They stack, and each closes by itself after a few seconds or when clicked.
/// </summary>
public sealed class ToastHost
{
	private const double ToastWidth = 320;

	private readonly Popup _popup;
	private readonly StackPanel _stack;
	private readonly FrameworkElement _bounds;
	private readonly List<(Border card, MaterialIcon icon, TextBlock title, TextBlock message, bool isError)> _toasts = [];

	public Theme CurrentTheme
	{
		get;
		set
		{
			field = value;
			foreach (var toast in _toasts) ApplyTheme(toast);
		}
	} = DefaultThemes.DarkMode;

	/// <param name="host">Panel the popup is added to; toasts are placed at its bottom-right corner.</param>
	public ToastHost(Panel host)
	{
		_bounds = host;
		_stack = new StackPanel { Spacing = 6, Width = ToastWidth };
		_popup = new Popup
		{
			Child = _stack,
			IsLightDismissEnabled = false,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
		};
		host.Children.Add(_popup);
		host.SizeChanged += (_, _) => Reposition();
	}

	public void Show(string title, string? message = null, MaterialIconKind icon = MaterialIconKind.Information, bool isError = false, double seconds = 5)
	{
		var iconElement = new MaterialIcon { Kind = icon, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Top };
		var titleBlock = WebUiStyle.Text(title, 13, bold: true);
		var messageBlock = WebUiStyle.Text(message ?? string.Empty, 12);
		messageBlock.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;

		var text = new StackPanel();
		text.Children.Add(titleBlock);
		text.Children.Add(messageBlock);

		var layout = new Grid
		{
			ColumnSpacing = 8,
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
			},
		};
		Grid.SetColumn(text, 1);
		layout.Children.Add(iconElement);
		layout.Children.Add(text);

		var card = WebUiStyle.Card(layout, 10);
		var toast = (card, iconElement, titleBlock, messageBlock, isError);
		_toasts.Add(toast);
		ApplyTheme(toast);

		_stack.Children.Add(card);
		_popup.IsOpen = true;
		Reposition();

		var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
		timer.Interval = TimeSpan.FromSeconds(seconds);
		timer.IsRepeating = false;
		timer.Tick += (_, _) => Remove(toast);
		card.PointerReleased += (_, _) =>
		{
			timer.Stop();
			Remove(toast);
		};
		timer.Start();
	}

	private void Remove((Border card, MaterialIcon icon, TextBlock title, TextBlock message, bool isError) toast)
	{
		if (!_toasts.Remove(toast)) return;
		_stack.Children.Remove(toast.card);
		if (_toasts.Count == 0) _popup.IsOpen = false;
		else Reposition();
	}

	private void Reposition()
	{
		if (!_popup.IsOpen) return;
		_stack.Measure(new Size(ToastWidth, double.PositiveInfinity));
		_popup.HorizontalOffset = Math.Max(0, _bounds.ActualWidth - ToastWidth - 12);
		_popup.VerticalOffset = Math.Max(0, _bounds.ActualHeight - _stack.DesiredSize.Height - 12);
	}

	private void ApplyTheme((Border card, MaterialIcon icon, TextBlock title, TextBlock message, bool isError) toast)
	{
		WebUiStyle.ApplyCardTheme(toast.card, CurrentTheme, solid: true);
		toast.icon.Foreground = new SolidColorBrush(toast.isError ? CurrentTheme.NoColor : CurrentTheme.PrimaryHighlightColor);
		toast.title.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		toast.message.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
	}
}

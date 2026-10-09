using FoxyBrowser716.DataObjects.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Shapes;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("Analog Clock Widget", MaterialIconKind.ClockOutline, WidgetCategory.TimeDate)]
public partial class AnalogClockWidget : WidgetBase
{
	private const double Center = 100;

	private readonly List<Line> _minorTicks = [];
	private readonly List<Line> _majorTicks = [];
	private readonly List<TextBlock> _numbers = [];

	private DispatcherQueueTimer? _timer;
	private bool _smoothSeconds;

	protected AnalogClockWidget()
	{
		InitializeComponent();
		BuildMarks();
		WidgetSettings =
		[
			new BoolSetting("Show Second Hand", "", true, v => SecondHand.Visibility = v ? Visibility.Visible : Visibility.Collapsed),
			new BoolSetting("Smooth Second Hand", "Sweep instead of ticking (updates more often)", false, v =>
			{
				_smoothSeconds = v;
				if (_timer is not null) _timer.Interval = TickInterval;
			}),
			new BoolSetting("Show Numbers", "", true, v => _numbers.ForEach(n => n.Visibility = v ? Visibility.Visible : Visibility.Collapsed)),
		];
	}

	private TimeSpan TickInterval => TimeSpan.FromMilliseconds(_smoothSeconds ? 50 : 250);

	protected override Task Initialize()
	{
		_timer = CreateLiveTimer(TickInterval, UpdateHands);
		ApplyTheme();
		return Task.CompletedTask;
	}

	private void BuildMarks()
	{
		for (var i = 0; i < 60; i++)
		{
			var major = i % 5 == 0;
			var angle = i * 6 * Math.PI / 180;
			var (outer, inner) = (92.0, major ? 82.0 : 87.0);
			var tick = new Line
			{
				X1 = Center + inner * Math.Sin(angle), Y1 = Center - inner * Math.Cos(angle),
				X2 = Center + outer * Math.Sin(angle), Y2 = Center - outer * Math.Cos(angle),
				StrokeThickness = major ? 3 : 1.2,
				StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
			};
			(major ? _majorTicks : _minorTicks).Add(tick);
			Marks.Children.Add(tick);
		}

		for (var hour = 1; hour <= 12; hour++)
		{
			var angle = hour * 30 * Math.PI / 180;
			// fixed-size, centered box so every number sits exactly on the ring regardless of its glyph width
			var number = new TextBlock
			{
				Text = hour.ToString(), FontSize = 16, Width = 24, Height = 22,
				TextAlignment = TextAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
			};
			Canvas.SetLeft(number, Center + 68 * Math.Sin(angle) - 12);
			Canvas.SetTop(number, Center - 68 * Math.Cos(angle) - 11);
			_numbers.Add(number);
			Marks.Children.Add(number);
		}
	}

	private void UpdateHands()
	{
		var now = DateTime.Now;
		var seconds = now.Second + (_smoothSeconds ? now.Millisecond / 1000.0 : 0);
		var minutes = now.Minute + seconds / 60;
		var hours = now.Hour % 12 + minutes / 60;

		SetHand(HourHand, hours * 30, 50);
		SetHand(MinuteHand, minutes * 6, 72);
		SetHand(SecondHand, seconds * 6, 80);
	}

	private static void SetHand(Line hand, double degrees, double length)
	{
		var radians = degrees * Math.PI / 180;
		hand.X2 = Center + length * Math.Sin(radians);
		hand.Y2 = Center - length * Math.Cos(radians);
	}

	protected override void ApplyTheme()
	{
		Dial.Fill = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
		Dial.Stroke = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);

		var major = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		var minor = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		_majorTicks.ForEach(t => t.Stroke = major);
		_minorTicks.ForEach(t => t.Stroke = minor);
		_numbers.ForEach(n => n.Foreground = major);

		HourHand.Stroke = major;
		MinuteHand.Stroke = major;
		SecondHand.Stroke = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);
		CenterDot.Fill = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);
	}
}

using System.Globalization;
using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("Countdown Widget", MaterialIconKind.TimerSandEmpty, WidgetCategory.TimeDate)]
public partial class CountdownWidget : WidgetBase
{
	private string _title = "New Year";
	private string _targetText = new DateTime(DateTime.Now.Year + 1, 1, 1).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
	private bool _showSeconds = true;
	private DateTime? _target;

	protected CountdownWidget()
	{
		InitializeComponent();
		WidgetSettings =
		[
			new StringSetting("Title", "", _title, v => { _title = v; Render(); }),
			new StringSetting("Target Date", "e.g. 2026-12-25 or 2026-12-25 17:30", _targetText, v => { _targetText = v; ParseTarget(); Render(); }),
			new BoolSetting("Show Seconds", "", true, v =>
			{
				_showSeconds = v;
				SecondsPanel.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
				SecondsColumn.Width = v ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
			}),
		];
		ParseTarget();
	}

	protected override Task Initialize()
	{
		CreateLiveTimer(TimeSpan.FromMilliseconds(250), Render);
		ApplyTheme();
		return Task.CompletedTask;
	}

	private void ParseTarget()
	{
		var text = _targetText.Trim();
		// accept ISO-style dates everywhere, then whatever the user's locale writes dates as
		_target = DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)
		          || DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out d)
			? d
			: null;
	}

	private void Render()
	{
		if (_target is not { } target)
		{
			TitleText.Text = string.IsNullOrWhiteSpace(_title) ? "Countdown" : _title;
			DaysText.Text = HoursText.Text = MinutesText.Text = SecondsText.Text = "--";
			TargetText.Text = "Set a valid date in this widget's settings";
			return;
		}

		var now = DateTime.Now;
		var passed = now >= target;
		// once the date passes, keep counting upward rather than sitting at zero
		var span = passed ? now - target : target - now;
		if (!passed && !_showSeconds) span += TimeSpan.FromSeconds(59); // round minutes up so 00 min means "now"

		TitleText.Text = (string.IsNullOrWhiteSpace(_title) ? "Countdown" : _title) + (passed ? " (since)" : "");
		DaysText.Text = ((int)span.TotalDays).ToString(CultureInfo.CurrentCulture);
		// a column fits three 34pt digits; shrink beyond that instead of clipping
		DaysText.FontSize = DaysText.Text.Length > 3 ? 34.0 * 3 / DaysText.Text.Length : 34;
		HoursText.Text = span.Hours.ToString("00");
		MinutesText.Text = span.Minutes.ToString("00");
		SecondsText.Text = span.Seconds.ToString("00");
		TargetText.Text = target.TimeOfDay == TimeSpan.Zero
			? target.ToString("D", CultureInfo.CurrentCulture)
			: target.ToString("f", CultureInfo.CurrentCulture);
	}

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);
		var primary = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		var secondary = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		TitleText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);
		foreach (var t in new[] { DaysText, HoursText, MinutesText, SecondsText }) t.Foreground = primary;
		foreach (var t in new[] { DaysLabel, HoursLabel, MinutesLabel, SecondsLabel, TargetText }) t.Foreground = secondary;
		Render();
	}
}

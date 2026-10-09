using System.Diagnostics;
using Material.Icons.WinUI3;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("Stopwatch Widget", MaterialIconKind.TimerPlayOutline, WidgetCategory.Tools)]
public partial class StopwatchWidget : WidgetBase
{
	private readonly Stopwatch _stopwatch = new();
	private readonly List<TimeSpan> _laps = [];
	private bool? _shownRunning;

	protected StopwatchWidget()
	{
		InitializeComponent();
	}

	protected override Task Initialize()
	{
		CreateLiveTimer(TimeSpan.FromMilliseconds(50), Render);
		ApplyTheme();
		return Task.CompletedTask;
	}

	private static string Format(TimeSpan t) => t.TotalHours >= 1
		? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
		: $"{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}";

	private void Render()
	{
		TimeText.Text = Format(_stopwatch.Elapsed);
		if (_shownRunning != _stopwatch.IsRunning)
		{
			ButtonPlayPause.Content = new MaterialIcon { Kind = _stopwatch.IsRunning ? MaterialIconKind.Pause : MaterialIconKind.Play };
			_shownRunning = _stopwatch.IsRunning;
		}
	}

	private void ButtonPlayPause_OnClick(object sender, RoutedEventArgs e)
	{
		if (_stopwatch.IsRunning) _stopwatch.Stop();
		else _stopwatch.Start();
		Render();
	}

	private void ButtonReset_OnClick(object sender, RoutedEventArgs e)
	{
		_stopwatch.Reset();
		_laps.Clear();
		LapText.Text = " "; // keep the line's height so the layout doesn't jump
		Render();
	}

	private void ButtonLap_OnClick(object sender, RoutedEventArgs e)
	{
		if (!_stopwatch.IsRunning) return;
		var total = _stopwatch.Elapsed;
		var lap = total - (_laps.Count > 0 ? _laps[^1] : TimeSpan.Zero);
		_laps.Add(total);
		LapText.Text = $"Lap {_laps.Count}: {Format(lap)}";
	}

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);
		TimeText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		LapText.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		ButtonPlayPause.CurrentTheme = CurrentTheme;
		ButtonReset.CurrentTheme = CurrentTheme;
		ButtonLap.CurrentTheme = CurrentTheme;
	}
}

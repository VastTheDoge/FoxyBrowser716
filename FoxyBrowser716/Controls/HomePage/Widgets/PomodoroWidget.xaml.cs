using FoxyBrowser716.DataObjects.Settings;
using Material.Icons.WinUI3;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("Pomodoro Timer Widget", MaterialIconKind.TimerOutline, WidgetCategory.Tools)]
public partial class PomodoroWidget : WidgetBase
{
	private enum Phase { Focus, ShortBreak, LongBreak }

	private const double ProgressWidth = 180;

	private int _focusMinutes = 25;
	private int _shortBreakMinutes = 5;
	private int _longBreakMinutes = 15;
	private int _sessionsPerLongBreak = 4;
	private bool _autoStart;

	private Phase _phase = Phase.Focus;
	private int _completedFocusSessions;
	private bool _running;
	private DateTime _endsAt;           // valid while running; wall-clock based so ticks can't drift
	private TimeSpan _remaining;        // valid while paused
	private bool _justFinished;         // highlights the card until the next interaction
	private bool? _shownRunning;        // which icon the play/pause button currently has

	protected PomodoroWidget()
	{
		InitializeComponent();
		WidgetSettings =
		[
			new IntSetting("Focus Minutes", "", _focusMinutes, v => { _focusMinutes = v; ResetIfIdle(); }, 1, 180),
			new IntSetting("Short Break Minutes", "", _shortBreakMinutes, v => { _shortBreakMinutes = v; ResetIfIdle(); }, 1, 60),
			new IntSetting("Long Break Minutes", "", _longBreakMinutes, v => { _longBreakMinutes = v; ResetIfIdle(); }, 1, 120),
			new IntSetting("Sessions Before Long Break", "", _sessionsPerLongBreak, v => _sessionsPerLongBreak = v, 1, 12),
			new BoolSetting("Auto-Start Next Phase", "", false, v => _autoStart = v),
		];
		_remaining = PhaseLength(_phase);
	}

	protected override Task Initialize()
	{
		CreateLiveTimer(TimeSpan.FromMilliseconds(250), Tick);
		ApplyTheme();
		return Task.CompletedTask;
	}

	private TimeSpan PhaseLength(Phase phase) => TimeSpan.FromMinutes(phase switch
	{
		Phase.Focus => _focusMinutes,
		Phase.ShortBreak => _shortBreakMinutes,
		_ => _longBreakMinutes,
	});

	private TimeSpan Remaining => _running ? _endsAt - DateTime.Now : _remaining;

	private void ResetIfIdle()
	{
		if (_running) return;
		_remaining = PhaseLength(_phase);
		Render();
	}

	private void Tick()
	{
		if (_running && Remaining <= TimeSpan.Zero) AdvancePhase(finishedNaturally: true);
		Render();
	}

	private void AdvancePhase(bool finishedNaturally)
	{
		if (_phase == Phase.Focus)
		{
			_completedFocusSessions++;
			_phase = _completedFocusSessions % Math.Max(1, _sessionsPerLongBreak) == 0 ? Phase.LongBreak : Phase.ShortBreak;
		}
		else _phase = Phase.Focus;

		_remaining = PhaseLength(_phase);
		_running = finishedNaturally && _autoStart;
		if (_running) _endsAt = DateTime.Now + _remaining;
		_justFinished = finishedNaturally;
		ApplyTheme();
	}

	private void Render()
	{
		var remaining = Remaining < TimeSpan.Zero ? TimeSpan.Zero : Remaining;
		// round up so the clock shows 25:00 at the start and hits 00:00 exactly when the phase ends
		var totalSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
		TimeText.Text = totalSeconds >= 3600
			? $"{totalSeconds / 3600}:{totalSeconds / 60 % 60:00}:{totalSeconds % 60:00}"
			: $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";

		ModeText.Text = _phase switch
		{
			Phase.Focus => "Focus",
			Phase.ShortBreak => "Short Break",
			_ => "Long Break",
		};
		SessionText.Text = $"{_completedFocusSessions} done";

		var length = PhaseLength(_phase).TotalSeconds;
		ProgressFill.Width = length <= 0 ? 0 : ProgressWidth * Math.Clamp(1 - remaining.TotalSeconds / length, 0, 1);
		if (_shownRunning != _running)
		{
			ButtonPlayPause.Content = new MaterialIcon { Kind = _running ? MaterialIconKind.Pause : MaterialIconKind.Play };
			_shownRunning = _running;
		}
	}

	private void ButtonPlayPause_OnClick(object sender, RoutedEventArgs e)
	{
		ClearFinishedHighlight();
		if (_running)
		{
			_remaining = Remaining;
			_running = false;
		}
		else
		{
			if (_remaining <= TimeSpan.Zero) _remaining = PhaseLength(_phase);
			_endsAt = DateTime.Now + _remaining;
			_running = true;
		}
		Render();
	}

	private void ButtonReset_OnClick(object sender, RoutedEventArgs e)
	{
		ClearFinishedHighlight();
		_running = false;
		_remaining = PhaseLength(_phase);
		Render();
	}

	private void ButtonSkip_OnClick(object sender, RoutedEventArgs e)
	{
		ClearFinishedHighlight();
		AdvancePhase(finishedNaturally: false);
		Render();
	}

	private void ClearFinishedHighlight()
	{
		if (!_justFinished) return;
		_justFinished = false;
		ApplyTheme();
	}

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);
		if (_justFinished) Card.BorderBrush = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);

		var accent = _phase == Phase.Focus ? CurrentTheme.PrimaryHighlightColor : CurrentTheme.YesColor;
		ModeText.Foreground = new SolidColorBrush(accent);
		SessionText.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		TimeText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		ProgressTrack.Fill = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
		ProgressFill.Fill = new SolidColorBrush(accent);

		ButtonPlayPause.CurrentTheme = CurrentTheme;
		ButtonReset.CurrentTheme = CurrentTheme;
		ButtonSkip.CurrentTheme = CurrentTheme;
		Render();
	}
}

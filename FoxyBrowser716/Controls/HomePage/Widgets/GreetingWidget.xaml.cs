using System.Globalization;
using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("Greeting Widget", MaterialIconKind.HandWave, WidgetCategory.General)]
public partial class GreetingWidget : WidgetBase
{
	private string _name = "";
	private int _subtitleMode = 1; // 0 = none, 1 = date, 2 = custom
	private string _customSubtitle = "";
	private bool _showBackground = true;

	protected GreetingWidget()
	{
		InitializeComponent();
		WidgetSettings =
		[
			new StringSetting("Name", "Shown after the greeting, e.g. \"Good morning, Alex\"", "", v => { _name = v; Render(); }),
			new ComboSetting("Subtitle", "", 1, v => { _subtitleMode = v; Render(); }, ("None", 0), ("Today's Date", 1), ("Custom Text", 2)),
			new StringSetting("Custom Subtitle", "Used when Subtitle is set to Custom Text", "", v => { _customSubtitle = v; Render(); }),
			new BoolSetting("Show Background", "Turn off to float the text over the wallpaper", true, v => { _showBackground = v; ApplyTheme(); }),
		];
	}

	protected override Task Initialize()
	{
		CreateLiveTimer(TimeSpan.FromMinutes(1), Render);
		ApplyTheme();
		return Task.CompletedTask;
	}

	private static string GreetingFor(int hour) => hour switch
	{
		< 5 => "Good night",
		< 12 => "Good morning",
		< 17 => "Good afternoon",
		< 22 => "Good evening",
		_ => "Good night",
	};

	private void Render()
	{
		var now = DateTime.Now;
		GreetingText.Text = string.IsNullOrWhiteSpace(_name) ? GreetingFor(now.Hour) : $"{GreetingFor(now.Hour)}, {_name.Trim()}";

		SubtitleText.Text = _subtitleMode switch
		{
			1 => now.ToString("D", CultureInfo.CurrentCulture),
			2 => _customSubtitle,
			_ => "",
		};
		SubtitleText.Visibility = string.IsNullOrWhiteSpace(SubtitleText.Text) ? Visibility.Collapsed : Visibility.Visible;
	}

	protected override void ApplyTheme()
	{
		if (_showBackground) ApplyCardTheme(Card);
		else
		{
			Card.Background = new SolidColorBrush(Colors.Transparent);
			Card.BorderBrush = new SolidColorBrush(Colors.Transparent);
		}
		GreetingText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		SubtitleText.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		Render();
	}
}

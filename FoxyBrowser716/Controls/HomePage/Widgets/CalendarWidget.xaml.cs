using System.Globalization;
using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("Calendar Widget", MaterialIconKind.CalendarMonth, WidgetCategory.TimeDate)]
public partial class CalendarWidget : WidgetBase
{
	private const int Columns = 7;
	private const int Rows = 6;

	private readonly TextBlock[] _weekdayLabels = new TextBlock[Columns];
	private readonly (Border cell, TextBlock text)[] _days = new (Border, TextBlock)[Columns * Rows];

	private DateTime _shownMonth = FirstOfMonth(DateTime.Today);
	private DateTime _lastToday = DateTime.Today;
	private DayOfWeek _firstDayOfWeek = DayOfWeek.Sunday;

	protected CalendarWidget()
	{
		InitializeComponent();
		BuildGrid();
		WidgetSettings =
		[
			new ComboSetting("Week Starts On", "", 0, v =>
			{
				_firstDayOfWeek = v == 1 ? DayOfWeek.Monday : DayOfWeek.Sunday;
				Render();
			}, ("Sunday", 0), ("Monday", 1)),
		];
	}

	protected override Task Initialize()
	{
		// roll over to the new day (and month) if the browser is left open past midnight
		CreateLiveTimer(TimeSpan.FromMinutes(1), () =>
		{
			if (DateTime.Today == _lastToday) return;
			if (_shownMonth == FirstOfMonth(_lastToday)) _shownMonth = FirstOfMonth(DateTime.Today);
			_lastToday = DateTime.Today;
			Render();
		});
		ApplyTheme(); // also renders
		return Task.CompletedTask;
	}

	private static DateTime FirstOfMonth(DateTime d) => new(d.Year, d.Month, 1);

	private void BuildGrid()
	{
		for (var c = 0; c < Columns; c++)
		{
			WeekdayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			DaysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			var label = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			Grid.SetColumn(label, c);
			WeekdayGrid.Children.Add(label);
			_weekdayLabels[c] = label;
		}

		for (var r = 0; r < Rows; r++)
			DaysGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

		for (var i = 0; i < _days.Length; i++)
		{
			var text = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			var cell = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Child = text };
			Grid.SetColumn(cell, i % Columns);
			Grid.SetRow(cell, i / Columns);
			DaysGrid.Children.Add(cell);
			_days[i] = (cell, text);
		}
	}

	private void Render()
	{
		var culture = CultureInfo.CurrentCulture;
		MonthText.Text = _shownMonth.ToString("MMMM yyyy", culture);

		for (var c = 0; c < Columns; c++)
			_weekdayLabels[c].Text = culture.DateTimeFormat.GetShortestDayName((DayOfWeek)(((int)_firstDayOfWeek + c) % 7));

		var leading = ((int)_shownMonth.DayOfWeek - (int)_firstDayOfWeek + 7) % 7;
		var start = _shownMonth.AddDays(-leading);
		var today = DateTime.Today;

		var primary = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		var dimmed = new SolidColorBrush(CurrentTheme.SecondaryForegroundColorSlightTransparent);
		var todayBackground = new SolidColorBrush(CurrentTheme.PrimaryHighlightColorSlightTransparent);
		var clear = new SolidColorBrush(Colors.Transparent);

		for (var i = 0; i < _days.Length; i++)
		{
			var date = start.AddDays(i);
			var (cell, text) = _days[i];
			text.Text = date.Day.ToString(culture);
			text.Foreground = date.Month == _shownMonth.Month ? primary : dimmed;
			text.FontWeight = date == today ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
			cell.Background = date == today ? todayBackground : clear;
		}
	}

	private void ButtonPrevious_OnClick(object sender, RoutedEventArgs e)
	{
		_shownMonth = _shownMonth.AddMonths(-1);
		Render();
	}

	private void ButtonNext_OnClick(object sender, RoutedEventArgs e)
	{
		_shownMonth = _shownMonth.AddMonths(1);
		Render();
	}

	private void MonthText_OnTapped(object sender, TappedRoutedEventArgs e)
	{
		_shownMonth = FirstOfMonth(DateTime.Today);
		Render();
	}

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);
		MonthText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		var weekday = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		foreach (var label in _weekdayLabels) label.Foreground = weekday;
		ButtonPrevious.CurrentTheme = CurrentTheme;
		ButtonNext.CurrentTheme = CurrentTheme;
		Render();
	}
}

using System.Globalization;
using FoxyBrowser716.DataObjects.Settings;
using Material.Icons.WinUI3;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("World Clock Widget", MaterialIconKind.WebClock, WidgetCategory.TimeDate)]
public partial class WorldClockWidget : WidgetBase
{
	private const double RowHeight = 50;
	private const double TimeFontSize = 24;
	private const double LabelAreaWidth = 150; // day/night icon + name + note; the clock column is sized separately
	private const double ColumnGap = 8;

	private static readonly char[] EntrySeparators = ['\n', '\r', ',', ';'];

	private sealed record ClockRow(TimeZoneInfo? Zone, Border Root, MaterialIcon Icon, TextBlock Label, TextBlock Note, TextBlock Time);

	private readonly List<ClockRow> _rows = [];
	private readonly FontFamily _clockFont = new("Consolas");

	private string _zonesText = "New York=America/New_York, London=Europe/London, Tokyo=Asia/Tokyo";
	private bool _showSeconds;

	protected WorldClockWidget()
	{
		InitializeComponent();
		WidgetSettings =
		[
			new StringSetting("Time Zones",
				"One per line or comma-separated, optionally named as Label=Zone. Zones are IANA ids (Europe/Paris) or Windows ids (Tokyo Standard Time)",
				_zonesText, v => { _zonesText = v ?? ""; RebuildRows(); }, multiline: true),
			new BoolSetting("Show Seconds", "", false, v => { _showSeconds = v; RebuildRows(); }),
		];
		RebuildRows();
	}

	protected override Task Initialize()
	{
		CreateLiveTimer(TimeSpan.FromMilliseconds(250), Tick);
		ApplyTheme();
		return Task.CompletedTask;
	}

	private static IEnumerable<(string label, string zone)> ParseEntries(string text)
	{
		foreach (var entry in text.Split(EntrySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var equals = entry.IndexOf('=');
			var zone = (equals >= 0 ? entry[(equals + 1)..] : entry).Trim();
			var label = equals >= 0 ? entry[..equals].Trim() : "";
			if (label.Length == 0) label = DefaultLabel(zone);
			if (label.Length == 0) continue; // "=" on its own
			yield return (label, zone);
		}
	}

	/// <summary>"America/New_York" → "New York"; Windows ids ("Tokyo Standard Time") are used as-is.</summary>
	private static string DefaultLabel(string zoneId) => zoneId[(zoneId.LastIndexOf('/') + 1)..].Replace('_', ' ');

	private static TimeZoneInfo? FindZone(string id)
	{
		if (id.Length == 0) return null;
		// .NET resolves IANA ids on Windows too (through ICU), alongside the Windows registry ids
		try { return TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : null; }
		catch { return null; }
	}

	private void RebuildRows()
	{
		RowsPanel.Children.Clear();
		_rows.Clear();

		var timeWidth = MeasureWidestTime();
		ContentGrid.Width = LabelAreaWidth + ColumnGap + timeWidth;

		var entries = ParseEntries(_zonesText).ToList();
		for (var i = 0; i < entries.Count; i++)
		{
			var (label, zoneId) = entries[i];
			var zone = FindZone(zoneId);

			var icon = new MaterialIcon
			{
				Kind = zone is null ? MaterialIconKind.HelpCircleOutline : MaterialIconKind.WeatherSunny,
				Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center,
			};
			var name = new TextBlock
			{
				Text = label, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
				TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
			};
			var note = new TextBlock
			{
				FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
				Text = zone is null ? "Unknown time zone" : "",
			};
			var time = new TextBlock
			{
				FontSize = TimeFontSize, FontFamily = _clockFont, MinWidth = timeWidth, TextAlignment = TextAlignment.Right,
				VerticalAlignment = VerticalAlignment.Center, Text = zone is null ? "--:--" : "",
			};
			ToolTipService.SetToolTip(name, zoneId);

			var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, note } };
			var grid = new Grid { ColumnSpacing = ColumnGap };
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			Grid.SetColumn(labels, 1);
			Grid.SetColumn(time, 2);
			grid.Children.Add(icon);
			grid.Children.Add(labels);
			grid.Children.Add(time);

			var root = new Border
			{
				Height = RowHeight, Child = grid,
				// hairline between rows, none under the last one
				BorderThickness = new Thickness(0, 0, 0, i < entries.Count - 1 ? 1 : 0),
			};

			var row = new ClockRow(zone, root, icon, name, note, time);
			_rows.Add(row);
			ColorRow(row);
			RowsPanel.Children.Add(root);
		}

		EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		Tick();
	}

	/// <summary>Width of the longest time the current culture prints, so the clock column never changes size while ticking.</summary>
	private double MeasureWidestTime()
	{
		var format = _showSeconds ? "T" : "t";
		var probe = new TextBlock { FontSize = TimeFontSize, FontFamily = _clockFont };
		double widest = 0, longest = 0;
		// two-digit hours in both halves of the day cover the longest text any 12h or 24h pattern produces
		foreach (var hour in new[] { 10, 12, 22, 23 })
		{
			probe.Text = new DateTime(2000, 1, 1, hour, 58, 58).ToString(format, CultureInfo.CurrentCulture);
			probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
			widest = Math.Max(widest, probe.DesiredSize.Width);
			longest = Math.Max(longest, probe.Text.Length);
		}
		// if measuring off-screen ever comes back empty, fall back to Consolas' fixed ~0.55em advance
		if (widest <= 0) widest = longest * TimeFontSize * 0.55;
		return Math.Ceiling(widest);
	}

	private void Tick()
	{
		var utcNow = DateTimeOffset.UtcNow;
		var today = DateTime.Today;
		var format = _showSeconds ? "T" : "t";
		foreach (var row in _rows)
		{
			if (row.Zone is not { } zone) continue;
			var zoneNow = TimeZoneInfo.ConvertTime(utcNow, zone);
			SetText(row.Time, zoneNow.ToString(format, CultureInfo.CurrentCulture));
			SetText(row.Note, $"{DayText((zoneNow.Date - today).Days)} · {OffsetText(zoneNow.Offset)}");
			var kind = zoneNow.Hour is >= 6 and < 18 ? MaterialIconKind.WeatherSunny : MaterialIconKind.WeatherNight;
			if (row.Icon.Kind != kind) row.Icon.Kind = kind;
		}
	}

	private static void SetText(TextBlock block, string text)
	{
		if (block.Text != text) block.Text = text;
	}

	private static string DayText(int days) => days switch
	{
		0 => "Today",
		1 => "+1 day",
		-1 => "-1 day",
		> 0 => $"+{days} days",
		_ => $"{days} days",
	};

	private static string OffsetText(TimeSpan offset)
	{
		if (offset == TimeSpan.Zero) return "UTC";
		var sign = offset < TimeSpan.Zero ? "-" : "+";
		var abs = offset.Duration();
		return abs.Minutes == 0 ? $"UTC{sign}{abs.Hours}" : $"UTC{sign}{abs.Hours}:{abs.Minutes:00}";
	}

	private void ColorRow(ClockRow row)
	{
		var known = row.Zone is not null;
		row.Root.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
		row.Icon.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		row.Label.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		row.Note.Foreground = new SolidColorBrush(known ? CurrentTheme.SecondaryForegroundColor : CurrentTheme.NoColor);
		row.Time.Foreground = new SolidColorBrush(known ? CurrentTheme.PrimaryForegroundColor : CurrentTheme.SecondaryForegroundColor);
	}

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);
		EmptyText.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		foreach (var row in _rows) ColorRow(row);
	}
}

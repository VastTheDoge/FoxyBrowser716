using System.Diagnostics;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("Browser Stats Widget", MaterialIconKind.ChartBox, WidgetCategory.FoxyBrowser716)]
public partial class BrowserStatsWidget : WidgetBase
{
	private static readonly DateTime StartedAt = Process.GetCurrentProcess().StartTime;

	private readonly List<(TextBlock label, TextBlock value)> _rows = [];
	private TextBlock _tabs = null!, _groups = null!, _bookmarks = null!, _processes = null!, _memory = null!, _uptime = null!;
	private bool _memoryQueryRunning;

	protected BrowserStatsWidget()
	{
		InitializeComponent();
		_tabs = AddRow("Open tabs");
		_groups = AddRow("Tab groups");
		_bookmarks = AddRow("Bookmarks");
		_processes = AddRow("Processes");
		_memory = AddRow("Memory");
		_uptime = AddRow("Uptime");
	}

	private TextBlock AddRow(string name)
	{
		var row = StatsGrid.RowDefinitions.Count;
		StatsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		var label = new TextBlock { Text = name, FontSize = 13 };
		var value = new TextBlock { Text = "-", FontSize = 13, FontFamily = new FontFamily("Consolas"), HorizontalAlignment = HorizontalAlignment.Right };
		Grid.SetRow(label, row);
		Grid.SetRow(value, row);
		Grid.SetColumn(value, 1);
		StatsGrid.Children.Add(label);
		StatsGrid.Children.Add(value);
		_rows.Add((label, value));
		return value;
	}

	protected override Task Initialize()
	{
		CreateLiveTimer(TimeSpan.FromSeconds(2), Refresh);
		ApplyTheme();
		return Task.CompletedTask;
	}

	private void Refresh()
	{
		_tabs.Text = TabManager.GetAllTabs().Count.ToString();
		_groups.Text = TabManager.Groups.Count.ToString();
		_bookmarks.Text = (TabManager.Instance.Bookmarks?.Count ?? 0).ToString();

		var uptime = DateTime.Now - StartedAt;
		_uptime.Text = uptime.TotalDays >= 1
			? $"{(int)uptime.TotalDays}d {uptime.Hours}h"
			: $"{uptime.Hours}h {uptime.Minutes:00}m";

		_ = RefreshMemory();
	}

	private async Task RefreshMemory()
	{
		if (_memoryQueryRunning) return;
		_memoryQueryRunning = true;
		try
		{
			// WebView2 objects are UI-thread bound, so collect the ids here and measure them off-thread
			var ids = new HashSet<int> { Environment.ProcessId };
			if (TabManager.WebsiteEnvironment is { } environment)
				foreach (var info in environment.GetProcessInfos())
					ids.Add(info.ProcessId);

			var bytes = await Task.Run(() => ids.Sum(id =>
			{
				try { using var p = Process.GetProcessById(id); return p.WorkingSet64; }
				catch { return 0L; } // process exited between listing and measuring
			}));

			_processes.Text = ids.Count.ToString();
			_memory.Text = bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):F2} GB" : $"{bytes >> 20} MB";
		}
		catch { /* environment torn down while the window closes */ }
		finally { _memoryQueryRunning = false; }
	}

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);
		HeaderText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);
		var label = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		var value = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		foreach (var row in _rows)
		{
			row.label.Foreground = label;
			row.value.Foreground = value;
		}
	}
}

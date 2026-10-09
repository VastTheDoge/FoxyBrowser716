using System.Runtime.InteropServices;
using FoxyBrowser716.DataObjects.Settings;
using Microsoft.UI.Xaml.Shapes;
using Windows.System.Power;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

// A mini Task Manager: CPU (with a one-minute graph), memory, battery and system-drive space.
[WidgetInfo("System Monitor Widget", MaterialIconKind.MonitorDashboard, WidgetCategory.Tools)]
public partial class SystemMonitorWidget : WidgetBase
{
	private const double DesignWidth = 220, GraphHeight = 48;
	private const int GraphSamples = 60;
	private const int DiskRefreshTicks = 15; // free space barely moves; no need to hit the disk every second
	private const double Gib = 1024d * 1024 * 1024;

	private enum Tone { Normal, Good, Bad }

	/// <summary>Label + value on one line, a thin usage bar under it.</summary>
	private sealed class MetricRow
	{
		public readonly StackPanel Panel;
		public readonly TextBlock Label, Value;
		public readonly Rectangle Track, Fill;
		public Tone Tone;

		public MetricRow(string name)
		{
			Label = new TextBlock { Text = name, FontSize = 13 };
			Value = new TextBlock { Text = "--", FontSize = 13, FontFamily = new FontFamily("Consolas"), HorizontalAlignment = HorizontalAlignment.Right };
			Track = new Rectangle();
			Fill = new Rectangle { HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
			Panel = new StackPanel
			{
				Spacing = 3,
				Children =
				{
					new Grid { Children = { Label, Value } },
					new Grid { Height = 4, CornerRadius = new CornerRadius(2), Children = { Track, Fill } },
				},
			};
		}
	}

	private readonly List<MetricRow> _rows = [];
	private readonly MetricRow _cpu, _memory, _battery, _disk;
	private readonly List<Line> _gridLines = [];
	private readonly Queue<double> _cpuHistory = new(); // 0..1
	private readonly string _systemDrive = Directory.GetDirectoryRoot(Environment.SystemDirectory);

	private bool _showGraph = true, _showBattery = true, _showDisk = true;
	private bool _hasBattery;

	private bool _hasCpuBaseline;
	private long _lastIdle, _lastKernel, _lastUser, _lastSampleAt;

	private int _ticksUntilDisk;
	private bool _diskQueryRunning;

	protected SystemMonitorWidget()
	{
		InitializeComponent();
		// the graph belongs to the CPU row, so CPU goes above it and everything else below
		_cpu = AddRow("CPU", aboveGraph: true);
		_memory = AddRow("Memory");
		_battery = AddRow("Battery");
		_disk = AddRow($"Disk {_systemDrive.TrimEnd('\\')}");
		_battery.Panel.Visibility = Visibility.Collapsed; // until a tick finds a battery
		BuildGraphGrid();

		WidgetSettings =
		[
			new BoolSetting("Show Graph", "CPU usage over the last minute", true, v =>
			{
				_showGraph = v;
				GraphHost.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
				if (v) RenderGraph();
			}),
			new BoolSetting("Show Battery", "Always hidden on PCs without a battery", true, v =>
			{
				_showBattery = v;
				UpdateBatteryVisibility();
			}),
			new BoolSetting("Show Disk", "Free space on the system drive", true, v =>
			{
				_showDisk = v;
				_disk.Panel.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
				if (v) _ticksUntilDisk = 0;
			}),
		];
	}

	private MetricRow AddRow(string name, bool aboveGraph = false)
	{
		var row = new MetricRow(name);
		_rows.Add(row);
		if (aboveGraph) MetricsPanel.Children.Insert(MetricsPanel.Children.IndexOf(GraphHost), row.Panel);
		else MetricsPanel.Children.Add(row.Panel);
		return row;
	}

	private void BuildGraphGrid()
	{
		for (var i = 1; i < 4; i++)
		{
			var y = GraphHeight * i / 4;
			var line = new Line { X1 = 0, X2 = DesignWidth, Y1 = y, Y2 = y, StrokeThickness = 1 };
			_gridLines.Add(line);
			GraphGrid.Children.Add(line);
		}
	}

	protected override Task Initialize()
	{
		// re-baseline after being off screen, or the first sample would average over the whole gap
		Unloaded += (_, _) => _hasCpuBaseline = false;
		CreateLiveTimer(TimeSpan.FromSeconds(1), Tick);
		ApplyTheme();
		return Task.CompletedTask;
	}

	private void Tick()
	{
		SampleCpu();
		SampleMemory();
		SampleBattery();
		if (_showDisk && --_ticksUntilDisk <= 0)
		{
			_ticksUntilDisk = DiskRefreshTicks;
			_ = SampleDiskAsync();
		}
	}

	#region Sampling

	private void SampleCpu()
	{
		var now = Environment.TickCount64;
		// Loaded can tick right after another tick; a few-ms window gives a meaningless reading
		if (_hasCpuBaseline && now - _lastSampleAt < 250) return;
		if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime)) return;

		long idle = idleTime.Ticks, kernel = kernelTime.Ticks, user = userTime.Ticks;
		var hadBaseline = _hasCpuBaseline;
		var idleDelta = idle - _lastIdle;
		var busyAndIdleDelta = (kernel - _lastKernel) + (user - _lastUser); // kernel time includes idle time
		(_lastIdle, _lastKernel, _lastUser, _lastSampleAt, _hasCpuBaseline) = (idle, kernel, user, now, true);
		if (!hadBaseline || busyAndIdleDelta <= 0 || idleDelta < 0) return;

		var usage = Math.Clamp((double)(busyAndIdleDelta - idleDelta) / busyAndIdleDelta, 0, 1);
		_cpu.Value.Text = $"{usage * 100:0}%";
		SetBar(_cpu, usage, usage >= 0.9 ? Tone.Bad : Tone.Normal);

		_cpuHistory.Enqueue(usage);
		while (_cpuHistory.Count > GraphSamples) _cpuHistory.Dequeue();
		if (_showGraph) RenderGraph();
	}

	private void SampleMemory()
	{
		var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
		if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0) return;

		double total = status.TotalPhys;
		var used = total - status.AvailPhys;
		var fraction = used / total;
		_memory.Value.Text = $"{used / Gib:0.0}/{total / Gib:0.0} GB {fraction * 100,3:0}%";
		SetBar(_memory, fraction, fraction >= 0.9 ? Tone.Bad : Tone.Normal);
	}

	private void SampleBattery()
	{
		if (!_showBattery) return; // the setting already hides the row

		BatteryStatus status;
		PowerSupplyStatus supply;
		int percent;
		try
		{
			status = PowerManager.BatteryStatus;
			supply = PowerManager.PowerSupplyStatus;
			percent = PowerManager.RemainingChargePercent;
		}
		catch
		{
			// no power service (e.g. under Wine): treat as a desktop
			status = BatteryStatus.NotPresent;
			supply = PowerSupplyStatus.NotPresent;
			percent = 0;
		}

		_hasBattery = status != BatteryStatus.NotPresent;
		UpdateBatteryVisibility();
		if (!_hasBattery) return;

		var charging = status == BatteryStatus.Charging;
		var pluggedIn = supply != PowerSupplyStatus.NotPresent;
		_battery.Value.Text = charging ? $"{percent}% charging" : pluggedIn ? $"{percent}% plugged in" : $"{percent}%";
		SetBar(_battery, percent / 100.0, charging ? Tone.Good : !pluggedIn && percent <= 20 ? Tone.Bad : Tone.Normal);
	}

	private void UpdateBatteryVisibility() =>
		_battery.Panel.Visibility = _showBattery && _hasBattery ? Visibility.Visible : Visibility.Collapsed;

	private async Task SampleDiskAsync()
	{
		if (_diskQueryRunning) return;
		_diskQueryRunning = true;
		try
		{
			// real disk I/O, so keep it off the UI thread
			var drive = _systemDrive;
			var (total, free) = await Task.Run(() =>
			{
				var info = new DriveInfo(drive);
				return (info.TotalSize, info.AvailableFreeSpace);
			});
			if (total <= 0) return;

			var freeFraction = (double)free / total;
			_disk.Value.Text = $"{FormatBytes(free)} free";
			SetBar(_disk, 1 - freeFraction, freeFraction < 0.1 ? Tone.Bad : Tone.Normal);
		}
		catch { /* drive not ready */ }
		finally { _diskQueryRunning = false; }
	}

	#endregion

	#region Rendering

	private void SetBar(MetricRow row, double fraction, Tone tone)
	{
		row.Fill.Width = double.IsNaN(fraction) ? 0 : DesignWidth * Math.Clamp(fraction, 0, 1);
		if (row.Tone == tone && row.Fill.Fill is not null) return; // only allocate a brush when the state changes
		row.Tone = tone;
		row.Fill.Fill = new SolidColorBrush(ToneColor(tone));
	}

	private Color ToneColor(Tone tone) => tone switch
	{
		Tone.Good => CurrentTheme.YesColor,
		Tone.Bad => CurrentTheme.NoColor,
		_ => CurrentTheme.PrimaryHighlightColor,
	};

	private void RenderGraph()
	{
		// fresh collections each time: WinUI doesn't reliably redraw a shape whose Points were edited in place
		var line = new PointCollection();
		var fill = new PointCollection();
		var step = DesignWidth / (GraphSamples - 1);
		var firstX = DesignWidth - (_cpuHistory.Count - 1) * step; // right-aligned: new samples enter from the right
		var x = firstX;
		foreach (var sample in _cpuHistory)
		{
			var point = new Point(x, 1 + (GraphHeight - 2) * (1 - sample));
			line.Add(point);
			fill.Add(point);
			x += step;
		}
		if (_cpuHistory.Count > 1)
		{
			fill.Add(new Point(DesignWidth, GraphHeight));
			fill.Add(new Point(firstX, GraphHeight));
		}
		GraphLine.Points = line;
		GraphFill.Points = fill;
	}

	private static string FormatBytes(double bytes) => bytes >= 1024 * Gib
		? $"{bytes / (1024 * Gib):0.0} TB"
		: $"{bytes / Gib:0} GB";

	private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

	#endregion

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);

		var label = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		var value = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		var track = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
		foreach (var row in _rows)
		{
			row.Label.Foreground = label;
			row.Value.Foreground = value;
			row.Track.Fill = track;
			row.Fill.Fill = new SolidColorBrush(ToneColor(row.Tone));
		}

		GraphHost.Background = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorVeryTransparent);
		var gridBrush = new SolidColorBrush(WithAlpha(CurrentTheme.SecondaryForegroundColor, 40));
		_gridLines.ForEach(l => l.Stroke = gridBrush);
		GraphLine.Stroke = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);
		GraphFill.Fill = new SolidColorBrush(WithAlpha(CurrentTheme.PrimaryHighlightColor, 60));
	}

	#region Native

	// the fields are written by kernel32, which the compiler can't see
#pragma warning disable CS0649
	[StructLayout(LayoutKind.Sequential)]
	private struct FileTime
	{
		public uint Low;
		public uint High;
		public readonly long Ticks => (long)(((ulong)High << 32) | Low);
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MemoryStatusEx
	{
		public uint Length;
		public uint MemoryLoad;
		public ulong TotalPhys;
		public ulong AvailPhys;
		public ulong TotalPageFile;
		public ulong AvailPageFile;
		public ulong TotalVirtual;
		public ulong AvailVirtual;
		public ulong AvailExtendedVirtual;
	}
#pragma warning restore CS0649

	[DllImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

	[DllImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

	#endregion
}

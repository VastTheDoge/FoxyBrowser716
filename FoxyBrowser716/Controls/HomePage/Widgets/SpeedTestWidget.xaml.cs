using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using FoxyBrowser716.DataObjects.Settings;
using Material.Icons.WinUI3;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

// Two halves: live throughput of this PC's internet-facing adapters (always on, no network traffic of its own),
// and an on-demand ping/download/upload test against Cloudflare's public speed test endpoints.
[WidgetInfo("Speed Test Widget", MaterialIconKind.WifiArrowUpDown, WidgetCategory.Tools)]
public partial class SpeedTestWidget : WidgetBase
{
	private const int HistoryLength = 60;
	private const double GraphWidth = 240, GraphHeight = 56;

	private const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=";
	private const string UploadUrl = "https://speed.cloudflare.com/__up";
	private const int Streams = 4;
	private static readonly TimeSpan DownloadDuration = TimeSpan.FromSeconds(8);
	private static readonly TimeSpan UploadDuration = TimeSpan.FromSeconds(6);
	// stop early on fast links; this is already plenty for a stable reading and caps the data a test costs
	private const long DownloadCap = 200_000_000;
	private const long UploadCap = 50_000_000;

	private static readonly HttpClient Http = CreateClient();
	private static readonly Lazy<byte[]> UploadPayload = new(() =>
	{
		// random so nothing along the way can compress it
		var bytes = new byte[4_000_000];
		Random.Shared.NextBytes(bytes);
		return bytes;
	});

	private readonly Queue<double> _downHistory = new(), _upHistory = new(); // bytes per second
	private Dictionary<string, (long rx, long tx)> _lastCounters = [];
	private readonly Stopwatch _sampleClock = new();
	private bool _sampling;

	private bool _bits = true;
	private CancellationTokenSource? _testCts;
	private double? _testDown, _testUp, _testPing; // bytes/s, bytes/s, ms

	protected SpeedTestWidget()
	{
		InitializeComponent();
		WidgetSettings =
		[
			new ComboSetting("Units", "", 0, v => { _bits = v == 0; RenderLive(); RenderTest(); },
				("Megabits (Mbps)", 0), ("Megabytes (MB/s)", 1)),
			new BoolSetting("Show Live Usage", "This PC's current download/upload, with a one-minute graph", true,
				v => LivePanel.Visibility = v ? Visibility.Visible : Visibility.Collapsed),
			new BoolSetting("Show Speed Test", "Runs against speed.cloudflare.com when you press the speedometer (uses up to ~250 MB on fast connections)", true, v =>
			{
				TestPanel.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
				ButtonTest.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
			}),
		];
	}

	protected override Task Initialize()
	{
		// registered before the timer so a widget coming back on screen starts from a fresh baseline
		// instead of averaging over the time it was hidden
		Loaded += (_, _) => _lastCounters = [];
		Unloaded += (_, _) => _testCts?.Cancel();
		_sampleClock.Start();
		CreateLiveTimer(TimeSpan.FromSeconds(1), SampleThroughput);
		ApplyTheme();
		return Task.CompletedTask;
	}

	private static HttpClient CreateClient()
	{
		var client = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd("FoxyBrowser716-SpeedTest");
		return client;
	}

	#region live usage
	private async void SampleThroughput()
	{
		if (_sampling) return;
		_sampling = true;
		try
		{
			var counters = await Task.Run(ReadCounters);
			var seconds = _sampleClock.Elapsed.TotalSeconds;
			_sampleClock.Restart();

			if (_lastCounters.Count > 0 && seconds > 0)
			{
				long rx = 0, tx = 0;
				foreach (var (id, now) in counters)
				{
					if (!_lastCounters.TryGetValue(id, out var before)) continue;
					// counters restart when an adapter reconnects; skip that sample instead of going negative
					rx += Math.Max(0, now.rx - before.rx);
					tx += Math.Max(0, now.tx - before.tx);
				}
				Push(_downHistory, rx / seconds);
				Push(_upHistory, tx / seconds);
				RenderLive();
			}
			_lastCounters = counters;
		}
		catch { /* adapters can vanish mid-read; the next tick tries again */ }
		finally { _sampling = false; }
	}

	private static Dictionary<string, (long rx, long tx)> ReadCounters()
	{
		var result = new Dictionary<string, (long rx, long tx)>();
		foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
		{
			if (nic.OperationalStatus != OperationalStatus.Up
			    || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
				continue;
			try
			{
				// only adapters with a gateway lead to the internet; this skips Hyper-V/WSL virtual switches,
				// which would otherwise count the same traffic twice
				if (nic.GetIPProperties().GatewayAddresses.Count == 0) continue;
				var stats = nic.GetIPStatistics();
				result[nic.Id] = (stats.BytesReceived, stats.BytesSent);
			}
			catch { /* adapter went away */ }
		}
		return result;
	}

	private static void Push(Queue<double> history, double value)
	{
		history.Enqueue(value);
		while (history.Count > HistoryLength) history.Dequeue();
	}

	private void RenderLive()
	{
		if (_downHistory.Count == 0) return;
		LiveDownText.Text = FormatRate(_downHistory.Last());
		LiveUpText.Text = FormatRate(_upHistory.Last());

		// scale to the busiest moment on screen, but never below 1 Mbps so an idle connection reads as flat
		var max = Math.Max(125_000, Math.Max(_downHistory.Max(), _upHistory.Max()));
		DownLine.Points = ToPoints(_downHistory, max);
		UpLine.Points = ToPoints(_upHistory, max);
		ScaleText.Text = FormatRate(max);
	}

	private static PointCollection ToPoints(Queue<double> samples, double max)
	{
		var points = new PointCollection();
		var x = HistoryLength - samples.Count; // right-aligned: new samples enter from the right edge
		foreach (var value in samples)
			points.Add(new Point(x++ * GraphWidth / (HistoryLength - 1), GraphHeight - 2 - value / max * (GraphHeight - 6)));
		return points;
	}
	#endregion

	#region speed test
	private async void ButtonTest_OnClick(object sender, RoutedEventArgs e)
	{
		if (_testCts is not null)
		{
			_testCts.Cancel();
			return;
		}

		var cts = _testCts = new CancellationTokenSource();
		SetTestButton(running: true);
		_testDown = _testUp = _testPing = null;
		RenderTest();
		try
		{
			StatusText.Text = "Measuring ping...";
			_testPing = await MeasurePing(cts.Token);
			RenderTest();

			_testDown = await MeasureThroughput(download: true, cts.Token, live => StatusText.Text = $"Testing download... {FormatRate(live)}");
			RenderTest();

			_testUp = await MeasureThroughput(download: false, cts.Token, live => StatusText.Text = $"Testing upload... {FormatRate(live)}");
			RenderTest();

			StatusText.Text = $"Tested at {DateTime.Now:t} via speed.cloudflare.com";
		}
		catch (OperationCanceledException) when (cts.IsCancellationRequested)
		{
			StatusText.Text = "Speed test cancelled";
		}
		catch (Exception)
		{
			StatusText.Text = "Speed test failed - check your connection";
		}
		finally
		{
			cts.Dispose();
			_testCts = null;
			SetTestButton(running: false);
		}
	}

	private static async Task<double> MeasurePing(CancellationToken token)
	{
		var samples = new List<double>();
		for (var i = 0; i < 6; i++)
		{
			var clock = Stopwatch.StartNew();
			using var response = await Http.GetAsync(DownloadUrl + "0", HttpCompletionOption.ResponseHeadersRead, token);
			clock.Stop();
			response.EnsureSuccessStatusCode();
			// the first request also pays for DNS + TCP + TLS setup, which isn't latency
			if (i > 0) samples.Add(clock.Elapsed.TotalMilliseconds);
		}
		samples.Sort();
		return samples[samples.Count / 2];
	}

	/// <summary>Runs <see cref="Streams"/> parallel transfers for a fixed time and returns bytes/second.</summary>
	private static async Task<double> MeasureThroughput(bool download, CancellationToken token, Action<double> onProgress)
	{
		var cap = download ? DownloadCap : UploadCap;
		using var phase = CancellationTokenSource.CreateLinkedTokenSource(token);
		phase.CancelAfter(download ? DownloadDuration : UploadDuration);

		var counter = new long[1];
		void Count(int bytes) => Interlocked.Add(ref counter[0], bytes);

		var workers = Enumerable.Range(0, Streams).Select(_ => Task.Run(async () =>
		{
			try
			{
				while (!phase.IsCancellationRequested && Interlocked.Read(ref counter[0]) < cap)
				{
					if (download) await DownloadOnce(Count, phase.Token);
					else await UploadOnce(Count, phase.Token);
				}
			}
			catch (OperationCanceledException) { /* the phase ended mid-transfer, which is the normal way out */ }
		})).ToArray();

		var clock = Stopwatch.StartNew();
		var samples = new List<(double seconds, long bytes)>();
		while (!workers.All(w => w.IsCompleted))
		{
			await Task.Delay(250, CancellationToken.None);
			var bytes = Interlocked.Read(ref counter[0]);
			samples.Add((clock.Elapsed.TotalSeconds, bytes));
			if (samples.Count >= 5)
			{
				var (t0, b0) = samples[^5]; // ~1s sliding window for the live readout
				onProgress((bytes - b0) / Math.Max(0.001, clock.Elapsed.TotalSeconds - t0));
			}
			if (bytes >= cap) phase.Cancel();
		}
		token.ThrowIfCancellationRequested();

		var total = Interlocked.Read(ref counter[0]);
		var failed = workers.FirstOrDefault(w => w.IsFaulted);
		foreach (var faulted in workers.Where(x => x.IsFaulted)) _ = faulted.Exception; // observe every failure
		if (total == 0 && failed?.Exception?.InnerException is { } error) throw error;

		// ignore the first ~30%: TCP slow-start makes the ramp-up unrepresentative of the line's speed
		var elapsed = clock.Elapsed.TotalSeconds;
		var start = samples.FirstOrDefault(s => s.seconds >= elapsed * 0.3);
		return start.seconds > 0 && elapsed - start.seconds >= 0.5
			? (total - start.bytes) / (elapsed - start.seconds)
			: total / Math.Max(0.001, elapsed);
	}

	private static async Task DownloadOnce(Action<int> onBytes, CancellationToken token)
	{
		using var response = await Http.GetAsync(DownloadUrl + 25_000_000, HttpCompletionOption.ResponseHeadersRead, token);
		response.EnsureSuccessStatusCode();
		await using var stream = await response.Content.ReadAsStreamAsync(token);
		var buffer = new byte[81920];
		int read;
		while ((read = await stream.ReadAsync(buffer, token)) > 0) onBytes(read);
	}

	private static async Task UploadOnce(Action<int> onBytes, CancellationToken token)
	{
		using var content = new CountingContent(UploadPayload.Value, onBytes);
		using var response = await Http.PostAsync(UploadUrl, content, token);
		response.EnsureSuccessStatusCode();
	}

	/// <summary>Request body that reports bytes as they're written, so upload speed can be sampled mid-request.</summary>
	private sealed class CountingContent(byte[] payload, Action<int> onBytes) : HttpContent
	{
		private const int ChunkSize = 65536;

		protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
			SerializeToStreamAsync(stream, context, CancellationToken.None);

		protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
		{
			for (var offset = 0; offset < payload.Length; offset += ChunkSize)
			{
				var count = Math.Min(ChunkSize, payload.Length - offset);
				await stream.WriteAsync(payload.AsMemory(offset, count), cancellationToken);
				onBytes(count);
			}
		}

		protected override bool TryComputeLength(out long length)
		{
			length = payload.Length;
			return true;
		}
	}

	private void SetTestButton(bool running) =>
		ButtonTest.Content = new MaterialIcon { Kind = running ? MaterialIconKind.Stop : MaterialIconKind.Speedometer };

	private void RenderTest()
	{
		TestDownText.Text = _testDown is { } down ? FormatNumber(ToMegaUnits(down)) : "--";
		TestUpText.Text = _testUp is { } up ? FormatNumber(ToMegaUnits(up)) : "--";
		TestPingText.Text = _testPing is { } ping ? ping.ToString("F0") : "--";
		var unit = _bits ? "Mbps" : "MB/s";
		TestDownLabel.Text = $"Download ({unit})";
		TestUpLabel.Text = $"Upload ({unit})";
	}
	#endregion

	private double ToMegaUnits(double bytesPerSecond) => (_bits ? bytesPerSecond * 8 : bytesPerSecond) / 1_000_000;

	private string FormatRate(double bytesPerSecond)
	{
		var value = _bits ? bytesPerSecond * 8 : bytesPerSecond;
		var units = _bits ? new[] { "bps", "Kbps", "Mbps", "Gbps" } : new[] { "B/s", "KB/s", "MB/s", "GB/s" };
		var unit = 0;
		while (value >= 999.5 && unit < units.Length - 1)
		{
			value /= 1000;
			unit++;
		}
		return $"{FormatNumber(value)} {units[unit]}";
	}

	// three significant digits keeps every reading about the same width: 1.23 / 12.3 / 123
	private static string FormatNumber(double value) =>
		value >= 99.95 ? value.ToString("F0") : value >= 9.995 ? value.ToString("F1") : value.ToString("F2");

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);
		var primary = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		var secondary = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		var down = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);
		var up = new SolidColorBrush(CurrentTheme.YesColor);

		HeaderText.Foreground = primary;
		LiveDownText.Foreground = primary;
		LiveUpText.Foreground = primary;
		DownIcon.Foreground = down;
		UpIcon.Foreground = up;
		DownLine.Stroke = down;
		UpLine.Stroke = up;
		GraphHost.Background = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorVeryTransparent);
		ScaleText.Foreground = secondary;

		foreach (var t in new[] { TestDownText, TestUpText, TestPingText }) t.Foreground = primary;
		foreach (var t in new[] { TestDownLabel, TestUpLabel, TestPingLabel, StatusText }) t.Foreground = secondary;
		ButtonTest.CurrentTheme = CurrentTheme;
		RenderTest();
	}
}

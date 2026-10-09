using System.Globalization;
using System.Net.Http;
using System.Threading;
using FoxyBrowser716.DataObjects.Settings;
using FoxyBrowser716.ErrorHandeler;
using Material.Icons.WinUI3;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("Weather Widget", MaterialIconKind.WeatherPartlyCloudy, WidgetCategory.General)]
public partial class WeatherWidget : WidgetBase
{
	private const int ForecastDays = 3;
	// the timer only checks; a fetch happens once the data is this old (or the last attempt failed)
	private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
	private static readonly TimeSpan MaxDataAge = TimeSpan.FromMinutes(29);
	private static readonly string[] QualifierFields = ["country", "country_code", "admin1", "admin2"];

	// Open-Meteo needs no API key; HttpClient is meant to be shared, so one per widget type
	private static readonly HttpClient Http = CreateHttpClient();

	private sealed record Place(string Name, double Latitude, double Longitude);
	private sealed record DayForecast(DateTime Date, int? Code, double? High, double? Low);
	private sealed record WeatherData(string Location, bool Fahrenheit, double? Temperature, double? FeelsLike, double? Wind, int? Code, bool IsDay, List<DayForecast> Days);

	private readonly TextBlock[] _dayNames = new TextBlock[ForecastDays];
	private readonly MaterialIcon[] _dayIcons = new MaterialIcon[ForecastDays];
	private readonly TextBlock[] _dayHighs = new TextBlock[ForecastDays];
	private readonly TextBlock[] _dayLows = new TextBlock[ForecastDays];

	private string _city = "";
	private bool _fahrenheit = DefaultsToFahrenheit();
	private bool _initialized;

	private CancellationTokenSource? _requestCts; // the in-flight request; every new request cancels the previous one
	private WeatherData? _data;                   // what's on screen, always for _dataKey
	private string? _dataKey;
	private DateTime _fetchedAt;
	private bool _refreshFailed;                  // _data is still shown, but the last refresh of it failed
	private string? _error;                       // shown when there's no data for the current city

	protected WeatherWidget()
	{
		InitializeComponent();
		BuildForecastColumns();
		WidgetSettings =
		[
			new StringSetting("City", "e.g. London, or Springfield, Illinois", "", v => { _city = v ?? ""; OnLocationChanged(debounce: true); }),
			new ComboSetting("Units", "", _fahrenheit ? 1 : 0, v => { _fahrenheit = v == 1; OnLocationChanged(debounce: false); },
				("Celsius", 0), ("Fahrenheit", 1)),
			new BoolSetting("Show Forecast", "", true, v => ForecastPanel.Visibility = v ? Visibility.Visible : Visibility.Collapsed),
		];
	}

	protected override Task Initialize()
	{
		_initialized = true;
		// ticks on every Loaded too, which makes the first fetch; fresh data isn't refetched when the page comes back
		CreateLiveTimer(CheckInterval, OnTimerTick);
		ApplyTheme();
		return Task.CompletedTask;
	}

	private static bool DefaultsToFahrenheit()
	{
		try { return !RegionInfo.CurrentRegion.IsMetric; }
		catch { return false; }
	}

	private static HttpClient CreateHttpClient()
	{
		var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
		client.DefaultRequestHeaders.UserAgent.TryParseAdd("FoxyBrowser716/1.0 (home page weather widget)");
		return client;
	}

	private void BuildForecastColumns()
	{
		for (var i = 0; i < ForecastDays; i++)
		{
			ForecastGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			_dayNames[i] = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Text = "--" };
			_dayIcons[i] = new MaterialIcon { Kind = MaterialIconKind.WeatherCloudy, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center };
			_dayHighs[i] = new TextBlock { FontSize = 12, Text = "--°" };
			_dayLows[i] = new TextBlock { FontSize = 12, Text = "--°" };

			var column = new StackPanel
			{
				Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center,
				Children =
				{
					_dayNames[i],
					_dayIcons[i],
					new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, Children = { _dayHighs[i], _dayLows[i] } },
				},
			};
			Grid.SetColumn(column, i);
			ForecastGrid.Children.Add(column);
		}
	}

	private string CleanCity() => _city.ReplaceLineEndings(" ").Trim();

	private static string KeyFor(string city, bool fahrenheit) => $"{city.ToLowerInvariant()}|{(fahrenheit ? "F" : "C")}";

	private void OnLocationChanged(bool debounce)
	{
		// callbacks also fire while saved settings load, before Initialize; the first fetch waits for Loaded
		if (!_initialized) return;
		_ = RefreshAsync(debounce);
	}

	private void OnTimerTick()
	{
		var fresh = _dataKey == KeyFor(CleanCity(), _fahrenheit) && !_refreshFailed && DateTime.Now - _fetchedAt < MaxDataAge;
		if (fresh || _requestCts is not null) return;
		_ = RefreshAsync(debounce: false);
	}

	private async Task RefreshAsync(bool debounce)
	{
		_requestCts?.Cancel();
		_requestCts = null;

		var city = CleanCity();
		var fahrenheit = _fahrenheit;
		var key = KeyFor(city, fahrenheit);
		_error = null;
		if (key != _dataKey)
		{
			// never show one city's weather under another city's settings, even briefly
			_data = null;
			_dataKey = null;
			_refreshFailed = false;
		}
		if (city.Length == 0)
		{
			Render();
			return;
		}

		var cts = new CancellationTokenSource();
		_requestCts = cts;
		Render();

		WeatherData? data = null;
		string? error = null;
		try
		{
			// the City box reports every keystroke; let typing settle before calling the API
			if (debounce) await Task.Delay(TimeSpan.FromMilliseconds(700), cts.Token);
			data = await FetchAsync(city, fahrenheit, cts.Token);
			if (data is null) error = $"Couldn't find \"{city}\"";
		}
		catch (OperationCanceledException) when (cts.IsCancellationRequested)
		{
			return; // replaced by a newer request
		}
		catch (Exception e)
		{
			error = "Couldn't load the weather";
			FoxyLogger.AddWarning("Weather widget couldn't load the forecast", e.Message);
		}

		if (!ReferenceEquals(_requestCts, cts)) return;
		_requestCts = null;
		if (data is not null)
		{
			_data = data;
			_dataKey = key;
			_fetchedAt = DateTime.Now;
			_refreshFailed = false;
		}
		else if (_dataKey == key) _refreshFailed = true; // keep showing the older forecast for this city
		else _error = error;
		Render();
	}

	#region Open-Meteo

	private static async Task<WeatherData?> FetchAsync(string query, bool fahrenheit, CancellationToken token)
	{
		var place = await GeocodeAsync(query, token).ConfigureAwait(false);
		if (place is null) return null;

		// coordinates must use '.' whatever the user's culture is
		var url = FormattableString.Invariant(
			$"https://api.open-meteo.com/v1/forecast?latitude={place.Latitude}&longitude={place.Longitude}&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days={ForecastDays + 1}");
		if (fahrenheit) url += "&temperature_unit=fahrenheit&wind_speed_unit=mph";

		using var doc = await GetJsonAsync(url, token).ConfigureAwait(false);
		var root = doc.RootElement;
		var current = root.GetProperty("current");

		var days = new List<DayForecast>();
		if (root.TryGetProperty("daily", out var daily) && ArrayProperty(daily, "time") is { ValueKind: JsonValueKind.Array } times)
		{
			var codes = ArrayProperty(daily, "weather_code");
			var highs = ArrayProperty(daily, "temperature_2m_max");
			var lows = ArrayProperty(daily, "temperature_2m_min");
			for (var i = 0; i < times.GetArrayLength(); i++)
			{
				if (times[i].ValueKind != JsonValueKind.String
				    || !DateTime.TryParseExact(times[i].GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
					continue;
				days.Add(new DayForecast(date, (int?)NumberAt(codes, i), NumberAt(highs, i), NumberAt(lows, i)));
			}
		}

		return new WeatherData(
			place.Name, fahrenheit,
			Number(current, "temperature_2m"), Number(current, "apparent_temperature"), Number(current, "wind_speed_10m"),
			(int?)Number(current, "weather_code"), Number(current, "is_day") != 0, days);
	}

	private static async Task<Place?> GeocodeAsync(string query, CancellationToken token)
	{
		// the API only matches place names, so "Paris, France" searches "Paris" and uses "France" to pick a result
		var comma = query.IndexOf(',');
		var name = comma > 0 ? query[..comma].Trim() : query;
		var qualifier = comma > 0 ? query[(comma + 1)..].Trim() : "";
		var count = qualifier.Length > 0 ? 10 : 1;

		using var doc = await GetJsonAsync(
			$"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name)}&count={count}&format=json", token).ConfigureAwait(false);
		if (ArrayProperty(doc.RootElement, "results") is not { ValueKind: JsonValueKind.Array } results || results.GetArrayLength() == 0)
			return null;

		var candidates = results.EnumerateArray().ToList();
		var best = candidates.FirstOrDefault(c => MatchesQualifier(c, qualifier), candidates[0]);
		if (Number(best, "latitude") is not { } latitude || Number(best, "longitude") is not { } longitude) return null;
		return new Place(DescribePlace(best) is { Length: > 0 } described ? described : query, latitude, longitude);
	}

	private static bool MatchesQualifier(JsonElement place, string qualifier) =>
		qualifier.Length == 0
		|| QualifierFields.Any(field => Text(place, field) is { } value && value.StartsWith(qualifier, StringComparison.OrdinalIgnoreCase));

	private static string DescribePlace(JsonElement place)
	{
		var name = Text(place, "name") ?? "";
		var region = Text(place, "admin1");
		var country = Text(place, "country_code");
		var parts = new List<string>();
		if (name.Length > 0) parts.Add(name);
		if (!string.IsNullOrWhiteSpace(region) && !region.Equals(name, StringComparison.OrdinalIgnoreCase)) parts.Add(region);
		if (!string.IsNullOrWhiteSpace(country)) parts.Add(country.ToUpperInvariant());
		return string.Join(", ", parts);
	}

	private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken token)
	{
		using var response = await Http.GetAsync(url, token).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();
		using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
		return await JsonDocument.ParseAsync(stream, default, token).ConfigureAwait(false);
	}

	private static string? Text(JsonElement obj, string name) =>
		obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	private static double? Number(JsonElement obj, string name) =>
		obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

	private static JsonElement ArrayProperty(JsonElement obj, string name) =>
		obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value : default;

	// daily arrays can contain nulls, and a missing array comes through as default (Undefined)
	private static double? NumberAt(JsonElement array, int index) =>
		array.ValueKind == JsonValueKind.Array && index < array.GetArrayLength() && array[index].ValueKind == JsonValueKind.Number ? array[index].GetDouble() : null;

	/// <summary>Short condition text and icon for a WMO weather code (what Open-Meteo reports).</summary>
	private static (string text, MaterialIconKind icon) Describe(int? code, bool isDay) => code switch
	{
		0 => ("Clear", isDay ? MaterialIconKind.WeatherSunny : MaterialIconKind.WeatherNight),
		1 => ("Mostly clear", isDay ? MaterialIconKind.WeatherPartlyCloudy : MaterialIconKind.WeatherNightPartlyCloudy),
		2 => ("Partly cloudy", isDay ? MaterialIconKind.WeatherPartlyCloudy : MaterialIconKind.WeatherNightPartlyCloudy),
		3 => ("Overcast", MaterialIconKind.WeatherCloudy),
		45 => ("Fog", MaterialIconKind.WeatherFog),
		48 => ("Freezing fog", MaterialIconKind.WeatherFog),
		51 => ("Light drizzle", MaterialIconKind.WeatherRainy),
		53 => ("Drizzle", MaterialIconKind.WeatherRainy),
		55 => ("Heavy drizzle", MaterialIconKind.WeatherRainy),
		56 or 57 => ("Freezing drizzle", MaterialIconKind.WeatherSnowyRainy),
		61 => ("Light rain", MaterialIconKind.WeatherRainy),
		63 => ("Rain", MaterialIconKind.WeatherRainy),
		65 => ("Heavy rain", MaterialIconKind.WeatherPouring),
		66 or 67 => ("Freezing rain", MaterialIconKind.WeatherSnowyRainy),
		71 => ("Light snow", MaterialIconKind.WeatherSnowy),
		73 => ("Snow", MaterialIconKind.WeatherSnowy),
		75 => ("Heavy snow", MaterialIconKind.WeatherSnowyHeavy),
		77 => ("Snow grains", MaterialIconKind.WeatherSnowy),
		80 => ("Light showers", isDay ? MaterialIconKind.WeatherPartlyRainy : MaterialIconKind.WeatherRainy),
		81 => ("Showers", MaterialIconKind.WeatherRainy),
		82 => ("Heavy showers", MaterialIconKind.WeatherPouring),
		85 => ("Snow showers", isDay ? MaterialIconKind.WeatherPartlySnowy : MaterialIconKind.WeatherSnowy),
		86 => ("Heavy snow showers", MaterialIconKind.WeatherSnowyHeavy),
		95 => ("Thunderstorm", MaterialIconKind.WeatherLightning),
		96 or 99 => ("Thunderstorm, hail", MaterialIconKind.WeatherLightningRainy),
		_ => ("Unknown", MaterialIconKind.WeatherCloudy),
	};

	#endregion

	private static string Round(double value)
	{
		var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
		if (rounded == 0) rounded = 0; // Math.Round(-0.2) is -0, which .NET formats as "-0"
		return rounded.ToString("0", CultureInfo.CurrentCulture);
	}

	private static string Degrees(double? value) => value is { } v ? Round(v) + "°" : "--°";

	private void Render()
	{
		if (_data is not { } data)
		{
			var hasCity = CleanCity().Length > 0;
			WeatherPanel.Opacity = 0;
			StatusText.Visibility = Visibility.Visible;
			StatusText.Text = !hasCity ? "Set a city in this widget's settings" : _error ?? "Loading weather…";
			StatusText.Foreground = new SolidColorBrush(hasCity && _error is not null ? CurrentTheme.NoColor : CurrentTheme.SecondaryForegroundColor);
			Card.ClearValue(ToolTipService.ToolTipProperty);
			return;
		}

		WeatherPanel.Opacity = 1;
		StatusText.Visibility = Visibility.Collapsed;

		var (condition, icon) = Describe(data.Code, data.IsDay);
		ConditionIcon.Kind = icon;
		TemperatureText.Text = Degrees(data.Temperature);
		ConditionText.Text = condition;
		var today = data.Days.Count > 0 ? data.Days[0] : null;
		HighText.Text = "H " + Degrees(today?.High);
		LowText.Text = "L " + Degrees(today?.Low);
		LocationText.Text = data.Location;
		var wind = data.Wind is { } w ? $"{Round(w)} {(data.Fahrenheit ? "mph" : "km/h")}" : "--";
		DetailsText.Text = $"Feels like {Degrees(data.FeelsLike)} · Wind {wind}";

		for (var i = 0; i < ForecastDays; i++)
		{
			// Days[0] is today (shown above), so the forecast row starts with tomorrow
			var day = i + 1 < data.Days.Count ? data.Days[i + 1] : null;
			_dayNames[i].Text = day?.Date.ToString("ddd", CultureInfo.CurrentCulture) ?? "--";
			_dayIcons[i].Kind = Describe(day?.Code, isDay: true).icon;
			_dayHighs[i].Text = Degrees(day?.High);
			_dayLows[i].Text = Degrees(day?.Low);
		}

		var updated = "Updated " + _fetchedAt.ToString("t", CultureInfo.CurrentCulture);
		ToolTipService.SetToolTip(Card, _refreshFailed ? $"{data.Location}\nCouldn't refresh. {updated}" : $"{data.Location}\n{updated}");
	}

	protected override void ApplyTheme()
	{
		ApplyCardTheme(Card);
		var primary = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		var secondary = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		var accent = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);

		ConditionIcon.Foreground = accent;
		TemperatureText.Foreground = primary;
		ConditionText.Foreground = primary;
		HighText.Foreground = primary;
		LowText.Foreground = secondary;
		LocationText.Foreground = secondary;
		DetailsText.Foreground = secondary;
		ForecastDivider.Fill = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
		for (var i = 0; i < ForecastDays; i++)
		{
			_dayNames[i].Foreground = secondary;
			_dayIcons[i].Foreground = accent;
			_dayHighs[i].Foreground = primary;
			_dayLows[i].Foreground = secondary;
		}
		Render();
	}
}

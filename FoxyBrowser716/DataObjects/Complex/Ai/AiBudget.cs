using FoxyBrowser716.DataManagement;

namespace FoxyBrowser716.DataObjects.Complex.Ai;

/// <summary>What <see cref="AiBudget"/> saves: the cap, the reset day and spending per day.</summary>
public sealed partial class AiBudgetData : ObservableObject
{
	/// <summary>What the user expects to spend on Claude per period, in US dollars.</summary>
	[ObservableProperty] public partial decimal MonthlyCapUsd { get; set; } = 100m;

	/// <summary>Day of the month a new period starts, 1-31 (short months use their last day).</summary>
	[ObservableProperty] public partial int ResetDay { get; set; } = 1;

	/// <summary>
	/// Estimated spend per day ("yyyy-MM-dd" → $). Kept per day rather than as one running total, so moving the reset
	/// day re-counts the period correctly. Replace the dictionary to save.
	/// </summary>
	[ObservableProperty] public partial Dictionary<string, decimal> Daily { get; set; } = [];
}

/// <summary>
/// Estimated Claude spending by this browser, against a monthly cap that resets on a chosen day. A normal API key
/// can't read the real balance, so this adds up the cost estimates of every Claude response (chats and quick
/// jobs). Shared by every instance, like the API key.
/// </summary>
public static class AiBudget
{
	private const string DayFormat = "yyyy-MM-dd";

	private static readonly FoxyAutoSaverField<AiBudgetData> Field =
		new(() => new AiBudgetData(), "AiBudget.json", FoxyFileManager.FolderType.BrowserAppData);

	private static Task? _loading;

	/// <summary>Raised after spending, the cap or the reset day changes.</summary>
	public static event Action? Changed;

	/// <summary>Loads the saved budget once; safe to call from every window.</summary>
	public static Task EnsureLoaded() => _loading ??= AppServer.AutoSaver.AddItem(Field);

	private static AiBudgetData? Data => Field.IsLoaded ? Field.Item : null;

	public static bool IsLoaded => Data is not null;

	public static decimal Cap => Data?.MonthlyCapUsd ?? 100m;

	public static int ResetDay => Data?.ResetDay ?? 1;

	public static void SetCap(decimal cap)
	{
		if (Data is not { } data) return;
		data.MonthlyCapUsd = Math.Max(0, cap);
		Changed?.Invoke();
	}

	public static void SetResetDay(int day)
	{
		if (Data is not { } data) return;
		data.ResetDay = Math.Clamp(day, 1, 31);
		Changed?.Invoke();
	}

	/// <summary>Adds the cost of a Claude response to today.</summary>
	public static void Add(decimal costUsd)
	{
		if (Data is not { } data || costUsd <= 0) return;
		var today = DateTime.Now.ToString(DayFormat);
		var cutoff = DateTime.Now.AddDays(-62).ToString(DayFormat);

		// a new dictionary, so the saver sees the change; days older than two periods are dropped
		var daily = data.Daily.Where(d => string.CompareOrdinal(d.Key, cutoff) >= 0).ToDictionary();
		daily[today] = daily.GetValueOrDefault(today) + costUsd;
		data.Daily = daily;
		Changed?.Invoke();
	}

	/// <summary>Forgets this period's spending (for example after topping up, or to start counting from now).</summary>
	public static void ClearPeriod()
	{
		if (Data is not { } data) return;
		var start = PeriodStart(DateTime.Now, data.ResetDay).ToString(DayFormat);
		data.Daily = data.Daily.Where(d => string.CompareOrdinal(d.Key, start) < 0).ToDictionary();
		Changed?.Invoke();
	}

	/// <summary>Spending in the current period, the cap, and when the next period starts.</summary>
	public static (decimal Spent, decimal Cap, DateTime Start, DateTime NextReset) Current()
	{
		var now = DateTime.Now;
		var start = PeriodStart(now, ResetDay);
		var next = DayIn(start.AddMonths(1), ResetDay);
		var from = start.ToString(DayFormat);
		var spent = Data?.Daily.Where(d => string.CompareOrdinal(d.Key, from) >= 0).Sum(d => d.Value) ?? 0m;
		return (spent, Cap, start, next);
	}

	/// <summary>The latest reset day on or before <paramref name="now"/>.</summary>
	private static DateTime PeriodStart(DateTime now, int resetDay)
	{
		var start = DayIn(now, resetDay);
		return now.Date >= start ? start : DayIn(now.AddMonths(-1), resetDay);
	}

	/// <summary>The reset day in <paramref name="month"/>'s month, or that month's last day if it's shorter.</summary>
	private static DateTime DayIn(DateTime month, int resetDay) =>
		new(month.Year, month.Month, Math.Min(resetDay, DateTime.DaysInMonth(month.Year, month.Month)));
}

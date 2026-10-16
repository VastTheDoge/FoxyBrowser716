using System.Collections.Specialized;
using System.Threading;
using FoxyBrowser716.Controls.MainWindow;
using FoxyBrowser716.ErrorHandeler;

namespace FoxyBrowser716.DataObjects.Complex.Ai;

// Claude Doc
/// <summary>
/// Names a window's tab groups from their tabs with the quick model (<see cref="Settings.BrowserSettings.AiQuickModel"/>),
/// when <see cref="Settings.BrowserSettings.AiNameTabGroups"/> is on. Only groups still called "Group N", or still
/// carrying a name this gave them, are (re)named, so a name the user or the assistant chose is never overwritten.
/// </summary>
internal sealed class TabGroupNamer
{
	private const string SystemPrompt =
		"You name groups of browser tabs. Reply with the name only: one to three words in Title Case that say what the tabs have in common. No quotes, emoji or final punctuation.";

	// Claude Doc
	/// <summary>Waits this long after the last change to a group's tabs, so dragging in several tabs costs one call.</summary>
	private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1.5);

	private readonly MainWindow _window;
	private readonly Dictionary<TabGroup, string> _givenNames = [];
	private readonly Dictionary<TabGroup, CancellationTokenSource> _pending = [];

	public TabGroupNamer(MainWindow window)
	{
		_window = window;
		var groups = window.TabManager.Groups;
		groups.CollectionChanged += (_, e) =>
		{
			if (e.Action == NotifyCollectionChangedAction.Reset) return;
			foreach (TabGroup group in e.NewItems ?? Array.Empty<TabGroup>())
			{
				group.Tabs.CollectionChanged += (_, _) => Schedule(group);
				Schedule(group);
			}
			foreach (TabGroup group in e.OldItems ?? Array.Empty<TabGroup>())
			{
				_givenNames.Remove(group);
				if (_pending.Remove(group, out var cts)) cts.Cancel();
			}
		};
		foreach (var group in groups)
			group.Tabs.CollectionChanged += (_, _) => Schedule(group);
	}

	private bool CanRename(TabGroup group) =>
		group.Name == $"Group {group.Id}" || (_givenNames.TryGetValue(group, out var given) && group.Name == given);

	private async void Schedule(TabGroup group)
	{
		var settings = _window.Instance.Settings;
		if (!settings.AiEnabled || !settings.AiNameTabGroups || !CanRename(group)) return;

		if (_pending.Remove(group, out var previous)) previous.Cancel();
		var cts = new CancellationTokenSource();
		_pending[group] = cts;

		try
		{
			await Task.Delay(Debounce, cts.Token);
			if (group.Tabs.Count == 0 || !_window.TabManager.Groups.Contains(group) || !CanRename(group)) return;

			var tabs = string.Join("\n", group.Tabs.Take(20).Select(t =>
			{
				var host = Uri.TryCreate(t.Info.Url, UriKind.Absolute, out var uri) ? uri.Host : t.Info.Url;
				return string.IsNullOrWhiteSpace(t.Info.Title) ? $"- {host}" : $"- {t.Info.Title} ({host})";
			}));

			cts.CancelAfter(TimeSpan.FromSeconds(30));
			var model = settings.AiQuickModel;
			var (text, usage) = await AiConversation.CompleteAsync(model, settings, SystemPrompt, $"Tabs in the group:\n{tabs}", cts.Token);
			if (model.Provider == AiProviderKind.Claude && usage.CostUsd is { } cost) AiBudget.Add(cost);

			var name = Clean(text);
			if (name.Length == 0 || !_window.TabManager.Groups.Contains(group) || !CanRename(group)) return;
			group.Name = name;
			_givenNames[group] = name;
		}
		catch (OperationCanceledException)
		{
			//C a newer change took over, or the call took too long
		}
		catch (AiNotConfiguredException)
		{
			//C no key or server yet; the panel says so when it's opened
		}
		catch (Exception e)
		{
			FoxyLogger.AddWarning("Couldn't name a tab group", e.Message);
		}
		finally
		{
			if (_pending.TryGetValue(group, out var current) && current == cts) _pending.Remove(group);
		}
	}

	// Claude Doc
	/// <summary>The first line, without quotes or trailing punctuation, at most 32 characters.</summary>
	private static string Clean(string text)
	{
		var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
		line = line.Trim('"', '\'', '`', '*', '.', '!', '?', ':', ' ');
		return line.Length > 32 ? line[..32].TrimEnd() : line;
	}
}

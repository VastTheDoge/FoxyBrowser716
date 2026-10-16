using FoxyBrowser716.Controls.MainWindow;
using FoxyBrowser716.DataManagement;
using Microsoft.Web.WebView2.Core;

namespace FoxyBrowser716.DataObjects.Complex.Ai;

// Claude Doc
/// <summary>
/// The tools the assistant gets for the window it lives in: tabs and tab groups, reading pages as text (or a
/// screenshot), and acting on them, plus the other windows it can reach (its instance's, and other instances' when
/// <see cref="Settings.BrowserSettings.AiSeeOtherInstances"/> allows; never across a private instance). Tab and group ids are
/// unique across windows, so tab tools find a tab wherever it is. Permission checks happen in <see cref="AiChat"/>;
/// these only validate input and do the work.
/// </summary>
public static class BrowserTools
{
	// Claude Doc
	/// <summary>Characters per <c>read_page</c> call (~5k tokens), small enough for local models' context windows.</summary>
	private const int ReadChunk = 20_000;

	// Claude Doc
	/// <summary><see cref="TabManager.ActiveTabId"/> while the home page or the settings page is showing.</summary>
	private const int HomeId = -1, SettingsId = -2;

	private static readonly TimeSpan NavigationWait = TimeSpan.FromSeconds(10);

	public static List<AiTool> Create(MainWindow window)
	{
		var tabs = window.TabManager;

		#region helpers
		//C tab_id is optional on page tools: no id means the tab the user is looking at
		async Task<(int Id, WebviewTab Tab, CoreWebView2 Core)> Page(IReadOnlyDictionary<string, JsonElement> input)
		{
			var id = input.ContainsKey("tab_id") ? GetInt(input, "tab_id") : tabs.ActiveTabId;
			switch (id)
			{
				case HomeId:
					throw new AiToolException("Tab -1 is the FoxyBrowser home page: part of the browser, not a website, so page tools can't read or use it. Pick a website tab from get_list_of_tabs.");
				case SettingsId:
					throw new AiToolException("The user is on the FoxyBrowser settings page, which page tools can't read or use. Pick a website tab from get_list_of_tabs.");
			}
			var tab = Tab(id);
			await Task.WhenAny(tab.InitializeTask, Task.Delay(TimeSpan.FromSeconds(10)));
			if (!tab.InitializeTask.IsCompletedSuccessfully || tab.Core?.CoreWebView2 is not { } core)
				throw new AiToolException($"Tab {id} hasn't loaded yet.");
			return (id, tab, core);
		}

		//C The chat always reaches its own instance's windows; other instances' only when the user allows it. Private
		//C instances are walled off both ways: a private chat stays in its instance, and nothing looks into a private one.
		bool CanReach(Instance instance) =>
			instance == window.Instance
			|| (window.Instance.Settings.AiSeeOtherInstances && !window.Instance.IsPrivate && !instance.IsPrivate);

		string WhyUnreachable(Instance instance) =>
			window.Instance.IsPrivate ? "This chat is in a private instance, so other instances are off limits."
			: instance.IsPrivate ? "It's a private instance, so the assistant can't look into it."
			: "The user hasn't let the assistant see other instances (assistant settings → Permissions).";

		//C this window's tabs first, then the rest it can reach; tab and group ids are unique across all of them
		IEnumerable<TabManager> Managers() => AppServer.Instances
			.Where(CanReach)
			.SelectMany(i => i.Windows)
			.Select(w => w.TabManager)
			.Where(m => m != tabs)
			.Prepend(tabs);

		MainWindow Window(int id)
		{
			var found = AppServer.Instances.SelectMany(i => i.Windows).FirstOrDefault(w => w.WindowNumber == id)
				?? throw new AiToolException($"No window {id} (it may have been closed). Use list_windows to see the open windows.");
			return CanReach(found.Instance) ? found : throw new AiToolException($"Window {id} belongs to another instance. {WhyUnreachable(found.Instance)}");
		}

		Instance InstanceNamed(string name)
		{
			var found = AppServer.Instances.FirstOrDefault(i => string.Equals(i.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
				?? throw new AiToolException($"No instance named \"{name}\". Instances: {string.Join(", ", AppServer.Instances.Select(i => $"\"{i.Name}\""))}.");
			return CanReach(found) ? found : throw new AiToolException($"The assistant can't use the \"{found.Name}\" instance. {WhyUnreachable(found)}");
		}

		WebviewTab Tab(int id)
		{
			foreach (var manager in Managers())
				if (manager.TryGetTab(id, out var tab)) return tab!;
			throw new AiToolException($"No tab with id {id}. Use get_list_of_tabs to see the open tabs.");
		}

		(TabGroup Group, TabManager Manager) Group(int id)
		{
			foreach (var manager in Managers())
				if (manager.Groups.FirstOrDefault(g => g.Id == id) is { } group) return (group, manager);
			throw new AiToolException($"No group with id {id}. Use get_list_of_tabs to see the groups.");
		}

		//C group tools work inside one window, the way the user drags tabs into groups
		TabManager SameWindow(IEnumerable<int> ids)
		{
			var managers = ids.Select(id => Tab(id).TabManager).Distinct().ToList();
			return managers.Count == 1
				? managers[0]
				: throw new AiToolException("Those tabs are in different windows. Tab groups belong to one window; move the tabs together first with move_tabs_to_window.");
		}

		string InWindow(TabManager manager) => manager == tabs ? "" : $" in window {manager.Window.WindowNumber}";

		static string Host(CoreWebView2 core) => Uri.TryCreate(core.Source, UriKind.Absolute, out var uri) ? uri.Host : core.Source;

		static string Where(int id, CoreWebView2 core) => $"Tab {id} is at {core.Source} (\"{core.DocumentTitle}\").";

		static string TabLine(WebviewTab tab) => $"Tab {tab.Id}: \"{tab.Info.Title}\" at {tab.Info.Url}{(tab.Id == tab.TabManager.ActiveTabId ? " (ACTIVE)" : "")}";

		static string Showing(TabManager manager) => manager.ActiveTabId switch
		{
			HomeId => "showing the home page",
			SettingsId => "showing the settings page",
			var id when manager.TryGetTab(id, out var tab) => $"showing tab {id} \"{tab!.Info.Title}\" at {tab.Info.Url}",
			_ => "showing nothing yet",
		};

		static JsonElement Check(JsonElement result, string elementRef)
		{
			if (!result.TryGetProperty("error", out var error)) return result;
			var desc = result.TryGetProperty("desc", out var d) ? d.GetString() : elementRef;
			throw new AiToolException(error.GetString() switch
			{
				"stale" => $"No element {elementRef} on the page now (the page changed or navigated). Read the page again for fresh refs.",
				"covered" => $"{desc} is covered by {result.GetProperty("cover").GetString()}. Deal with that first (often a cookie banner or popup).",
				"invisible" => $"{desc} has no size on screen, so it can't be clicked. It may be in a closed menu.",
				"sensitive" => "Typing into password and payment card fields is blocked. Ask the user to fill that in themselves.",
				"disabled" => $"{desc} is disabled (or read-only), so it can't be used right now.",
				"not a select" => $"{desc} isn't a <select>. Click it to open it, then click the option.",
				"no such option" => $"No option like that. Options: {string.Join(" | ", result.GetProperty("options").EnumerateArray().Select(o => o.GetString()))}",
				var other => $"{desc}: {other}",
			});
		}

		static string Describe(JsonElement result, string elementRef) => Check(result, elementRef).GetProperty("desc").GetString() ?? elementRef;

		//C Runs a page action, waits for any navigation it starts, and reports where the tab is now and any tabs it opened
		//C (links with target=_blank open a tab and switch to it).
		async Task<string> Act(int id, CoreWebView2 core, Func<Task> action, string done)
		{
			var manager = Tab(id).TabManager;
			var before = manager.GetAllTabs().Keys.ToHashSet();
			var navigated = await PageAgent.RunAndWaitForNavigation(core, action, NavigationWait);

			var sb = new StringBuilder(done).Append(' ').Append(Where(id, core));
			if (navigated) sb.Append(" (It navigated.)");
			foreach (var (newId, tab) in manager.GetAllTabs().Where(t => !before.Contains(t.Key)))
				sb.Append($" It opened a new tab {newId}{InWindow(manager)} at {tab.Info.Url}{(newId == manager.ActiveTabId ? ", which is now the active tab" : "")}.");
			return sb.ToString();
		}

		static Task<AiToolOutput> Done(string text) => Task.FromResult<AiToolOutput>(text);
		#endregion

		var tabId = Prop("integer", "Tab id from get_list_of_tabs. Leave out to use the tab the user is looking at.");
		var elementRef = Prop("string", "Element ref from read_page or find_in_page, like \"e12\".");
		var tabIds = ArrayProp("integer", "Tab ids from get_list_of_tabs.");
		var groupId = Prop("integer", "Group id from get_list_of_tabs.");
		var color = ColorProp("Group color as red, green and blue, each 0-255, like { \"r\": 173, \"g\": 216, \"b\": 230 } for light blue.");
		var windowId = Prop("integer", "Window id from list_windows.");

		return
		[
			#region Reading
			new("get_list_of_tabs", "List tabs", AiToolKind.Read,
				"List the tabs in this window (or another one, with window_id) in the order the user sees them, with their tab groups: ids, titles, URLs, and which one is active. " +
				"Tab -1 is this window's FoxyBrowser home page (the user can be on it; switch_to_tab can go back to it). " +
				"Tab and group ids are unique across windows, so the page, tab and group tools also work on tabs listed for other windows.",
				Props(("window_id", Prop("integer", "Window id from list_windows. Leave out for this window."))), [],
				input =>
				{
					var manager = input.ContainsKey("window_id") ? Window(GetInt(input, "window_id")).TabManager : tabs;
					var sb = new StringBuilder();
					if (manager == tabs)
					{
						sb.AppendLine($"Tab -1: FoxyBrowser home page{(tabs.ActiveTabId == HomeId ? " (ACTIVE)" : "")}");
						if (tabs.ActiveTabId == SettingsId) sb.AppendLine("The user is looking at the FoxyBrowser settings page.");
					}
					else
						sb.AppendLine($"Window {manager.Window.WindowNumber} of the \"{manager.Instance.Name}\" instance, {Showing(manager)}:");
					foreach (var tab in manager.Tabs) sb.AppendLine(TabLine(tab));
					foreach (var group in manager.Groups)
					{
						sb.AppendLine($"Group {group.Id} \"{group.Name}\" ({Rgb(group.GroupColor)}), {group.Tabs.Count} tab(s):");
						foreach (var tab in group.Tabs) sb.AppendLine("  " + TabLine(tab));
					}
					if (manager.GetAllTabs().Count == 0) sb.AppendLine("No website tabs are open.");
					return Done(sb.ToString());
				}),

			new("read_page", "Read page", AiToolKind.Read,
				"Read a tab's page as text: headings (#), lists, tables, and every link, button and form field inline with a ref, " +
				"like [e12 button \"Add to cart\"] or [e7 textbox \"Search\" = \"current value\"]. Use the refs with click, type_text and the other page tools. " +
				$"Long pages come in parts of about {ReadChunk:N0} characters; the result says where the next part starts.",
				Props(("tab_id", tabId), ("start_at", Prop("integer", "Character offset to start from, for later parts of a long page. Default 0."))), [],
				async input =>
				{
					var (id, _, core) = await Page(input);
					await PageAgent.WaitForLoad(core, TimeSpan.FromSeconds(8));
					var start = input.ContainsKey("start_at") ? GetInt(input, "start_at") : 0;
					var page = await PageAgent.Read(core, start, ReadChunk);
					if (page.TryGetProperty("empty", out _))
					{
						//C caught between two documents while navigating
						await Task.Delay(1000);
						await PageAgent.WaitForLoad(core, TimeSpan.FromSeconds(5));
						page = await PageAgent.Read(core, start, ReadChunk);
						if (page.TryGetProperty("empty", out _))
							throw new AiToolException($"Tab {id} hasn't shown anything yet; it's still loading. Use wait_for, then read again.");
					}

					var total = page.GetProperty("total").GetInt32();
					var end = page.GetProperty("end").GetInt32();
					var sb = new StringBuilder();
					sb.AppendLine($"Tab {id}: \"{page.GetProperty("title").GetString()}\" at {page.GetProperty("url").GetString()}");
					if (page.GetProperty("loading").GetBoolean()) sb.AppendLine("(The page is still loading; use wait_for and read again if something seems missing.)");
					sb.AppendLine(total > ReadChunk || start > 0
						? $"Characters {page.GetProperty("start").GetInt32():N0}-{end:N0} of {total:N0}."
						: $"Whole page, {total:N0} characters.");
					sb.AppendLine();
					sb.AppendLine(page.GetProperty("text").GetString());
					if (end < total) sb.AppendLine($"\n[More below: read_page with start_at={end} for the next part, or find_in_page to jump to something.]");
					return sb.ToString();
				}),

			new("find_in_page", "Find in page", AiToolKind.Read,
				"Search a tab's page text (the same text read_page returns) and get each matching line with its refs and the start_at offset to read around it. " +
				"Faster than reading a long page part by part.",
				Props(("tab_id", tabId), ("text", Prop("string", "Text to look for, case-insensitive."))), ["text"],
				async input =>
				{
					var (id, _, core) = await Page(input);
					await PageAgent.WaitForLoad(core, TimeSpan.FromSeconds(8));
					var result = await PageAgent.Find(core, GetString(input, "text"));
					var count = result.GetProperty("count").GetInt32();
					if (count == 0) return result.TryGetProperty("loading", out _) ? $"Tab {id} is still loading; try wait_for first." : $"No matches on tab {id}.";

					var sb = new StringBuilder($"{count} matching line(s) on tab {id}{(count > 40 ? ", first 40 shown" : "")}:\n");
					foreach (var match in result.GetProperty("matches").EnumerateArray())
						sb.AppendLine($"[start_at {match.GetProperty("offset").GetInt32()}] {match.GetProperty("line").GetString()}");
					return sb.ToString();
				}),

			new("wait_for", "Wait for page", AiToolKind.Read,
				"Wait for a tab to finish loading, or for some text to appear on it (content that arrives after the page loads, like a status that says \"Loading...\" at first). " +
				"Returns as soon as the text shows up.",
				Props(("tab_id", tabId), ("text", Prop("string", "Text to wait for, case-insensitive. Leave out to just wait for the page to load.")),
					("seconds", Prop("integer", "How long to wait at most, 1-30. Default 10."))), [],
				async input =>
				{
					var (id, _, core) = await Page(input);
					var seconds = input.ContainsKey("seconds") ? Math.Clamp(GetInt(input, "seconds"), 1, 30) : 10;
					var deadline = DateTime.UtcNow.AddSeconds(seconds);
					var started = DateTime.UtcNow;
					await PageAgent.WaitForLoad(core, TimeSpan.FromSeconds(seconds));

					if (!input.ContainsKey("text"))
						return DateTime.UtcNow < deadline ? $"Tab {id} has finished loading." : $"Tab {id} was still loading after {seconds} seconds.";

					var text = GetString(input, "text");
					while (true)
					{
						var result = await PageAgent.Find(core, text);
						if (result.GetProperty("count").GetInt32() > 0)
							return $"\"{text}\" is on tab {id} (after {(DateTime.UtcNow - started).TotalSeconds:0.#} s). Read or find it now.";
						if (DateTime.UtcNow >= deadline)
							return $"\"{text}\" didn't appear on tab {id} within {seconds} seconds.";
						await Task.Delay(500);
					}
				}),

			new("screenshot", "Screenshot", AiToolKind.Read,
				"Take a screenshot of what a tab shows on screen right now. It costs far more than read_page (about 1,500 tokens per image), so use it only when needed: " +
				"when how the page looks matters (images, charts, maps, canvas, a broken layout) or read_page doesn't explain what the user is seeing. " +
				"Shows only the visible part; scroll_page to see more. Works best on the active tab.",
				Props(("tab_id", tabId)), [],
				async input =>
				{
					var (id, _, core) = await Page(input);
					var image = await PageAgent.Screenshot(core, TimeSpan.FromSeconds(8));
					return new AiToolOutput($"Screenshot of tab {id} ({core.Source}).", image, "image/jpeg");
				})
			{
				ReturnsImage = true,
			},

			new("scroll_page", "Scroll page", AiToolKind.Read,
				"Scroll a tab: up/down by a screen, to the top/bottom, or to an element. Useful for pages that load more content as you scroll; read the page again afterwards.",
				Props(("tab_id", tabId), ("direction", Prop("string", "up, down, top or bottom.", ["up", "down", "top", "bottom"])), ("ref", Prop("string", "Element to scroll to instead of a direction."))), [],
				async input =>
				{
					var (id, _, core) = await Page(input);
					var hasRef = input.ContainsKey("ref");
					if (!hasRef && !input.ContainsKey("direction")) throw new AiToolException("Give a direction or a ref.");
					var r = hasRef ? GetString(input, "ref") : null;
					var result = await PageAgent.Scroll(core, r, hasRef ? null : GetString(input, "direction"));
					if (r is not null) Check(result, r);
					return $"Tab {id} scrolled to {result.GetProperty("percent").GetInt32()}% of the page.";
				}),
			#endregion

			#region Page actions
			new("click", "Click", AiToolKind.Action,
				"Click an element on a page, like a real mouse click. Use a ref from read_page or find_in_page. " +
				"If the click opens a page or a new tab, this waits for it to load and says where things are.",
				Props(("tab_id", tabId), ("ref", elementRef)), ["ref"],
				async input =>
				{
					var (id, _, core) = await Page(input);
					var r = GetString(input, "ref");
					var target = Check(await PageAgent.Target(core, r), r);
					var x = target.GetProperty("x").GetDouble();
					var y = target.GetProperty("y").GetDouble();
					var result = await Act(id, core, () => PageAgent.Click(core, x, y), $"Clicked {target.GetProperty("desc").GetString()}.");
					return result + " Read the page again to see what changed.";
				})
			{
				Describe = async input =>
				{
					var (_, _, core) = await Page(input);
					var r = GetString(input, "ref");
					return $"Click {Describe(await PageAgent.Describe(core, r), r)} on {Host(core)}";
				},
			},

			new("type_text", "Type text", AiToolKind.Action,
				"Replace the text in a text field (or editable area) on a page, as if the user typed it. Optionally press Enter afterwards, e.g. to search. " +
				"Password and payment card fields are blocked.",
				Props(("tab_id", tabId), ("ref", elementRef), ("text", Prop("string", "The text. Empty clears the field.")),
					("press_enter", Prop("boolean", "Press Enter after typing. Default false."))), ["ref", "text"],
				async input =>
				{
					var (id, _, core) = await Page(input);
					var r = GetString(input, "ref");
					var text = GetString(input, "text", allowEmpty: true);
					var enter = GetBool(input, "press_enter");
					var focus = Check(await PageAgent.Focus(core, r), r);
					if (!focus.GetProperty("focused").GetBoolean())
						throw new AiToolException($"Couldn't focus {focus.GetProperty("desc").GetString()}. Try clicking it first.");

					return await Act(id, core, async () =>
					{
						if (text.Length > 0) await PageAgent.InsertText(core, text);
						else await PageAgent.PressKey(core, "Backspace");
						if (enter) await PageAgent.PressKey(core, "Enter");
					}, $"Typed into {focus.GetProperty("desc").GetString()}{(enter ? " and pressed Enter" : "")}.");
				})
			{
				Describe = async input =>
				{
					var (_, _, core) = await Page(input);
					var r = GetString(input, "ref");
					var text = GetString(input, "text", allowEmpty: true);
					var what = text.Length > 0 ? $"Type \"{Truncate(text, 300)}\" into" : "Clear";
					return $"{what} {Describe(await PageAgent.Describe(core, r), r)} on {Host(core)}{(GetBool(input, "press_enter") ? ", then press Enter" : "")}";
				},
			},

			new("select_option", "Choose option", AiToolKind.Action,
				"Choose an option in a dropdown (<select>) on a page, by its text. For custom dropdowns that aren't a <select>, click them open and click the option instead.",
				Props(("tab_id", tabId), ("ref", elementRef), ("option", Prop("string", "The option's text (or value)."))), ["ref", "option"],
				async input =>
				{
					var (id, _, core) = await Page(input);
					var r = GetString(input, "ref");
					JsonElement result = default;
					var done = await Act(id, core, async () => result = Check(await PageAgent.Select(core, r, GetString(input, "option")), r), "");
					return $"Chose \"{result.GetProperty("selected").GetString()}\" in {result.GetProperty("desc").GetString()}.{done}";
				})
			{
				Describe = async input =>
				{
					var (_, _, core) = await Page(input);
					var r = GetString(input, "ref");
					return $"Choose \"{GetString(input, "option")}\" in {Describe(await PageAgent.Describe(core, r), r)} on {Host(core)}";
				},
			},

			new("press_key", "Press key", AiToolKind.Action,
				"Press a key on a page, sent to whatever has focus: closing popups (Escape), moving through forms (Tab), submitting (Enter), scrolling (PageDown).",
				Props(("tab_id", tabId), ("key", Prop("string", "The key.", PageAgent.KeyNames.ToArray()))), ["key"],
				async input =>
				{
					var (id, _, core) = await Page(input);
					var key = GetString(input, "key");
					return await Act(id, core, () => PageAgent.PressKey(core, key), $"Pressed {key}.");
				})
			{
				Describe = async input =>
				{
					var (_, _, core) = await Page(input);
					return $"Press {GetString(input, "key")} on {Host(core)}";
				},
			},
			#endregion

			#region Tabs and navigation
			new("open_tab", "Open tab", AiToolKind.Action,
				"Open a new tab at a URL and switch to it. Text that isn't a valid URL is searched with the default search engine.",
				Props(("url", Prop("string", "URL to open, or text to search for."))), ["url"],
				input =>
				{
					var url = GetString(input, "url");
					var id = tabs.AddTab(url);
					tabs.SwapActiveTabTo(id);
					return Done($"Opened tab {id} at '{url}' and switched to it. It may still be loading; read_page waits for it.");
				})
			{
				Describe = input => Task.FromResult($"Open a new tab at {GetString(input, "url")}"),
			},

			new("navigate_tab_to_url", "Navigate tab", AiToolKind.Action,
				"Point an existing tab at a URL and wait for it to load. Text that isn't a valid URL is searched with the default search engine.",
				Props(("tab_id", tabId), ("url", Prop("string", "URL to open, or text to search for."))), ["url"],
				async input =>
				{
					var (id, tab, core) = await Page(input);
					var url = GetString(input, "url");
					return await Act(id, core, () => tab.NavigateOrSearch(url), $"Went to (or searched for) '{url}'.");
				})
			{
				Describe = async input =>
				{
					var (id, _, core) = await Page(input);
					return $"Go to {GetString(input, "url")} in tab {id} (now on {Host(core)})";
				},
			},

			new("switch_to_tab", "Switch tab", AiToolKind.Action,
				"Show a tab to the user. Tab -1 is this window's FoxyBrowser home page. A tab in another window becomes that window's active tab, " +
				"but the window isn't brought to the front; use focus_window for that.",
				Props(("tab_id", Prop("integer", "Tab id from get_list_of_tabs, or -1 for the home page."))), ["tab_id"],
				input =>
				{
					var id = GetInt(input, "tab_id");
					if (id == SettingsId) throw new AiToolException("The assistant can't open the settings page. Ask the user to open it (☰ menu → Settings).");
					if (id == HomeId)
					{
						tabs.SwapActiveTabTo(HomeId);
						return Done("Switched to the home page.");
					}
					var manager = Tab(id).TabManager;
					manager.SwapActiveTabTo(id);
					return Done(manager == tabs
						? $"Switched to tab {id}."
						: $"Window {manager.Window.WindowNumber} now shows tab {id}. It wasn't brought to the front; use focus_window if the user should see it.");
				})
			{
				Describe = input =>
				{
					var id = GetInt(input, "tab_id");
					return Task.FromResult(id == HomeId ? "Switch to the home page" : $"Switch to tab {id} (\"{Tab(id).Info.Title}\")");
				},
			},

			new("close_tab", "Close tab", AiToolKind.Action,
				"Close a tab.",
				Props(("tab_id", Prop("integer", "Tab id from get_list_of_tabs."))), ["tab_id"],
				input =>
				{
					var id = GetInt(input, "tab_id");
					if (id < 0) throw new AiToolException("The home and settings pages can't be closed.");
					var manager = Tab(id).TabManager;
					manager.RemoveTab(id);
					return Done(manager.TryGetTab(id, out _)
						? $"Tab {id} wasn't closed."
						: $"Closed tab {id}. The active tab{InWindow(manager)} is now {(manager.ActiveTabId == HomeId ? "the home page" : manager.ActiveTabId.ToString())}.");
				})
			{
				Describe = input =>
				{
					var tab = Tab(GetInt(input, "tab_id"));
					return Task.FromResult($"Close tab {tab.Id} (\"{tab.Info.Title}\"){InWindow(tab.TabManager)}");
				},
			},

			new("history_navigation", "Back / forward / reload", AiToolKind.Action,
				"Go back, go forward, or reload in a tab, and wait for the page to load.",
				Props(("tab_id", tabId), ("action", Prop("string", "back, forward or reload.", ["back", "forward", "reload"]))), ["action"],
				async input =>
				{
					var (id, _, core) = await Page(input);
					var action = GetString(input, "action");
					Action go = action switch
					{
						"back" when core.CanGoBack => core.GoBack,
						"forward" when core.CanGoForward => core.GoForward,
						"reload" => core.Reload,
						"back" or "forward" => throw new AiToolException($"Tab {id} has nowhere to go {action}."),
						_ => throw new AiToolException("action must be back, forward or reload."),
					};
					return await Act(id, core, () => { go(); return Task.CompletedTask; }, "Done.");
				})
			{
				Describe = async input =>
				{
					var (id, _, core) = await Page(input);
					return $"{GetString(input, "action") switch { "back" => "Go back", "forward" => "Go forward", _ => "Reload" }} in tab {id} ({Host(core)})";
				},
			},
			#endregion

			#region Tab groups
			new("group_tabs", "Group tabs", AiToolKind.Action,
				"Put tabs into a tab group: a new group (give it a short name), or an existing one (group_id). Tabs already in another group move over; a group left empty disappears. " +
				"A group holds tabs from its own window only.",
				Props(("tab_ids", tabIds), ("name", Prop("string", "Name for a new group, a few words. Renames the group when used with group_id.")),
					("group_id", groupId), ("color", color)), ["tab_ids"],
				input =>
				{
					var ids = GetIntArray(input, "tab_ids");
					var manager = SameWindow(ids);

					TabGroup group;
					if (input.ContainsKey("group_id"))
					{
						(group, var groupManager) = Group(GetInt(input, "group_id"));
						if (groupManager != manager)
							throw new AiToolException($"Group {group.Id} is in a different window from those tabs. Move the tabs to its window first with move_tabs_to_window.");
					}
					else group = manager.CreateGroup();
					if (input.ContainsKey("name")) group.Name = GetString(input, "name").Trim();
					if (input.ContainsKey("color")) group.GroupColor = GetColor(input, "color");
					foreach (var id in ids) manager.MoveTabToGroup(id, group.Id);
					return Done($"Group {group.Id} \"{group.Name}\"{InWindow(manager)} has {group.Tabs.Count} tab(s): {string.Join(", ", group.Tabs.Select(t => t.Id))}.");
				})
			{
				Describe = input =>
				{
					var ids = string.Join(", ", GetIntArray(input, "tab_ids").Select(id => $"\"{Tab(id).Info.Title}\""));
					var into = input.ContainsKey("group_id")
						? $"group \"{Group(GetInt(input, "group_id")).Group.Name}\""
						: input.ContainsKey("name") ? $"a new group \"{GetString(input, "name")}\"" : "a new group";
					return Task.FromResult($"Put {ids} into {into}");
				},
			},

			new("ungroup_tabs", "Ungroup tabs", AiToolKind.Action,
				"Take tabs out of their groups (they stay open). A group left empty disappears.",
				Props(("tab_ids", tabIds)), ["tab_ids"],
				input =>
				{
					var ids = GetIntArray(input, "tab_ids");
					var found = ids.Select(Tab).ToList();
					foreach (var tab in found) tab.TabManager.MoveTabToGroup(tab.Id, -1);
					return Done($"Tabs {string.Join(", ", ids)} are no longer in a group.");
				})
			{
				Describe = input => Task.FromResult($"Take {string.Join(", ", GetIntArray(input, "tab_ids").Select(id => $"\"{Tab(id).Info.Title}\""))} out of their groups"),
			},

			new("edit_group", "Edit group", AiToolKind.Action,
				"Rename a tab group or change its color.",
				Props(("group_id", groupId), ("name", Prop("string", "New name, a few words.")), ("color", color)), ["group_id"],
				input =>
				{
					var (group, _) = Group(GetInt(input, "group_id"));
					if (!input.ContainsKey("name") && !input.ContainsKey("color")) throw new AiToolException("Give a name or a color.");
					if (input.ContainsKey("name")) group.Name = GetString(input, "name").Trim();
					if (input.ContainsKey("color")) group.GroupColor = GetColor(input, "color");
					return Done($"Group {group.Id} is now \"{group.Name}\" ({Rgb(group.GroupColor)}).");
				})
			{
				Describe = input =>
				{
					var (group, _) = Group(GetInt(input, "group_id"));
					var changes = new List<string>();
					if (input.ContainsKey("name")) changes.Add($"rename it to \"{GetString(input, "name")}\"");
					if (input.ContainsKey("color")) changes.Add($"color it {Rgb(GetColor(input, "color"))}");
					return Task.FromResult($"Group \"{group.Name}\": {string.Join(", ", changes)}");
				},
			},

			new("remove_group", "Remove group", AiToolKind.Action,
				"Remove a tab group. By default its tabs stay open, ungrouped; with close_tabs they are closed too.",
				Props(("group_id", groupId), ("close_tabs", Prop("boolean", "Close the group's tabs as well. Default false."))), ["group_id"],
				input =>
				{
					var (group, manager) = Group(GetInt(input, "group_id"));
					var count = group.Tabs.Count;
					if (GetBool(input, "close_tabs"))
					{
						manager.RemoveGroup(group.Id);
						return Done($"Removed group \"{group.Name}\" and closed its {count} tab(s).");
					}
					manager.DissolveGroup(group);
					return Done($"Removed group \"{group.Name}\"; its {count} tab(s) are now ungrouped.");
				})
			{
				Describe = input =>
				{
					var (group, _) = Group(GetInt(input, "group_id"));
					return Task.FromResult(GetBool(input, "close_tabs")
						? $"Remove group \"{group.Name}\" and close its {group.Tabs.Count} tab(s)"
						: $"Remove group \"{group.Name}\" (its tabs stay open)");
				},
			},
			#endregion

			#region Windows and instances
			new("list_windows", "List windows", AiToolKind.Read,
				"List FoxyBrowser's open windows by instance. Instances are separate browser profiles, each with its own tabs, history and logins. " +
				"For each window: its id, what it's showing, how many tabs it has, and whether it's minimized. Use window_id with get_list_of_tabs to see a window's tabs.",
				Props(), [],
				_ =>
				{
					static string State(MainWindow w) => w.StateFromWindow() switch
					{
						MainWindow.BrowserWindowState.Minimized => ", minimized",
						MainWindow.BrowserWindowState.Maximized => ", maximized",
						MainWindow.BrowserWindowState.Borderless => ", full screen",
						_ => "",
					};

					var sb = new StringBuilder();
					foreach (var instance in AppServer.Instances.OrderByDescending(i => i == window.Instance))
					{
						List<string> tags = [];
						if (instance == window.Instance) tags.Add("this chat's instance");
						if (instance.IsPrimaryInstance) tags.Add("primary");
						if (instance.IsPrivate) tags.Add("private");
						var label = $"Instance \"{instance.Name}\"{(tags.Count > 0 ? $" ({string.Join(", ", tags)})" : "")}";
						var windows = instance.Windows.ToList();

						if (!CanReach(instance))
						{
							sb.AppendLine($"{label}: {windows.Count} window(s). {WhyUnreachable(instance)}");
							continue;
						}
						sb.AppendLine(windows.Count == 0 ? $"{label}: no open windows." : $"{label}:");
						foreach (var w in windows)
							sb.AppendLine($"  Window {w.WindowNumber}{(w == window ? " (THIS WINDOW, where the chat is)" : "")}: " +
								$"{w.TabManager.GetAllTabs().Count} tab(s), {Showing(w.TabManager)}{State(w)}");
					}
					return Done(sb.ToString());
				}),

			new("focus_window", "Focus window", AiToolKind.Action,
				"Bring a window to the front, restoring it if it's minimized.",
				Props(("window_id", windowId)), ["window_id"],
				input =>
				{
					var target = Window(GetInt(input, "window_id"));
					if (target.StateFromWindow() == MainWindow.BrowserWindowState.Minimized)
						target.ApplyWindowState(MainWindow.BrowserWindowState.Normal);
					target.Activate();
					return Done($"Window {target.WindowNumber} is in front now, {Showing(target.TabManager)}.");
				})
			{
				Describe = input =>
				{
					var target = Window(GetInt(input, "window_id"));
					return Task.FromResult($"Bring window {target.WindowNumber} (\"{target.Instance.Name}\" instance, {Showing(target.TabManager)}) to the front");
				},
			},

			new("open_window", "Open window", AiToolKind.Action,
				"Open a new window, optionally with tabs at some URLs, in this chat's instance or another one by name. " +
				"Text that isn't a valid URL is searched with the default search engine. The new window comes to the front.",
				Props(("urls", ArrayProp("string", "URLs (or search text) to open as tabs. Leave out for a window on the home page.")),
					("instance", Prop("string", "Instance name from list_windows. Leave out for this chat's instance."))), [],
				async input =>
				{
					var instance = input.ContainsKey("instance") ? InstanceNamed(GetString(input, "instance")) : window.Instance;
					var urls = input.ContainsKey("urls") ? GetStringArray(input, "urls") : [];
					var opened = await instance.CreateWindow(urls.ToArray()) ?? throw new AiToolException("The window couldn't be opened.");
					var ids = opened.TabManager.Tabs.Select(t => t.Id).ToList();
					return $"Opened window {opened.WindowNumber} in the \"{instance.Name}\" instance" +
						(ids.Count > 0 ? $" with tab(s) {string.Join(", ", ids)}. They may still be loading." : " on the home page.");
				})
			{
				Describe = input =>
				{
					var instance = input.ContainsKey("instance") ? InstanceNamed(GetString(input, "instance")) : window.Instance;
					var urls = input.ContainsKey("urls") ? GetStringArray(input, "urls") : [];
					return Task.FromResult($"Open a new window in the \"{instance.Name}\" instance" +
						(urls.Count > 0 ? $" with {string.Join(", ", urls.Select(u => Truncate(u, 200)))}" : ""));
				},
			},

			new("move_tabs_to_window", "Move tabs to window", AiToolKind.Action,
				"Move tabs to another window of the same instance, or into a new window when window_id is left out. " +
				"Moved tabs reload at their current URL and get new ids; their back/forward history doesn't come along. " +
				"Tabs can't move between instances (each has its own logins); to show a page in another instance, use open_window with its URL.",
				Props(("tab_ids", tabIds), ("window_id", Prop("integer", "Window to move them to, from list_windows. Leave out to put them in a new window."))), ["tab_ids"],
				async input =>
				{
					var moving = GetIntArray(input, "tab_ids").Select(Tab).ToList();
					var source = moving[0].TabManager;
					if (moving.Any(t => !TabManager.CanAcceptTabsFrom(t.TabManager, source)))
						throw new AiToolException("Those tabs are in different instances; move each instance's tabs separately.");

					var toNew = !input.ContainsKey("window_id");
					MainWindow target;
					if (toNew)
						target = await source.Instance.CreateWindow() ?? throw new AiToolException("The new window couldn't be opened.");
					else
					{
						target = Window(GetInt(input, "window_id"));
						if (!TabManager.CanAcceptTabsFrom(target.TabManager, source))
							throw new AiToolException($"Window {target.WindowNumber} is in the \"{target.Instance.Name}\" instance, and tabs can't move between instances. Use open_window with instance and their URLs instead.");
						if (moving.All(t => t.TabManager == target.TabManager))
							throw new AiToolException($"Those tabs are already in window {target.WindowNumber}.");
					}

					List<string> moved = [];
					foreach (var tab in moving.Where(t => t.TabManager != target.TabManager))
					{
						var before = target.TabManager.GetAllTabs().Keys.ToHashSet();
						target.TabManager.MoveTabFromWindow(tab, tab.TabManager, -1);
						var newId = target.TabManager.GetAllTabs().Keys.Where(k => !before.Contains(k)).Cast<int?>().FirstOrDefault();
						moved.Add(newId is null ? $"tab {tab.Id}" : $"tab {tab.Id} (now tab {newId})");
					}
					return $"Moved {string.Join(", ", moved)} into {(toNew ? "new " : "")}window {target.WindowNumber}. They're reloading at their URLs.";
				})
			{
				Describe = input =>
				{
					var titles = string.Join(", ", GetIntArray(input, "tab_ids").Select(id => $"\"{Tab(id).Info.Title}\""));
					return Task.FromResult(input.ContainsKey("window_id")
						? $"Move {titles} to window {Window(GetInt(input, "window_id")).WindowNumber}"
						: $"Move {titles} to a new window");
				},
			},
			#endregion

			new("run_script", "Run script", AiToolKind.Script,
				"Run JavaScript in a page and get the result as JSON. Promises are awaited, so async code works. " +
				"Prefer the other page tools; use this only when they can't do the job (for example pulling structured data out of a page). " +
				"The value of the last expression is returned; keep results small.",
				Props(("tab_id", tabId), ("code", Prop("string", "JavaScript to evaluate in the page."))), ["code"],
				async input =>
				{
					var (_, _, core) = await Page(input);
					var result = await PageAgent.Evaluate(core, GetString(input, "code"), TimeSpan.FromSeconds(30));
					return Truncate(result, 20_000);
				})
			{
				Describe = async input =>
				{
					var (_, _, core) = await Page(input);
					return $"Run this script on {Host(core)}:\n\n```js\n{Truncate(GetString(input, "code"), 5_000)}\n```";
				},
			},
		];
	}

	#region schema + input helpers
	private static JsonElement Prop(string type, string description, string[]? values = null) => values is null
		? JsonSerializer.SerializeToElement(new { type, description })
		: JsonSerializer.SerializeToElement(new { type, description, @enum = values });

	private static JsonElement ArrayProp(string itemType, string description) =>
		JsonSerializer.SerializeToElement(new { type = "array", items = new { type = itemType }, description });

	private static Dictionary<string, JsonElement> Props(params (string Name, JsonElement Schema)[] properties) =>
		properties.ToDictionary(p => p.Name, p => p.Schema);

	//C tool input isn't validated upstream (it streams in as the model writes it), so check it here
	internal static int GetInt(IReadOnlyDictionary<string, JsonElement> input, string name)
	{
		if (input.TryGetValue(name, out var value) && ToInt(value) is { } number) return number;
		throw new AiToolException($"'{name}' must be an integer.");
	}

	private static int? ToInt(JsonElement value) =>
		value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number
		: value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number
		: null;

	// Claude Doc
	/// <summary>A list of non-empty strings; a single string is taken as a list of one.</summary>
	private static List<string> GetStringArray(IReadOnlyDictionary<string, JsonElement> input, string name)
	{
		if (input.TryGetValue(name, out var value))
		{
			if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } single) return [single];
			if (value.ValueKind == JsonValueKind.Array)
			{
				var items = value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : null).ToList();
				if (items.All(i => !string.IsNullOrWhiteSpace(i))) return items.Select(i => i!).ToList();
			}
		}
		throw new AiToolException($"'{name}' must be a list of strings.");
	}

	// Claude Doc
	/// <summary>A list of integers; a single number is taken as a list of one.</summary>
	private static List<int> GetIntArray(IReadOnlyDictionary<string, JsonElement> input, string name)
	{
		if (input.TryGetValue(name, out var value))
		{
			if (ToInt(value) is { } single) return [single];
			if (value.ValueKind == JsonValueKind.Array)
			{
				var items = value.EnumerateArray().Select(ToInt).ToList();
				if (items.Count > 0 && items.All(i => i is not null)) return items.Select(i => i!.Value).Distinct().ToList();
			}
		}
		throw new AiToolException($"'{name}' must be a non-empty list of integers.");
	}

	internal static string GetString(IReadOnlyDictionary<string, JsonElement> input, string name, bool allowEmpty = false) =>
		input.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && (allowEmpty || text.Length > 0)
			? text
			: throw new AiToolException($"'{name}' must be a {(allowEmpty ? "" : "non-empty ")}string.");

	private static bool GetBool(IReadOnlyDictionary<string, JsonElement> input, string name) =>
		input.TryGetValue(name, out var value) && (value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && value.GetString() == "true"));

	private static string Truncate(string text, int max) => text.Length > max ? text[..max] + $"\n…({text.Length - max:N0} more characters cut)" : text;

	private static string Rgb(Color color) => $"rgb {color.R}, {color.G}, {color.B}";

	private static JsonElement ColorProp(string description)
	{
		var channel = new { type = "integer", minimum = 0, maximum = 255 };
		return JsonSerializer.SerializeToElement(new
		{
			type = "object",
			description,
			properties = new { r = channel, g = channel, b = channel },
			required = new[] { "r", "g", "b" },
		});
	}

	// Claude Doc
	/// <summary>A color given as <c>{ "r": 173, "g": 216, "b": 230 }</c>, each channel 0-255.</summary>
	private static Color GetColor(IReadOnlyDictionary<string, JsonElement> input, string name)
	{
		if (input.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Object)
		{
			byte? Channel(string key) => value.TryGetProperty(key, out var c) && ToInt(c) is int v and >= 0 and <= 255 ? (byte)v : null;
			if (Channel("r") is { } r && Channel("g") is { } g && Channel("b") is { } b)
				return Color.FromArgb(255, r, g, b);
		}
		throw new AiToolException($"'{name}' must be {{ \"r\": 0-255, \"g\": 0-255, \"b\": 0-255 }}.");
	}
	#endregion
}

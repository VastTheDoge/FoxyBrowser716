using FoxyBrowser716.Controls.MainWindow;
using FoxyBrowser716.DataManagement;
using Microsoft.Web.WebView2.Core;

namespace FoxyBrowser716.DataObjects.Complex.Ai;

/// <summary>
/// The tools the assistant gets for the window it lives in: tabs, reading pages as text, and acting on them.
/// Permission checks happen in <see cref="AiChat"/>; these only validate input and do the work.
/// </summary>
public static class BrowserTools
{
	/// <summary>Characters per <c>read_page</c> call (~5k tokens), small enough for local models' context windows.</summary>
	private const int ReadChunk = 20_000;

	public static List<AiTool> Create(MainWindow window)
	{
		var tabs = window.TabManager;

		#region helpers
		// tab_id is optional on page tools: no id means the tab the user is looking at
		async Task<(int Id, WebviewTab Tab, CoreWebView2 Core)> Page(IReadOnlyDictionary<string, JsonElement> input)
		{
			var id = input.ContainsKey("tab_id") ? GetInt(input, "tab_id") : tabs.ActiveTabId;
			if (id < 0)
				throw new AiToolException("The active tab is a FoxyBrowser page (home or settings), not a website. Pick a website tab from get_list_of_tabs.");
			var tab = Tab(id);
			await Task.WhenAny(tab.InitializeTask, Task.Delay(TimeSpan.FromSeconds(10)));
			if (!tab.InitializeTask.IsCompletedSuccessfully || tab.Core?.CoreWebView2 is not { } core)
				throw new AiToolException($"Tab {id} hasn't loaded yet.");
			return (id, tab, core);
		}

		WebviewTab Tab(int id) => tabs.TryGetTab(id, out var tab)
			? tab!
			: throw new AiToolException($"No tab with id {id}. Use get_list_of_tabs to see the open tabs.");

		static string Host(CoreWebView2 core) => Uri.TryCreate(core.Source, UriKind.Absolute, out var uri) ? uri.Host : core.Source;

		static string Where(int id, CoreWebView2 core) => $"Tab {id} is at {core.Source} (\"{core.DocumentTitle}\").";

		// fails the call with a useful message when the page script reports a problem with the element
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
				"disabled" => $"{desc} is disabled or read-only.",
				"not a select" => $"{desc} isn't a <select>. Click it to open it, then click the option.",
				"no such option" => $"No option like that. Options: {string.Join(" | ", result.GetProperty("options").EnumerateArray().Select(o => o.GetString()))}",
				var other => $"{desc}: {other}",
			});
		}

		static string Describe(JsonElement result, string elementRef) => Check(result, elementRef).GetProperty("desc").GetString() ?? elementRef;

		// lets the page react (navigation start, menus opening) before reporting where things stand
		static Task Settle() => Task.Delay(400);
		#endregion

		var tabId = Prop("integer", "Tab id from get_list_of_tabs. Leave out to use the tab the user is looking at.");
		var elementRef = Prop("string", "Element ref from read_page or find_in_page, like \"e12\".");

		return
		[
			#region Reading
			new("get_list_of_tabs", "List tabs", AiToolKind.Read,
				"List the open tabs in this window: id, title, URL, and which one is active.",
				Props(), [],
				_ =>
				{
					var sb = new StringBuilder();
					foreach (var (id, tab) in tabs.GetAllTabs())
						sb.AppendLine($"Tab {id}: '{tab.Info.Title}' at '{tab.Info.Url}'{(id == tabs.ActiveTabId ? " (ACTIVE)" : "")}");
					if (tabs.ActiveTabId < 0) sb.AppendLine("The user is looking at a FoxyBrowser page (home or settings), not one of these tabs.");
					return Task.FromResult(sb.Length > 0 ? sb.ToString() : "No tabs are open.");
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

					var total = page.GetProperty("total").GetInt32();
					var end = page.GetProperty("end").GetInt32();
					var sb = new StringBuilder();
					sb.AppendLine($"Tab {id}: \"{page.GetProperty("title").GetString()}\" at {page.GetProperty("url").GetString()}");
					if (page.GetProperty("loading").GetBoolean()) sb.AppendLine("(The page is still loading; read again if something seems missing.)");
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
					if (count == 0) return $"No matches on tab {id}.";

					var sb = new StringBuilder($"{count} matching line(s) on tab {id}{(count > 40 ? ", first 40 shown" : "")}:\n");
					foreach (var match in result.GetProperty("matches").EnumerateArray())
						sb.AppendLine($"[start_at {match.GetProperty("offset").GetInt32()}] {match.GetProperty("line").GetString()}");
					return sb.ToString();
				}),

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
				"Click an element on a page, like a real mouse click. Use a ref from read_page or find_in_page.",
				Props(("tab_id", tabId), ("ref", elementRef)), ["ref"],
				async input =>
				{
					var (id, _, core) = await Page(input);
					var r = GetString(input, "ref");
					var target = Check(await PageAgent.Target(core, r), r);
					var x = target.GetProperty("x").GetDouble();
					var y = target.GetProperty("y").GetDouble();
					await PageAgent.Click(core, x, y);
					await Settle();
					return $"Clicked {target.GetProperty("desc").GetString()}. {Where(id, core)} Read the page again to see what changed.";
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
					var focus = Check(await PageAgent.Focus(core, r), r);
					if (!focus.GetProperty("focused").GetBoolean())
						throw new AiToolException($"Couldn't focus {focus.GetProperty("desc").GetString()}. Try clicking it first.");

					if (text.Length > 0) await PageAgent.InsertText(core, text);
					else await PageAgent.PressKey(core, "Backspace");
					if (GetBool(input, "press_enter")) await PageAgent.PressKey(core, "Enter");
					await Settle();
					return $"Typed into {focus.GetProperty("desc").GetString()}{(GetBool(input, "press_enter") ? " and pressed Enter" : "")}. {Where(id, core)}";
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
					var result = Check(await PageAgent.Select(core, r, GetString(input, "option")), r);
					await Settle();
					return $"Chose \"{result.GetProperty("selected").GetString()}\" in {result.GetProperty("desc").GetString()}. {Where(id, core)}";
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
					await PageAgent.PressKey(core, key);
					await Settle();
					return $"Pressed {key}. {Where(id, core)}";
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
					return Task.FromResult($"Opened tab {id} at '{url}'. It may still be loading.");
				})
			{
				Describe = input => Task.FromResult($"Open a new tab at {GetString(input, "url")}"),
			},

			new("navigate_tab_to_url", "Navigate tab", AiToolKind.Action,
				"Point an existing tab at a URL. Text that isn't a valid URL is searched with the default search engine.",
				Props(("tab_id", tabId), ("url", Prop("string", "URL to open, or text to search for."))), ["url"],
				async input =>
				{
					var (id, tab, _) = await Page(input);
					var url = GetString(input, "url");
					await tab.NavigateOrSearch(url);
					return $"Tab {id} is navigating to (or searching for) '{url}'.";
				})
			{
				Describe = async input =>
				{
					var (id, _, core) = await Page(input);
					return $"Go to {GetString(input, "url")} in tab {id} (now on {Host(core)})";
				},
			},

			new("switch_to_tab", "Switch tab", AiToolKind.Action,
				"Show a tab to the user.",
				Props(("tab_id", Prop("integer", "Tab id from get_list_of_tabs."))), ["tab_id"],
				input =>
				{
					var id = GetInt(input, "tab_id");
					Tab(id);
					tabs.SwapActiveTabTo(id);
					return Task.FromResult($"Switched to tab {id}.");
				})
			{
				Describe = input => Task.FromResult($"Switch to tab {GetInt(input, "tab_id")} (\"{Tab(GetInt(input, "tab_id")).Info.Title}\")"),
			},

			new("close_tab", "Close tab", AiToolKind.Action,
				"Close a tab.",
				Props(("tab_id", Prop("integer", "Tab id from get_list_of_tabs."))), ["tab_id"],
				input =>
				{
					var id = GetInt(input, "tab_id");
					Tab(id);
					tabs.RemoveTab(id);
					return Task.FromResult(tabs.TryGetTab(id, out _) ? $"Tab {id} wasn't closed (it may be the last tab)." : $"Closed tab {id}.");
				})
			{
				Describe = input => Task.FromResult($"Close tab {GetInt(input, "tab_id")} (\"{Tab(GetInt(input, "tab_id")).Info.Title}\")"),
			},

			new("history_navigation", "Back / forward / reload", AiToolKind.Action,
				"Go back, go forward, or reload in a tab.",
				Props(("tab_id", tabId), ("action", Prop("string", "back, forward or reload.", ["back", "forward", "reload"]))), ["action"],
				async input =>
				{
					var (id, _, core) = await Page(input);
					switch (GetString(input, "action"))
					{
						case "back" when core.CanGoBack: core.GoBack(); break;
						case "forward" when core.CanGoForward: core.GoForward(); break;
						case "reload": core.Reload(); break;
						case "back" or "forward": throw new AiToolException($"Tab {id} has nowhere to go {GetString(input, "action")}.");
						default: throw new AiToolException("action must be back, forward or reload.");
					}
					await Settle();
					return Where(id, core);
				})
			{
				Describe = async input =>
				{
					var (id, _, core) = await Page(input);
					return $"{GetString(input, "action") switch { "back" => "Go back", "forward" => "Go forward", _ => "Reload" }} in tab {id} ({Host(core)})";
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

	private static Dictionary<string, JsonElement> Props(params (string Name, JsonElement Schema)[] properties) =>
		properties.ToDictionary(p => p.Name, p => p.Schema);

	// tool input isn't validated upstream (it streams in as the model writes it), so check it here
	internal static int GetInt(IReadOnlyDictionary<string, JsonElement> input, string name)
	{
		if (input.TryGetValue(name, out var value))
		{
			if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
			if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
		}
		throw new AiToolException($"'{name}' must be an integer.");
	}

	internal static string GetString(IReadOnlyDictionary<string, JsonElement> input, string name, bool allowEmpty = false) =>
		input.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && (allowEmpty || text.Length > 0)
			? text
			: throw new AiToolException($"'{name}' must be a {(allowEmpty ? "" : "non-empty ")}string.");

	private static bool GetBool(IReadOnlyDictionary<string, JsonElement> input, string name) =>
		input.TryGetValue(name, out var value) && (value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && value.GetString() == "true"));

	private static string Truncate(string text, int max) => text.Length > max ? text[..max] + $"\n…({text.Length - max:N0} more characters cut)" : text;
	#endregion
}

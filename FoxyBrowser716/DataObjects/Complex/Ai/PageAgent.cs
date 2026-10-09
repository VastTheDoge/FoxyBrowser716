using System.Globalization;
using System.Threading;
using Microsoft.Web.WebView2.Core;

namespace FoxyBrowser716.DataObjects.Complex.Ai;

/// <summary>
/// Reads and drives a web page for the assistant. A script installed into the page renders it as text with
/// interactive elements marked inline (<c>[e12 button "Add to cart"]</c>) and keeps a ref → element map, so
/// the model can say "click e12". Clicks, typing and keys go through the DevTools protocol, so the page
/// receives real (trusted) input events rather than synthetic ones.
/// </summary>
internal static class PageAgent
{
	/// <summary>Bump when the script changes: pages that already have an older copy reinstall it.</summary>
	private const int Version = 1;

	// Runs in the page's main world. Refs are stable for an element's lifetime (WeakMap), so re-reading a page
	// keeps the same ids; a navigation starts a fresh map. Nothing here sends data anywhere.
	private const string Script = """
		if (window.__foxyAgent?.v !== VERSION) Object.defineProperty(window, '__foxyAgent', { configurable: true, value: (() => {
		  const refs = new Map(), ids = new WeakMap();
		  let next = 1;
		  const refOf = el => { let id = ids.get(el); if (!id) { id = 'e' + next++; ids.set(el, id); } refs.set(id, new WeakRef(el)); return id; };
		  const byRef = id => { const el = refs.get(String(id).trim())?.deref(); return el && el.isConnected ? el : null; };
		  const clean = s => (s || '').replace(/\s+/g, ' ').trim();
		  const cut = (s, n) => s.length > n ? s.slice(0, n - 1) + '…' : s;
		  const q = s => JSON.stringify(s);
		  const SKIP = new Set(['script', 'style', 'noscript', 'template', 'head', 'meta', 'link', 'svg', 'canvas', 'object', 'embed', 'audio', 'video', 'map']);
		  const ROLES = new Set(['button', 'link', 'checkbox', 'radio', 'switch', 'tab', 'menuitem', 'menuitemcheckbox', 'menuitemradio', 'option', 'combobox', 'textbox', 'searchbox', 'slider', 'treeitem']);
		  const VALUE_ROLES = new Set(['textbox', 'textarea', 'searchbox', 'combobox', 'editable']);
		  const CHECK_ROLES = new Set(['checkbox', 'radio', 'switch', 'menuitemcheckbox', 'menuitemradio']);
		  const css = el => el.ownerDocument.defaultView.getComputedStyle(el);
		  const sensitive = el => (el.localName === 'input' && el.type === 'password') ||
		    (el.getAttribute('autocomplete') || '').split(/\s+/).some(t => t.startsWith('cc-'));
		  const shortUrl = href => { try { const u = new URL(href, location.href); return u.origin === location.origin ? u.pathname + u.search + u.hash : u.href; } catch { return href; } };

		  function labelOf(el) {
		    const aria = clean(el.getAttribute('aria-label')); if (aria) return aria;
		    const by = el.getAttribute('aria-labelledby');
		    if (by) { const t = clean(by.split(/\s+/).map(id => el.ownerDocument.getElementById(id)?.innerText || '').join(' ')); if (t) return t; }
		    if (el.labels?.length) { const t = clean([...el.labels].map(l => l.innerText).join(' ')); if (t) return t; }
		    for (const a of ['placeholder', 'title', 'alt', 'name']) { const v = clean(el.getAttribute(a)); if (v) return v; }
		    return '';
		  }
		  function textOf(el) {
		    let t = clean(el.innerText) || labelOf(el);
		    if (!t) t = clean(el.querySelector('img[alt]')?.alt);
		    return t;
		  }
		  function roleOf(el) {
		    const n = el.localName, role = (el.getAttribute('role') || '').split(' ')[0];
		    if (n === 'a' && el.hasAttribute('href')) return 'link';
		    if (n === 'button' || n === 'summary') return 'button';
		    if (n === 'select') return 'select';
		    if (n === 'textarea') return 'textarea';
		    if (n === 'input') {
		      const t = (el.getAttribute('type') || 'text').toLowerCase();
		      if (t === 'hidden') return null;
		      if (['button', 'submit', 'reset', 'image'].includes(t)) return 'button';
		      if (t === 'checkbox' || t === 'radio') return t;
		      if (t === 'password') return 'password';
		      if (t === 'file') return 'file';
		      if (t === 'range') return 'slider';
		      return 'textbox';
		    }
		    if (el.isContentEditable && !el.parentElement?.isContentEditable) return 'editable';
		    return ROLES.has(role) ? role : null;
		  }
		  function control(el, role) {
		    let s = `[${refOf(el)} ${role}`;
		    if (el.localName === 'input' && role === 'textbox' && el.type && el.type !== 'text') s += `:${el.type}`;
		    const label = ['link', 'button', 'tab', 'menuitem', 'option', 'treeitem'].includes(role) ? textOf(el) : labelOf(el);
		    if (label) s += ' ' + q(cut(label, 200));
		    if (role === 'link') {
		      const href = el.getAttribute('href') || '';
		      if (href && href !== '#' && !href.startsWith('javascript:')) s += ' → ' + cut(shortUrl(el.href || href), 150);
		    } else if (CHECK_ROLES.has(role)) {
		      const on = 'checked' in el ? el.checked : el.getAttribute('aria-checked') === 'true';
		      s += on ? ' (checked)' : ' (unchecked)';
		    } else if (role === 'select') {
		      const opts = [...el.options];
		      s += ' = ' + q(opts.filter(o => o.selected).map(o => clean(o.text)).join(', '));
		      s += ' options: ' + opts.slice(0, 25).map(o => clean(o.text)).join(' | ') + (opts.length > 25 ? ` | …${opts.length - 25} more` : '');
		    } else if (VALUE_ROLES.has(role)) {
		      const v = role === 'editable' ? clean(el.innerText) : ('value' in el ? el.value : el.getAttribute('aria-valuetext') || '');
		      if (v) s += ' = ' + q(cut(v, 300));
		      if (sensitive(el)) s += ' (payment field)';
		    } else if (role === 'slider' && 'value' in el) s += ' = ' + el.value;
		    if (el.disabled || el.getAttribute('aria-disabled') === 'true') s += ' (disabled)';
		    const expanded = el.getAttribute('aria-expanded');
		    if (expanded) s += expanded === 'true' ? ' (expanded)' : ' (collapsed)';
		    return s + ']';
		  }
		  const describe = el => { const role = roleOf(el); return role ? control(el, role) : `[${refOf(el)} clickable ${q(cut(clean(el.innerText) || labelOf(el), 120))}]`; };

		  function render(root) {
		    const out = [];
		    const kids = node => {
		      if (node.localName === 'slot') { const a = node.assignedNodes({ flatten: true }); return a.length ? a : node.childNodes; }
		      return node.shadowRoot ? node.shadowRoot.childNodes : node.childNodes;
		    };
		    const children = node => { for (const k of kids(node)) walk(k); };
		    function walk(node) {
		      if (node.nodeType === 3) {
		        const p = node.parentElement;
		        if (p && css(p).visibility !== 'visible') return;
		        const t = node.data.replace(/\s+/g, ' ');
		        if (t.trim() || t === ' ') out.push(t);
		        return;
		      }
		      if (node.nodeType !== 1) return;
		      const el = node, n = el.localName;
		      if (SKIP.has(n)) return;
		      const s = css(el);
		      if (s.display === 'none') return;
		      const shown = s.visibility === 'visible';
		      if (n === 'br') { out.push('\n'); return; }
		      if (n === 'hr') { out.push('\n---\n'); return; }
		      if (n === 'img') { const alt = clean(el.alt); if (alt && shown) out.push(` [image: ${cut(alt, 150)}] `); return; }
		      if (n === 'iframe' || n === 'frame') {
		        let doc = null; try { doc = el.contentDocument; } catch { }
		        if (doc?.body) { out.push('\n[frame]\n'); walk(doc.body); out.push('\n[end of frame]\n'); }
		        else { const r = el.getBoundingClientRect(); if (r.width >= 50 && r.height >= 50) out.push(`\n[frame from another site, can't read: ${cut(clean(el.title) || shortUrl(el.src || ''), 80)}]\n`); }
		        return;
		      }
		      if (n === 'pre') { out.push('\n```\n' + el.innerText.replace(/\n+$/, '') + '\n```\n'); return; }
		      const role = shown ? roleOf(el) : null;
		      const block = !/^(inline|contents|table-cell)/.test(s.display);
		      if (role) { out.push(block ? '\n' : ' ', control(el, role), block ? '\n' : ' '); return; }
		      const h = /^h([1-6])$/.exec(n);
		      if (h || n === 'li' || n === 'tr') {
		        // rendered on their own, so empty ones (closed menus) disappear and rows/headings stay on one line
		        const mark = out.length;
		        children(el);
		        let inner = out.splice(mark).join('').trim();
		        if (h || n === 'tr') inner = inner.replace(/\s*\n\s*/g, ' ');
		        if (!inner || inner === '|') return;
		        out.push(h ? `\n${'#'.repeat(+h[1])} ${inner}\n` : n === 'li' ? `\n- ${inner}\n` : `\n| ${inner}\n`);
		        return;
		      }
		      if (n === 'td' || n === 'th') { children(el); out.push(' | '); return; }
		      // something the page made clickable without saying so (cursor: pointer set here, not inherited)
		      if (shown && s.cursor === 'pointer' && !(el.parentElement && css(el.parentElement).cursor === 'pointer')) out.push(` [${refOf(el)} clickable] `);
		      const pad = block ? '\n' : s.display.startsWith('inline-') ? ' ' : '';
		      out.push(pad); children(el); out.push(pad);
		    }
		    walk(root);

		    // one element per line, no blank lines except before headings: blank lines cost tokens and say nothing
		    const lines = [];
		    let fence = false;
		    for (let line of out.join('').split('\n')) {
		      if (line === '```') fence = !fence;
		      if (!fence) {
		        line = line.replace(/[ \t\u00a0]+/g, ' ').trim();
		        if (!line) continue;
		        if (line.startsWith('#') && lines.length) lines.push('');
		      }
		      lines.push(line);
		    }
		    return lines.join('\n').slice(0, 600000);
		  }

		  // later parts of a long read reuse the render, unless something was done to the page since
		  let cache = null;
		  const page = fresh => {
		    if (!fresh && cache && cache.url === location.href && performance.now() - cache.at < 20000) return cache.text;
		    const text = render(document.body || document.documentElement);
		    cache = { text, url: location.href, at: performance.now() };
		    return text;
		  };
		  const changed = () => { cache = null; };
		  const scrolled = () => {
		    const max = Math.max(0, document.documentElement.scrollHeight - innerHeight);
		    return { scrollY: Math.round(scrollY), percent: max ? Math.min(100, Math.round(scrollY / max * 100)) : 100 };
		  };
		  const gone = { error: 'stale' };

		  return {
		    v: VERSION,
		    read(start, max) {
		      const text = page(!start);
		      start = Math.max(0, Math.min(start | 0, text.length));
		      let end = Math.min(text.length, start + max);
		      if (end < text.length) { const nl = text.lastIndexOf('\n', end); if (nl > start + max / 2) end = nl; }
		      return { title: document.title, url: location.href, total: text.length, start, end, text: text.slice(start, end), loading: document.readyState !== 'complete' };
		    },
		    find(query) {
		      const text = page(true), needle = String(query).toLowerCase(), matches = [];
		      let offset = 0, count = 0;
		      for (const line of text.split('\n')) {
		        const at = line.toLowerCase().indexOf(needle);
		        if (at >= 0 && count++ < 40) {
		          // long paragraphs: show the part around the match
		          const from = line.length > 400 ? Math.max(0, Math.min(at - 150, line.length - 400)) : 0;
		          matches.push({ offset, line: (from > 0 ? '…' : '') + line.slice(from, from + 400) + (from + 400 < line.length ? '…' : '') });
		        }
		        offset += line.length + 1;
		      }
		      return { title: document.title, url: location.href, total: text.length, count, matches };
		    },
		    describe(ref) { const el = byRef(ref); return el ? { desc: describe(el) } : gone; },
		    // scrolls the element into view and returns its center in top-level viewport CSS pixels
		    target(ref) {
		      const el = byRef(ref); if (!el) return gone;
		      changed();
		      el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
		      const r = el.getBoundingClientRect();
		      if (r.width < 1 || r.height < 1) return { error: 'invisible', desc: describe(el) };
		      let x = r.left + r.width / 2, y = r.top + r.height / 2;
		      for (let w = el.ownerDocument.defaultView; w !== window && w.frameElement; w = w.parent) {
		        const f = w.frameElement.getBoundingClientRect(); x += f.left; y += f.top;
		      }
		      if (el.ownerDocument === document) {
		        const hit = document.elementFromPoint(x, y);
		        if (hit && hit !== el && !el.contains(hit) && !hit.contains(el)) {
		          let cover = hit; while (cover.parentElement && !roleOf(cover) && cover.parentElement !== document.body) cover = cover.parentElement;
		          return { error: 'covered', desc: describe(el), cover: describe(cover) };
		        }
		      }
		      return { x, y, desc: describe(el) };
		    },
		    focus(ref) {
		      const el = byRef(ref); if (!el) return gone;
		      changed();
		      if (sensitive(el)) return { error: 'sensitive', desc: describe(el) };
		      if (el.disabled || el.readOnly) return { error: 'disabled', desc: describe(el) };
		      el.scrollIntoView({ block: 'center', behavior: 'instant' });
		      el.focus();
		      try { if (typeof el.select === 'function') el.select(); } catch { }
		      if (el.isContentEditable) { const r = document.createRange(); r.selectNodeContents(el); getSelection().removeAllRanges(); getSelection().addRange(r); }
		      const active = el.ownerDocument.activeElement;
		      return { focused: active === el || el.contains(active), desc: describe(el) };
		    },
		    select(ref, option) {
		      const el = byRef(ref); if (!el) return gone;
		      changed();
		      if (el.localName !== 'select') return { error: 'not a select', desc: describe(el) };
		      const want = String(option).toLowerCase(), opts = [...el.options];
		      const o = opts.find(o => clean(o.text).toLowerCase() === want || o.value === option) || opts.find(o => clean(o.text).toLowerCase().includes(want));
		      if (!o) return { error: 'no such option', options: opts.map(o => clean(o.text)) };
		      // the prototype setter, so frameworks that wrap .value (React) see the change
		      Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value').set.call(el, o.value);
		      el.dispatchEvent(new Event('input', { bubbles: true }));
		      el.dispatchEvent(new Event('change', { bubbles: true }));
		      return { selected: clean(o.text), desc: describe(el) };
		    },
		    scroll(ref, direction) {
		      changed();
		      if (ref) { const el = byRef(ref); if (!el) return gone; el.scrollIntoView({ block: 'center', behavior: 'instant' }); return scrolled(); }
		      const h = innerHeight * 0.85, root = document.documentElement;
		      ({ up: () => scrollBy({ top: -h, behavior: 'instant' }), down: () => scrollBy({ top: h, behavior: 'instant' }),
		         top: () => scrollTo({ top: 0, behavior: 'instant' }), bottom: () => scrollTo({ top: root.scrollHeight, behavior: 'instant' }) })[direction]?.();
		      return scrolled();
		    },
		  };
		})() });
		""";

	public static Task<JsonElement> Read(CoreWebView2 core, int start, int maxChars) => Call(core, "read", start, maxChars);
	public static Task<JsonElement> Find(CoreWebView2 core, string query) => Call(core, "find", query);
	public static Task<JsonElement> Describe(CoreWebView2 core, string elementRef) => Call(core, "describe", elementRef);
	public static Task<JsonElement> Target(CoreWebView2 core, string elementRef) => Call(core, "target", elementRef);
	public static Task<JsonElement> Focus(CoreWebView2 core, string elementRef) => Call(core, "focus", elementRef);
	public static Task<JsonElement> Select(CoreWebView2 core, string elementRef, string option) => Call(core, "select", elementRef, option);
	public static Task<JsonElement> Scroll(CoreWebView2 core, string? elementRef, string? direction) => Call(core, "scroll", elementRef, direction);

	private static async Task<JsonElement> Call(CoreWebView2 core, string method, params object?[] args)
	{
		var arguments = string.Join(", ", args.Select(a => JsonSerializer.Serialize(a)));
		var script = "(() => {\n" + Script.Replace("VERSION", Version.ToString(CultureInfo.InvariantCulture)) +
			$"\nreturn window.__foxyAgent.{method}({arguments});\n}})()";

		var result = await core.ExecuteScriptWithResultAsync(script);
		if (!result.Succeeded)
			throw new AiToolException($"The page script failed: {result.Exception?.Message ?? "unknown error"}");
		return JsonDocument.Parse(result.ResultAsJson).RootElement.Clone();
	}

	/// <summary>Waits (up to <paramref name="timeout"/>) for the page to finish loading, so a read right after a navigation sees the new page.</summary>
	public static async Task WaitForLoad(CoreWebView2 core, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				if (await core.ExecuteScriptAsync("document.readyState") == "\"complete\"") return;
			}
			catch (Exception)
			{
				// mid-navigation; try again
			}
			await Task.Delay(250);
		}
	}

	#region Input (DevTools protocol)
	private static Task Cdp(CoreWebView2 core, string method, object parameters) =>
		core.CallDevToolsProtocolMethodAsync(method, JsonSerializer.Serialize(parameters)).AsTask();

	/// <summary>A real left click at viewport CSS pixel coordinates.</summary>
	public static async Task Click(CoreWebView2 core, double x, double y)
	{
		await Cdp(core, "Input.dispatchMouseEvent", new { type = "mouseMoved", x, y });
		await Cdp(core, "Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", buttons = 1, clickCount = 1 });
		await Cdp(core, "Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", buttons = 0, clickCount = 1 });
	}

	/// <summary>Types into the focused element, replacing its selection, as if pasted by the user.</summary>
	public static Task InsertText(CoreWebView2 core, string text) => Cdp(core, "Input.insertText", new { text });

	private static readonly Dictionary<string, (string Key, string Code, int VirtualKey, string? Text)> Keys = new(StringComparer.OrdinalIgnoreCase)
	{
		["Enter"] = ("Enter", "Enter", 13, "\r"),
		["Escape"] = ("Escape", "Escape", 27, null),
		["Tab"] = ("Tab", "Tab", 9, null),
		["Backspace"] = ("Backspace", "Backspace", 8, null),
		["Delete"] = ("Delete", "Delete", 46, null),
		["Space"] = (" ", "Space", 32, " "),
		["ArrowUp"] = ("ArrowUp", "ArrowUp", 38, null),
		["ArrowDown"] = ("ArrowDown", "ArrowDown", 40, null),
		["ArrowLeft"] = ("ArrowLeft", "ArrowLeft", 37, null),
		["ArrowRight"] = ("ArrowRight", "ArrowRight", 39, null),
		["PageUp"] = ("PageUp", "PageUp", 33, null),
		["PageDown"] = ("PageDown", "PageDown", 34, null),
		["Home"] = ("Home", "Home", 36, null),
		["End"] = ("End", "End", 35, null),
	};

	public static IEnumerable<string> KeyNames => Keys.Keys;

	/// <summary>Presses and releases a named key (see <see cref="KeyNames"/>) on whatever has focus.</summary>
	public static async Task PressKey(CoreWebView2 core, string name)
	{
		if (!Keys.TryGetValue(name.Trim(), out var k))
			throw new AiToolException($"Unknown key '{name}'. Use one of: {string.Join(", ", KeyNames)}.");

		if (k.Text is null)
			await Cdp(core, "Input.dispatchKeyEvent", new { type = "rawKeyDown", key = k.Key, code = k.Code, windowsVirtualKeyCode = k.VirtualKey });
		else
			await Cdp(core, "Input.dispatchKeyEvent", new { type = "keyDown", key = k.Key, code = k.Code, windowsVirtualKeyCode = k.VirtualKey, text = k.Text });
		await Cdp(core, "Input.dispatchKeyEvent", new { type = "keyUp", key = k.Key, code = k.Code, windowsVirtualKeyCode = k.VirtualKey });
	}

	/// <summary>
	/// Evaluates JavaScript in the page and returns the result as JSON. Promises are awaited, so <c>async</c>
	/// code works. Gives up waiting after <paramref name="timeout"/> (the script itself keeps running).
	/// </summary>
	public static async Task<string> Evaluate(CoreWebView2 core, string expression, TimeSpan timeout)
	{
		var call = core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
		{
			expression,
			awaitPromise = true,
			returnByValue = true,
			userGesture = true,
		})).AsTask();

		if (await Task.WhenAny(call, Task.Delay(timeout)) != call)
			throw new AiToolException($"The script didn't finish within {timeout.TotalSeconds:0} seconds.");

		var response = JsonDocument.Parse(await call).RootElement;
		if (response.TryGetProperty("exceptionDetails", out var exception))
		{
			var message = exception.TryGetProperty("exception", out var e) && e.TryGetProperty("description", out var d)
				? d.GetString()
				: exception.TryGetProperty("text", out var t) ? t.GetString() : "unknown error";
			throw new AiToolException($"The script threw: {message}");
		}

		var value = response.GetProperty("result");
		return value.TryGetProperty("value", out var v) ? v.GetRawText()
			: value.TryGetProperty("type", out var type) && type.GetString() == "undefined" ? "undefined"
			: value.TryGetProperty("description", out var description) ? description.GetString() ?? "" : value.GetRawText();
	}
	#endregion
}

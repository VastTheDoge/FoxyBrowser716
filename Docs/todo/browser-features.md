# Browser features — gaps vs other browsers

From a feature review (2026-10-12) against Chrome/Edge/Firefox. Ordered roughly by priority; the keyboard-shortcut
pass is deliberately **last**.

## Design rules (from the user)

- **Pins vs bookmarks.** Pins are the prominent, one-click list ("I use this site every other day"). Bookmarks are an
  out-of-the-way archive ("if I ever need to decode steganography again, this is what to use"). Bookmark work must not
  add always-visible UI — no bookmarks bar.
- **Don't reinvent what an extension already does well.** Ad blocking = uBlock Origin. Passwords = a password-manager
  extension. Make sure those work; don't build built-in replacements.

## 1. Popups lose `window.opener` (known bug)

`WebviewTab.CoreWebView2OnNewWindowRequested` opens every `window.open` as a fresh tab via `TabManager.AddTab(args.Uri)`
and never sets `args.NewWindow`, so the popup has no opener. OAuth popups ("Sign in with Google/Microsoft/Apple") and
payment/3-D Secure popups that `postMessage` back to the opener fail.

- [ ] Take a deferral, create the tab **without navigating it**, `EnsureCoreWebView2Async` with the same environment
      (and the InPrivate controller options in a private instance), set `args.NewWindow = tab.Core.CoreWebView2`,
      `args.Handled = true`, complete the deferral. WebView2 then navigates the new view itself.
- [ ] Popups open as a **new tab** (the user's preference), so `args.WindowFeatures` (requested size/position) can stay
      ignored. A small popup window like Chrome's might be fine later, as an option, not the default.
- [ ] Test: Google sign-in popup on a third-party site, a PayPal checkout popup, and the same in a private instance.

## 2. Tab audio: indicator + volume

`NewWebviewTab.IsPlayingAudio` / `IsMuted` (`DataObjects/WebviewTab/WTMain.cs`) are unused stubs.

- [ ] Speaker icon on the tab card while `CoreWebView2.IsDocumentPlayingAudio` (`IsDocumentPlayingAudioChanged`).
- [ ] Hovering the icon shows a volume slider for that tab; click toggles mute (`CoreWebView2.IsMuted`).
- [ ] Note: WebView2 only has mute, no per-tab volume API. Volume would have to be injected script (scale
      `<audio>`/`<video>` `volume`, wrap Web Audio output in a gain node) via `AddScriptToExecuteOnDocumentCreatedAsync`.
      Decide how far to go (media elements only vs Web Audio too) when implementing.

## 3. Reopen closed tabs — dropped

History covers it: it lists newest first (`HistoryManager` orders by `LastVisited` descending), so the page you just
closed is the top entry. No separate recently-closed list or Ctrl+Shift+T.

## 4. Security indicator / site info

- [ ] Padlock / "Not secure" in the address bar.
- [ ] Click it → site-info popover: connection + certificate details, and this site's permissions (today only in
      Settings > Site Permissions), cookies/site data for the site.

## 5. Tab sleeping (performance setting)

Suspend code exists but is dead: `if (false) //TODO` in `TabManager.SwapActiveTabTo`; `WTPerformance` is empty.

- [ ] Performance setting that trades memory for responsiveness, e.g. Off / sleep background tabs after N minutes /
      sleep immediately. Never sleep tabs playing audio (the existing code already checks this).
- [ ] Consider restoring session tabs lazily (load on first activation) under the same setting.

## 6. Import from other browsers

Only bookmarks have a real interchange file. Everything else is imported by reading the other browser's profile
files directly, which is what Edge's and Firefox's importers do.

| Data | Standard file? | How to import |
|---|---|---|
| Bookmarks | **Yes:** Netscape bookmark file (`bookmarks.html`); every browser imports/exports it. Has folders; Firefox's export adds a `TAGS` attribute | Read the HTML file. Or directly: Chromium `Bookmarks` (plain JSON), Firefox `places.sqlite` (`moz_bookmarks`) |
| Passwords | De facto: CSV (`name,url,username,password`) | Not ours — extension (§8) |
| History | **No** | Chromium `History` (SQLite: `urls`, `visits`; time = µs since 1601-01-01). Firefox `places.sqlite` (`moz_places`, `moz_historyvisits`; time = µs since 1970) |
| Open tabs / session | **No** | Chromium `Sessions/` is an undocumented binary format; Firefox `sessionstore-backups/recovery.jsonlz4` is LZ4 JSON with a `mozLz40` header. Likely not worth it |
| Extensions | No | Read the installed extension IDs from the profile and reinstall them from the store via the extension loader |
| Cookies / sign-ins | No | Not feasible from Chrome/Edge: cookies are app-bound encrypted (Chrome 127+), only that browser can decrypt them |
| Downloads list, settings, site permissions | No | Not worth importing |

Profile locations: Chrome `%LOCALAPPDATA%\Google\Chrome\User Data\<profile>\`, Edge
`%LOCALAPPDATA%\Microsoft\Edge\User Data\<profile>\` (Brave/Vivaldi/Opera use the same Chromium layout under their own
folders), Firefox `%APPDATA%\Mozilla\Firefox\Profiles\<profile>\` (list in `profiles.ini`). The SQLite files are locked
while that browser runs: copy them to a temp file first, or ask the user to close it.

- [ ] Bookmark import/export via the Netscape HTML file. Map its folders (and Firefox `TAGS`) onto §7.
- [ ] Import from Chrome / Edge / other Chromium browsers / Firefox: pick the browser and profile, then choose
      bookmarks, history, extensions. Import into a chosen instance.
- [ ] History import needs a SQLite reader (`Microsoft.Data.Sqlite`), which also fits the web-ui todo about moving
      FoxyBrowser's own history to SQLite.
- [ ] History export: no standard exists, so offer CSV (url, title, last visited, visit count) or JSON.
- [ ] Export/import a whole FoxyBrowser instance (its JSON files zipped), for moving to another PC.

## 7. Bookmarks (out of the way — see design rules)

Adding already works: the sidebar's bookmark card toggles the current page (`NewLeftBar.BookmarkCard_OnOnClick`).
`WebsiteInfo` has no folder field, so bookmarks are one flat list.

- [ ] Folders or tags inside the Bookmarks panel, so a long archive stays findable. Pick one before §6's bookmark
      import: Chrome files only have folders, Firefox has both.

## 8. Passwords

- [ ] Don't build a manager (WebView2 has no API to list/edit saved passwords anyway). Point users at a
      password-manager extension, e.g. in the description of the "Offer to save passwords" setting.

## 9. Menus and settings

- [ ] ☰ menu: Find in page, Print, Save page as, Zoom (once §11 works), Share.
- [ ] Tab right-click menu (today: Close, Duplicate, group moves): Reload, Pin, Mute, Close others, Close tabs to the
      right, Move to new window, Bookmark.
- [ ] Startup setting: continue where I left off / open the home page / open specific pages. Today session restore
      always runs off the 5 s backup timer (`BackupManagement`).
- [ ] Proxy and DNS-over-HTTPS settings (environment-wide, via `AdditionalBrowserArguments` in `TabManager`).

## 10. Keyboard shortcuts (do this last)

Only WebView2's built-ins work today (Ctrl+F, Ctrl+P, F5, …, behind the "Browser keyboard shortcuts" setting). No
app-level shortcuts exist; the only key handling is Enter in the address bar.

- [ ] Ctrl+T, Ctrl+W, Ctrl+Tab / Ctrl+Shift+Tab, Ctrl+1–9, Ctrl+L / Alt+D, Ctrl+D, Ctrl+H, Ctrl+J,
      F11, Alt+←/→, Ctrl+R.
- [ ] Must work while the WebView2 has focus (keys go to Chromium first), not just when XAML has focus.

## 11. Zoom (research spike)

Ctrl+scroll zoom works (WebView2 setting). There is no zoom indicator, no +/−/reset UI and no per-site memory. A
previous attempt (~late 2025) to set zoom from code went down a rabbit hole.

- [ ] Find out what the WinUI 3 `WebView2` control exposes for zoom (`Scripts/GetNugetApi.ps1 -Dll` against the built
      copy) before designing UI; record the answer in an architecture doc so it isn't re-researched.

## 12. Ad blocking = uBlock Origin

- [ ] Check uBlock Origin installs from the Chrome Web Store and filters pages through the extension loader. If the
      WebView2 runtime no longer runs MV2 extensions, check uBlock Origin Lite (MV3) instead.

## 13. Error pages (keep Edge's)

WebView2's built-in (Edge) error pages are good. Only replace them if the replacement shows **at least** everything
Edge shows — e.g. the certificate issuer on an invalid-certificate page, which Chrome and Firefox don't show.

## Nice-to-have

- [ ] Split view (two tabs side by side).
- [ ] Reader mode.
- [ ] Page translation (WebView2 doesn't ship Edge's translator).
- [ ] Install site as app (PWA).
- [ ] Screenshot tool, QR code for the page, tab hover previews.
- [ ] Cross-device sync.

# Web UI: themed engine UI, site permissions, downloads, history, private windows

WebView2 ships its own Chromium UI for the page context menu, permission prompts, `alert`/`confirm`/`prompt`,
HTTP sign-in and the download flyout. None of it follows FoxyBrowser themes, so each is replaced by themed UI
drawn by the window that hosts the tab. Every replacement has a setting to fall back to the engine UI.

## Flow

```
WebviewTab (one WebView2)            MainWindow (MainWindow.WebUi.cs)          Controls/WebUi
  CoreWebView2Initialization  ──►  OnTabContextMenuRequested  ─────────►  FContextMenu (UseSolidBackground)
  subscribes and forwards:         OnTabPermissionRequested   ─┐
    ContextMenuRequested           OnTabScriptDialogOpening   ─┼──────►  WebPromptHost ─► WebPromptCard
    PermissionRequested            OnTabBasicAuthentication…  ─┘
    ScriptDialogOpening            OnTabDownloadStarting      ─────────►  DownloadManager.Track + DownloadsPanel
    BasicAuthenticationRequested   ShowHistoryPanel / ShowToast ───────►  HistoryPanel, ToastHost
    DownloadStarting
```

- `TabManager.Window` is the hosting `MainWindow` (passed to `TabManager.Create`), which is how a tab reaches it.
- Each handler that shows UI takes the event's **deferral**, so the page waits while the user decides, and
  completes it exactly once (guard flag) — on a button, or via `WebPromptSpec.Cancelled` when the tab or window
  closes. Completing a deferral of a closed WebView throws, so cancellation is wrapped.
- Placement: popups are children of `MainWindow.BorderGrid`. The context menu converts
  `ContextMenuRequested.Location` from raw pixels to DIPs (`/ XamlRoot.RasterizationScale`) and then into
  `BorderGrid` space; `FContextMenu.PlaceWithin` keeps it on screen. Prompts are centered at the top of `TabHolder`.

## Prompts (`WebPromptHost`)

Queued **per tab**; only the active tab's oldest prompt is visible (`SetActiveTab` on tab switch), so a
background tab's dialog waits until you switch to it. Prompts never light-dismiss. `WebPromptSpec` describes
title/message/icon, optional text input, optional user+password, optional check box and buttons;
`WebPromptCard` renders it. Script dialogs only reach us while `BrowserSettings.ThemedDialogs` is on, because
`ApplySettings` sets `AreDefaultScriptDialogsEnabled = !ThemedDialogs`.

## Site permissions (`SitePermissionManager`, SitePermissions.json)

Resolution for a `PermissionRequested`:

1. a remembered decision for the request's origin (`scheme://host[:port]`) and kind;
2. else the kind's default from settings (`LocationPermission`, `CameraPermission`, … : Ask/Allow/Block);
3. else prompt (themed: Allow / Block).

Allow/Block answers the page immediately. Remembering is a separate step, controlled by
`RememberPermissionChoices`: **Ask** (default) queues a "Remember this for {site}?" follow-up prompt
(`WebPromptHost.EnqueueNext`, so it shows before the tab's other prompts) with "Just this time" / "Remember";
**Always** saves straight away and shows a toast; **Never** saves nothing. Private windows read remembered
choices but never add to them. Per-kind defaults follow Chrome's: motion sensors and autoplay are allowed,
everything else asks.

`SavesInProfile` is always set to `false` when FoxyBrowser decides, so our list is the source of truth and the
engine keeps asking. Decisions the engine stored itself (older builds, or its own prompt while themed dialogs
are off) are applied by the engine *before* `PermissionRequested` fires; the settings section lists them from
`Profile.GetNonDefaultPermissionSettingsAsync()` with a Reset button (`SetPermissionStateAsync(..., Default)`).

## Downloads (`DownloadManager`, Downloads.json)

`OnTabDownloadStarting` sets `Handled` (hides the engine flyout; the download continues), optionally asks
where to save (`FileSavePicker`; its empty placeholder file is deleted so WebView2 writes to that exact path),
then `DownloadManager.Track` follows the `CoreWebView2DownloadOperation` events. The list persists; anything
still running at shutdown is marked failed on next start (it cannot resume). Downloads started on the Chrome
Web Store page are left to `ExtensionManager`. The profile's `DefaultDownloadFolderPath` follows the
`DownloadFolder` setting (empty = the Downloads known folder).

## History (`HistoryManager`, History.json)

WebView2 does keep Chromium's history inside the profile (it colors visited links), but exposes no API to
read or search it — only `ClearBrowsingDataAsync(BrowsingHistory)`. So FoxyBrowser records its own, and
"Clear history" clears both.

One entry per URL (title, favicon, first/last visit, visit count). Recorded from `NavigationCompleted`
(success) and from same-document `SourceChanged` (single-page apps); title/favicon arrive later and update the
entry. Only http/https/file URLs, never in private windows, and only while `SaveHistory` is on. Expired entries
(`HistoryRetentionDays`) are pruned at startup only — pruning on change would fire per keystroke of the
number box. Used by the history panel and the address-bar suggestions (`Search` ranks host-prefix matches,
visit count and recency).

`FoxyAutoSaverLockedList<T>` backs history, downloads and permissions: the UI thread mutates under a lock and
the auto-saver serializes a snapshot, unlike `FoxyAutoSaverList<T>` which enumerates the live collection from
the timer thread. `FoxyAutoSaver` itself locks its queue bookkeeping (requests come from the UI thread, saves
run on its timer thread) and logs a failed save instead of letting it escape the timer callback.

## Private windows

A window is private when opened with "New private window" (or "Open link in private window"), or when its
instance has `BrowserSettings.PrivateBrowsing` on — then every window of that instance is private (applies
to windows opened after the change; a WebView2 cannot switch modes). `Instance.CreateWindow(isPrivate)` →
`MainWindow.IsPrivate` → `TabManager.IsPrivate` → tabs call
`EnsureCoreWebView2Async(env, options)` with `IsInPrivateModeEnabled`. All private tabs share one
off-the-record session per instance (each instance has its own WebView2 user data folder) that is discarded
when the last one closes. In a private window: no history, a session-only `DownloadManager(null)`, permission
choices are read but never remembered, extensions are not loaded (the Extensions menu says so), the top bar shows an incognito icon, and `BackupManagement` skips the
window (and a backup holding only private windows counts as nothing to restore, so startup still opens a
window). Links opened from other apps go to a normal window when the instance has one. Tabs cannot be dragged between private and normal
windows (`TabManager.CanAcceptTabsFrom`, the same check that keeps tabs inside their instance).

## Settings

`BrowserSettings` properties with `[SettingInfo]` become editors via reflection (`GetSettingControls`): bool,
int (with `MinValue`/`MaxValue`), decimal, string (folder/file picker when `PickerEnabled`), enum (every
member becomes an option), Color, and `ThemedUserControl` fields for custom sections (shown with their title
and description by `CustomControlSettingControl`). Categories render in `SettingsCategory` order.
`MainWindow.OnSettingsChanged` calls `WebviewTab.ApplySettings()` on every tab for any change, so engine
settings apply live.

## Extensions

`ExtensionsController` (Settings > Extensions) drives `ExtensionManager`: on/off is stored as folder names in
`BrowserSettings.DisabledExtensions` and re-applied whenever a WebView loads extensions; updates re-download
the CRX from the store named by the manifest's `update_url` and reinstall only if the version is newer;
"Load unpacked" copies a folder into the instance's Extensions folder. Installs and updates are staged: the CRX
is unpacked and validated in the instance Cache folder, the old copy is moved aside, and it is put back if the
new one fails to load. Every operation that changes the set (`SetupExtensionSupport` per tab, install, update,
remove, on/off) runs under one per-instance lock and re-reads the list inside it, because
`SetupExtensionSupport` replaces it. Folders with an unreadable manifest are skipped (logged), never fatal.

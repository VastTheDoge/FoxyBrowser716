# Tabs, popups, audio and sessions

How tabs are created and what the browser does around them: popups, audio, the tab menu, what opens at startup,
and where the data folder lives.

## Popups (`TabManager.OpenNewWindowAsTab`)

`window.open` / `target=_blank` (`NewWindowRequested`, also from the extension popup) opens a **tab**, not a window,
and keeps `window.opener`: the handler takes a deferral, creates a tab with no URL, waits for its core only
(`WebviewTab.CoreTask`, not the slower `InitializeTask` that also loads extensions), sets `args.NewWindow` to it
and completes the deferral; WebView2 then navigates it. OAuth and payment popups need this to `postMessage` back.
If that fails (e.g. the opener is in another profile) the tab is navigated plainly. The popup remembers its
`OpenerTabId`; closing it while active goes back to the opener. Requested popup size/position is ignored.

`WebviewTab` creation is split: `CreateCore()` (engine, settings, events) then `CoreWebView2Initialization()`
(extensions, process priority, first navigation).

## Audio

`WebviewTab.IsPlayingAudio` / `IsMuted` follow `IsDocumentPlayingAudioChanged` / `IsMutedChanged`. The tab card
(`NewTabCard.Tab`, set from the tab list templates) shows a small speaker badge on the favicon and, when the bar is
wide, a button: click toggles mute, hover opens a volume slider.

WebView2 has no per-tab volume. `WebviewTab.SetVolume` injects `VolumeScript` (registered for new documents and
run in the open document and every frame from `FrameCreated`): it wraps `HTMLMediaElement.prototype.volume` so
the page still reads back the volume it set while the element plays at that times the tab's volume. Web Audio
output is not scaled. The volume is not saved.

## Tab menu

`WebviewTab.BaseItems`: Close, Close other tabs, Close tabs below (within the tab's group or the ungrouped list),
Reload, Duplicate, Mute/Unmute, Pin/Unpin, Bookmark/Remove bookmark, Move to new window, Move to new group, plus
group moves. Pins and bookmarks go through `Instance.TogglePin/ToggleBookmark` (stores a copy of the page info);
the sidebar's pin/bookmark cards follow the lists' `CollectionChanged`.

The hamburger menu with a tab open also has Find in page (`CoreWebView2.Find`, engine find bar), Print (browser
print dialog), Save page as (`ShowSaveAsUIAsync`) and Share (Windows share sheet via `DataTransferManagerInterop`).

## Startup and session restore (`BackupManagement`)

The open windows/tabs are saved every 5 s (`Backup.json` in the app Cache folder, `ClosedCleanly = false`).
When the last window closes normally it is saved once more with `ClosedCleanly = true`. At startup:

- not closed cleanly (crash, logoff, killed): every window is restored, whatever the settings say;
- closed cleanly: only windows of instances whose `StartupMode` is *Continue where I left off* are restored.

If nothing was restored the primary instance opens a window: the home page, or with *Open specific pages* the
space-separated `StartupPages`. A launch with URLs (link opened from another app) opens those instead.

## Data folder (`FoxyFileManager`)

Two roots. The **default root** (`%AppData%\FoxyBrowser716`; packaged builds see the virtualized copy) always holds
the parts that stay on this PC: `DataFolder.json` (the pointer), the caches (`Cache`, `Instances/<name>/Cache`,
including the session backup) and the engine profiles (`Instances/<name>/WebView2`: cookies are encrypted for
this PC, and Chromium doesn't run reliably from a synced drive). The **data root** holds everything else
(`Instances/<name>/Data`, `Extensions`, top-level `Data`, `AiBudget.json`); it is the default root unless moved.
`BuildFolderPath` sends `WebView2` and `Cache` to the default root, the rest to the data root.

`BrowserSettings.BrowserDataPath` is app-wide despite showing in each instance's settings: it reads and writes
`FoxyFileManager.DataFolderSetting` (`DataFolder.json`: `Path` in use, `PendingPath` chosen; empty = default).
An unrelated non-empty folder gets a `FoxyBrowser716` subfolder; a folder inside the current root (or the other
way round) is refused.

`App.OnLaunched` calls `PrepareDataFolderAsync` right after the single-instance check, before the logger or
anything else loads data:

- If the data root in use is missing (cloud drive not started yet) it waits up to 20 s, then falls back to the
  default root and shows a toast in the first window.
- A pending folder that already holds browser data (an instance `Data` folder, no `.foxy-copying` or
  `.foxy-moved` marker) is used as is: that is the second-PC / sync case, and the data there wins.
- Otherwise the movable parts are copied in (`.foxy-copying` while it runs). When the target is the default root
  marked `.foxy-moved`, its stale movable parts are deleted first, so files removed elsewhere don't come back.
- Leaving the default root marks it `.foxy-moved`. Other folders are never deleted or marked: someone else may
  still use them.
- On failure the pending choice is dropped, the current root stays and the error is logged and toasted.

Root paths are properties, not cached fields, so the switch reaches every later path; static fields elsewhere
that cache paths (`FoxyLogger`, `BackupManagement`) are first touched after it. The move logic was exercised
with a throwaway harness on temp folders (default → cloud → default, sync case, nested refusal, late drive).

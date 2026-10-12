# Tab dragging — waiting on the user's go-ahead

**Don't start until the user says go.** Other chats are editing the same files (`NewLeftBar`,
`NewTabGroupCard`, `MainWindow`, `TabManager`).

How it works today: tabs and groups are dragged with the ListView's built-in OLE drag-and-drop
(`CanDragItems`/`CanReorderItems` in `NewLeftBar.xaml` and `NewTabGroupCard.xaml`), with `DataPackage.Properties`
carrying `DragType`/`DragItem`/`SourceManager`/`SourceGroupId`. A drag that ends with `DropResult.None` tears
off: `TabManager.CreateWindowWithTab`/`CreateWindowWithGroup` opens a window at `GetCursorPos`. Moving between
windows re-creates the tab from its URL (`MoveTabFromWindow` etc.).

## Bugs in the current drag (fix first)

- [ ] **Esc tears off.** `DragItemsCompleted` reports `DropResult.None` for a cancelled drag too (verify), so
      Esc opens a window. On a cancel the left button is still down (`GetAsyncKeyState(VK_LBUTTON)`).
- [ ] **Group move leaks the source tabs.** `TabManager.MoveGroupFromWindow` removes the group from the source
      but never calls `sourceManager.RemoveTab`, so the old WebView2s keep running (audio too) in the source
      window's `TabHolder`. Hit by group tear-off and cross-window group drops.
- [ ] **Cross-window drop onto a group throws.** `MoveTabFromWindowToGroup` defaults `targetIndex = -1`, which
      `MoveTabToGroup` treats as an index → `Tabs.Insert(-1, …)`. Hit by dropping a tab from another window on
      a group card (`NewTabGroupCard.Root_Drop`) or on "+ New Tab Group" (`MoveTabFromWindowToNewGroup`); the new
      tab is left in no list and the source tab is not removed.
- [ ] **Tiny torn-off window.** Requested at 250×100 DIP → clamped to `MinWidth`/`MinHeight` (253×43), a
      title-bar strip with its top-left corner at the cursor. See *Torn-off window bounds*.
- [ ] **Last tab.** Dragging a window's only tab out (no groups) opens a second window and leaves the first
      empty; move the source window instead.
- [ ] `CreateWindowWithGroup` leaves the new window on the home page; activate the group's first tab.
- [ ] `NewLeftBar.TabsList_DragItemsCompleted` throws if `GetCursorPos` fails (inside a drag handler).
- [ ] The tear-off code (`GetCursorPos` P/Invoke + bounds) is copied in three handlers across `NewLeftBar` and
      `NewTabGroupCard`; pull it into one helper.

## Torn-off window bounds (decided)

- [ ] **Near a screen edge → snap.** Left/right edge → that half, corners → quarter, top edge → maximized, of
      the work area of the monitor under the cursor. Emulated on drop while the drag is still OLE; native once
      the live tear-out below lands.
- [ ] **Otherwise → parent window size.** Its normal size (`GetBounds()` already returns the restore bounds
      when maximized/fullscreen), keeping the DIP size if the target monitor's scale differs. Place it so the
      cursor sits over the tab's row in the new window's sidebar (sidebar opened), fully on-screen
      (`MainWindow.ApplyBounds` clamps to the work area). Physical px throughout: `memory/window-pixel-units.md`.
- [ ] Optional: snap-zone preview while dragging (poll the cursor on a timer during the OLE drag, overlay
      with `TransparentWindow`).

## Try: keep the live page when a tab changes window

Today every cross-window move reloads the page (back/forward history, scroll, form input, media lost).

- [ ] Reparent the existing `WebView2` element into the target window instead of re-creating it
      (`RemoveTab(tabId, keepCore: true)` exists for this) and re-point `WebviewTab.TabManager` (its
      `CoreWebView2` handlers call `TabManager.Window.*` when they fire, so they follow).
      **Known risk:** the user's attempt ~1.5 years ago (older Windows App SDK) rendered **black** — WebView2
      keeps a reference to its original window somewhere. Retest on 1.8; check rendering, input/focus,
      DevTools, fullscreen, InPrivate.
- [ ] If it goes black: look for what still points at the old window (hosting HWND / compositor), or host a
      `CoreWebView2CompositionController` ourselves so `ParentWindow` and `RootVisualTarget` can be retargeted
      (large). Otherwise keep reload-by-URL.
- [ ] Either way: `MainWindow.TabManagerOnTabAdded` subscribes lambdas that are never removed (its TODO) —
      a moved tab must drop the old window's handlers.

## Later: Chrome-style live tear-out (separate go-ahead)

Tried before; making the mouse/cursor behave was the problem, which is why the drag is OLE today. Prove the
handoff in a small spike before rewriting anything.

- [ ] Replace the ListView OLE drag (tabs, groups, tabs in groups, "new group" area, group-card drop) with a
      pointer-driven drag: live collection moves + insert line for in-window reorder/group/ungroup.
- [ ] **Detach when the tab leaves the sidebar** (decided), past a small threshold — including over the page.
- [ ] On detach: create the window under the cursor (or move the source window if it's the last tab), then
      hand it to Windows' move loop (`ReleaseCapture` + `WM_SYSCOMMAND SC_MOVE|HTCAPTION`, posted through the
      dispatcher, not called inside the pointer handler). That gives real snap: edges/corners, the Snap Layouts
      flyout, snap assist, FancyZones, monitor-DPI changes.
- [ ] Cursor handoff risks: WinUI pointer capture vs. the Win32 move loop (`PointerCaptureLost` on the source
      card), button released before the window is ready (then just place it), keeping the grab offset.
- [ ] Re-attach: watch `WM_MOVING`/`WM_EXITSIZEMOVE` (WinUIEx `WindowMessageMonitor`); over another window of
      the same instance (`CanAcceptTabsFrom`), open its sidebar and show the insert line; release there merges
      the tab and closes the temporary window.
- [ ] Latency: `MainWindow.Create` awaits the WebView2 environment, the left bar/home/chat/settings init and
      the extension popup — pre-create a hidden window when a drag starts.
- [ ] Then write `Docs/architecture/tab-dragging.md`.

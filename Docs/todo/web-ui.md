# Web UI, permissions, history, downloads — outstanding

Built and type-checked on Linux (`Scripts/LinuxCompileCheck/check.sh`) but **not run on Windows yet**.
Architecture: `Docs/architecture/web-ui.md`.

## Verify on Windows (first run)

- [ ] `Scripts/CheckBuild.ps1` passes (real XAML compiler; the Linux check does not validate XAML).
- [ ] Page context menu opens at the cursor at 100% **and** 125/150% display scaling (Location is divided by
      `RasterizationScale`; if it is off by the scale factor, drop the division in `OnTabContextMenuRequested`).
- [ ] Submenus (e.g. the engine's "Share"/spell-check entries) open and "Back" returns.
- [ ] Popups (context menu, prompts, toasts, downloads, history) draw over the WebView2 content.
- [ ] Permission prompt: allow/block with and without "Remember", then check Settings > Site Permissions.
- [ ] `alert`/`confirm`/`prompt`/`beforeunload` (e.g. `javascript:alert(1)` from the console) and a background
      tab's alert waiting until you switch to it.
- [ ] HTTP basic auth prompt (e.g. https://httpbin.org/basic-auth/user/pass).
- [ ] Downloads: pause/resume/cancel, "ask where to save", "show in folder", list survives restart.
- [ ] Private window: InPrivate session works, nothing in History, not restored after restart.
- [ ] Extensions: on/off survives restart; Update on a Chrome Web Store and an Edge Add-ons extension;
      Load unpacked.
- [ ] Settings search + category buttons scroll to the right section.

## Follow-ups

- [ ] Context menu shows no keyboard-shortcut hints (`ShortcutKeyDescription`); `FTextButton` has no slot for them.
- [ ] Tooltips on icon-only buttons (downloads/history rows) would need a themed tooltip style first.
- [ ] Dragging a tab between a private and a normal window re-creates it in the target's mode; consider blocking.
- [ ] History is one JSON file rewritten on change (low priority, every ~25 s). Fine for months of browsing;
      move to SQLite if it grows past a few MB.
- [ ] "Load unpacked" copies the folder; edits to the source need another "Load unpacked" (Chrome loads in place).
- [ ] `BrowserDataPath` setting is still not wired to anything.

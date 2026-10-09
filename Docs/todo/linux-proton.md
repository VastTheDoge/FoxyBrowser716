# Linux via Proton/Wine (untested)

Run the unpackaged build (`Docs/architecture/unpackaged.md`) under Wine or Proton. **None of this has
been run on Linux yet.** This is the cheap attempt; the long-term Linux answer is the Rust rewrite
(`rust-poc.md`).

**Main risk:** WinUI 3 and its WebView2 control both draw through DirectComposition, and Wine's dcomp
is mostly stubs. A blank, white, or black window means the attempt has hit that wall, and it can't be
fixed from this repo. wine-staging carries extra dcomp patches, so try it as well as Proton.

## Background (researched 2026-10-09)

- No public report was found of any WinUI 3 app rendering under Wine or Proton. Microsoft says WinUI
  is Windows-only, which rules out native Linux but says nothing either way about Wine.
- Wine's DirectComposition is stubs. A late-2025 merge request makes the calls report success, but the
  window then renders blank white (https://gitlab.winehq.org/wine/wine/-/merge_requests/9839).
  wine-staging carries a `dcomp-DCompositionCreateDevice2` patchset. A third-party fork adds real
  d2d1/dcomp support and is worth a try if both fail (https://github.com/mklnln/wine-d2d1-dcomp).
- WebView2 under Wine:
  - Lutris scripts get *standalone* WebView2 apps working with a per-app Windows 8 override for
    `msedgewebview2.exe`, which makes WebView2 skip DirectComposition. That trick **doesn't apply
    here**: it forces HWND hosting, and WinUI's WebView2 control always uses composition (visual)
    hosting.
  - Wine bug 56378 (filed against Wine 9.3, status unknown) reports that Edge/WebView2 freezes unless
    it runs with `--no-sandbox`. The app adds that flag under Wine (`TabManager`).
- The old `origin/copilot/create-implementation-plan-linux-support` branch (Copilot, ~2025) is
  **superseded and untrusted**. Don't merge it. Its `Bootstrap.Initialize` call can't work in a Wine
  prefix.

## Setup

1. **On Windows**, run `Scripts/PublishUnpackaged.ps1` and copy `publish/unpackaged-win-x64/` to the
   Linux machine (e.g. `~/Apps/FoxyBrowser716/`).
   - Sanity check first: run `FoxyBrowser716.exe` from that folder on Windows, with the normal
     FoxyBrowser closed (they share `%APPDATA%\FoxyBrowser716`).
2. **Prefix:** leave its Windows version at the default Windows 10. WinAppSDK needs 10.0.17763+.
   ```bash
   export WINEPREFIX="$HOME/.wine-foxybrowser"
   wineboot --init
   ```
3. **WebView2 runtime** in the prefix: get the Evergreen Standalone Installer (x64) from
   https://developer.microsoft.com/microsoft-edge/webview2/ and run it with `wine <installer>.exe`.
   Alternatively, extract the *Fixed Version* runtime somewhere in the prefix and point
   `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` at it.
4. **Run**, either way:
   - wine / wine-staging: `WINEPREFIX="$HOME/.wine-foxybrowser" wine ~/Apps/FoxyBrowser716/FoxyBrowser716.exe`
   - Proton outside Steam via umu-launcher:
     `WINEPREFIX="$HOME/.wine-foxybrowser" PROTONPATH=GE-Proton umu-run ~/Apps/FoxyBrowser716/FoxyBrowser716.exe`
   - Steam: add `FoxyBrowser716.exe` as a non-Steam game and force Proton (Experimental first) in
     Properties → Compatibility. Steam uses its own prefix (`steamapps/compatdata/<id>/pfx`), so install
     WebView2 there instead, e.g. `protontricks-launch --appid <id> <installer>.exe`.
5. **Default browser** (optional). Add a launcher script plus a desktop entry:
   ```bash
   cat > ~/.local/bin/foxybrowser <<'EOF'
   #!/usr/bin/env bash
   export WINEPREFIX="$HOME/.wine-foxybrowser"
   exec wine "$HOME/Apps/FoxyBrowser716/FoxyBrowser716.exe" "$@"
   EOF
   chmod +x ~/.local/bin/foxybrowser
   cat > ~/.local/share/applications/foxybrowser716.desktop <<EOF
   [Desktop Entry]
   Type=Application
   Name=FoxyBrowser716
   Exec=$HOME/.local/bin/foxybrowser %u
   Categories=Network;WebBrowser;
   MimeType=x-scheme-handler/http;x-scheme-handler/https;text/html;
   EOF
   xdg-settings set default-web-browser foxybrowser716.desktop
   ```

## Debugging

- App logs: `$WINEPREFIX/drive_c/users/$USER/AppData/Roaming/FoxyBrowser716/Data/errors.jsonl`.
- `WINEDEBUG=+dcomp,err` shows which DirectComposition calls are stubbed when the window is blank.
- `0x80040154` (`REGDB_E_CLASSNOTREG`) at startup means Wine isn't honoring the exe manifest's
  `activatableClass` entries (reg-free WinRT).
- You can pass extra Chromium flags without rebuilding via
  `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS="--no-sandbox --disable-gpu"`. It may *replace* the app's own
  flags, so always include `--no-sandbox`.

## Checklist

- [ ] Unpackaged build starts and works on Windows
- [ ] Window renders under Wine/Proton (go/no-go)
- [ ] A page renders in a tab (WebView2)
- [ ] Clicking, typing, and scrolling in pages
- [ ] A second launch with a URL opens a tab in the running window (single-instance redirect)
- [ ] Settings, tabs, and bookmarks persist across restarts
- [ ] Extensions load
- [ ] Downloads land in `~/Downloads`
- [ ] File pickers (home background, extension folder, settings import/export). They may fail under
      Wine, in which case the button should just do nothing.
- [ ] Default-browser desktop entry opens links
- [ ] Follow-up if the pickers fail: switch to WinAppSDK 1.8's `Microsoft.Windows.Storage.Pickers`
      (IFileDialog-based, no `InitializeWithWindow`), which is likelier to work under Wine

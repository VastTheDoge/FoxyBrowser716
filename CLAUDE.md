# FoxyBrowser716

A Windows browser built on WinUI 3 + WebView2 — multi-instance (profiles), tab groups, a
widget-based home page, a Chrome-extension loader, theming, and an AI chat side-panel.

**This file is the map.** It is loaded every session, so it stays short and stable: source layout,
where things live, how to build, and an index into the deep docs. Depth lives in
`Docs/architecture/`. Outstanding work lives in `Docs/todo/`. Cross-cutting gotchas live in
`memory/`. **Do not append progress logs to this file** — see *Maintaining these docs* at the bottom.

## Project

One project: `FoxyBrowser716/FoxyBrowser716.csproj` — a single-project MSIX WinUI 3 app
(`WinExe`, .NET 9, `RootNamespace` `FoxyBrowser716`). WebView2 is the tab engine; everything else is
native WinUI. (`FoxyBrowser716-WinUI` at the root is a stale rename artifact — only `bin/obj`, not in
the solution.)

## Where things live

| Area | Key paths |
|---|---|
| App entry | `App.xaml(.cs)`, `GlobalUsingStatements.cs`, `app.manifest`, `Package.appxmanifest` |
| App spine / services | `DataManagement/` — `AppServer` (static app lifecycle + single-instance server), `Instance` (a browser profile/window), `TabManager`, `DownloadManager`, `HistoryManager`, `SitePermissionManager`, `ExtensionManager` (Chrome-extension manifest parsing + loading, enable/update/unpacked), `BackupManagement`, `FoxyFileManager`, `FoxyLogger` |
| Domain models | `DataObjects/Basic/` — POCOs (`Theme`, `GlobalSettings`, `InstanceSettings`, `TabGroup`, `Download`, `HistoryEntry`, `SitePermission`, `Extension`, `WebsiteInfo`, `VersionInfo`, `BackupModel`, `InstanceCache`…) |
| Tabs & AI | `DataObjects/Complex/` — `WebviewTab`, `AiChat`, `AiHandler`, `FoxyAutoSaver`, `FoxyAutoSaverLockedList`; `DataObjects/WebviewTab/` — `WebviewTab` partials (`WTMain`, `WTExtensionManagement`, `WTExtensionManifest`, `WTPerformance`) |
| Settings framework | `DataObjects/Settings/` — `ISetting`, `Setting`, `BrowserSettings`, `SettingClasses`, `SettingsUiHelper`, `ThemedUserControl` |
| Browser window UI | `Controls/MainWindow/` — `MainWindow` (+ `MainWindow.WebUi.cs`: hosts the themed web UI), `TopBar`, `LeftBar`/`NewLeftBar`, `TabCard`, `TabGroupCard`, `NewTabCard`, `NewTabGroupCard`, `BookmarkCard`, `InstanceCard`, `AiChatWindow` |
| Themed web UI | `Controls/WebUi/` — code-built replacements for WebView2's UI: `WebPromptHost`/`WebPromptCard` (permission, alert/confirm/prompt, sign-in), `DownloadsPanel`, `HistoryPanel`, `ToastHost`, `WebUiStyle`. See `Docs/architecture/web-ui.md` |
| Home page + widgets | `Controls/HomePage/` — `HomePage`, `Widget`, `WidgetEditOverlay`, `Widgets/` (`DateTime`, `SpeedTest`, `Title`, `Example`) |
| Settings UI | `Controls/SettingsPage/` (+ `SettingsCustomControls/`: `ExtensionController`, `SitePermissionsController`, `BrowsingDataController`) |
| Reusable controls | `Controls/Generic/` — `F`-prefixed primitives (`FIconButton`, `FTextButton`, `FTextInput`, `FRGBInput`, `FContextMenu`, `FCheckBox`, `FProgressBar`, `FoxyPopup`, `FTODO`, `TransparentWindow`) |
| Converters / helpers | `Controls/Helpers/` — value converters, `Animator`, `VisualCaptureHelper` |
| Static data | `StaticData/` — `DefaultThemes`, `InfoGetter` |
| Styles / assets | `Themes/Generic.xaml` (default control resources), `Assets/` (icons + MSIX tiles) |

## Build / run

- **Runtime:** .NET 9, WinUI 3 (Windows App SDK 1.8) + CsWinRT, single-project MSIX. `LangVersion`
  `preview`, nullable enabled.
- **Platforms:** `x86;x64;ARM64` — **no AnyCPU**, so every build/run must name a platform.
- **Check a change:** `Scripts/CheckBuild.ps1 [-Project <csproj> -Platform x64]` — quiet build that
  prints only errors/warnings (defaults to the app csproj at Platform x64).
- **Check a change on Linux (no Windows):** `Scripts/LinuxCompileCheck/check.sh` — type-checks all C# via
  generated XAML stubs; does not validate XAML. See its README and `memory/linux-compile-check.md`.
- **Run:** launch from Rider/VS with the *Unpackaged* (`commandName: Project`) or *(Package)*
  (`MsixPackage`) profile in `Properties/launchSettings.json` — those set up the Windows App SDK
  bootstrapper. Release builds are R2R + `TieredPGO`, published per-RID (`win-x64`/`win-x86`/`win-arm64`).
- **Unpackaged (no MSIX) build:** `Scripts/PublishUnpackaged.ps1 [-Platform x64]` → self-contained folder in
  `publish/` (also the Wine/Proton build). Packaged-only APIs must be guarded with `AppEnvironment.IsPackaged`.
  See `Docs/architecture/unpackaged.md`.

## Tech stack

WinUI 3 / Windows App SDK 1.8 + CsWinRT · WebView2 (Chromium) as the tab engine ·
CommunityToolkit.Mvvm (`ObservableObject` + source-gen) and CommunityToolkit.WinUI (animations,
Markdown) · Material.Icons.WinUI3 · Win2D (`Microsoft.Graphics.Win2D`) · WinUIEx (window
management) · Mistral.SDK (AI chat) · ColorCode.WinUI (syntax highlighting).

## Code lookup workflow

**Hard rule: never Read a source file just to learn a type's shape (members, signatures,
attributes).** That is what `Docs/api/` is for — one markdown file per type, named
`FoxyBrowser716.{Folder...}.{Class}.md` (e.g. `FoxyBrowser716.DataManagement.TabManager.md`,
`FoxyBrowser716.DataObjects.Basic.Theme.md`). It is generated (gitignored) by
`Scripts/GenerateApiDocs.ps1`, which stamps `Docs/api/_generated.txt` (UTC time + commit). If the
stamp is missing or predates recent changes, regenerate before trusting a doc.

Reading source is justified only for (a) files you are about to edit, or (b) implementation detail the
API doc can't show (method bodies, XAML layout). `grep` + `Docs/api/` answers shape questions cheaper.

For NuGet package APIs (both default `-Project FoxyBrowser716`, so `-Project` is rarely needed):
- `Scripts/GetNugetDoc.ps1 -Package X [-Type Y] [-Member Z]` — xml-doc lookup (simple class names
  resolve; ambiguity lists namespaces; no `-Type` lists all types).
- `Scripts/GetNugetApi.ps1 (-Package X | -Dll path) [-Type Y] [-Member Z]` — reflected public surface
  (signatures only): packages without xml docs, or any built assembly via `-Dll`.
- **WinUI caveat:** `-Package` reflection can fail to resolve WinRT projection assemblies
  (`Microsoft.Windows.SDK.NET`) that live in the build output, not the package cache. For those, run
  `-Dll` against the built copy under `bin\...\win-x64\`, where the projections sit as siblings.

## Documentation index

- `Docs/architecture/` — deep per-subsystem docs (how it works now). Filled in as subsystems get
  documented; empty at first.
- `Docs/todo/` — outstanding-work checklists (one per workstream).
- `Docs/api/` — generated markdown API reference (gitignored); regenerate via
  `Scripts/GenerateApiDocs.ps1`.
- `memory/` — repo-local gotcha notes; `memory/MEMORY.md` is the one-line-per-note index.

## Maintaining these docs

- **When a work-pass ends, do not append a `vX.Y` log block anywhere.** Update the relevant
  `Docs/architecture/*.md` and/or tick items in `Docs/todo/*`. Git is the changelog.
- **After a pass that changes public type shapes, rerun `Scripts/GenerateApiDocs.ps1`** so `Docs/api/`
  stays trustworthy.
- **This file changes only when the layout, a path, a command, or the tech stack changes.** Keep it a
  map, not a narrative.

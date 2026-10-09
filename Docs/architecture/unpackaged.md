# Unpackaged builds (no MSIX)

The app normally ships as MSIX. The unpackaged build is a plain, self-contained exe folder with no
install step and no package identity. It's portable on Windows, and it's what runs under Wine/Proton on
Linux, because MSIX can't be installed into a Wine prefix (see `Docs/todo/linux-proton.md`).

## Building

`Scripts/PublishUnpackaged.ps1 [-Platform x64|x86|ARM64]` writes to `publish/unpackaged-win-<rid>/`
(gitignored, ~280 MB for x64). It runs `dotnet publish` with `-p:FoxyUnpackaged=true`, and the csproj
turns that switch into:

- `WindowsPackageType=None`: no MSIX.
- `WindowsAppSDKSelfContained=true`: the Windows App SDK runtime DLLs ship in the folder. Its WinRT
  classes are registered by `activatableClass` entries in the exe's embedded manifest (reg-free WinRT),
  so there is no bootstrapper and no framework package to install. **Don't add a
  `Bootstrap.Initialize` call**: it requires the WinAppSDK framework MSIX, which defeats the point.
- `SelfContained=true`: the .NET runtime ships in the folder.
- `Assets\Foxybrowser716.ico` is copied to the output (window icon; MSIX gets it from the package layout).

Normal (MSIX) builds are unaffected; the switch is off by default.

## Runtime: `AppEnvironment`

`StaticData/AppEnvironment.cs` holds two flags:

- `IsPackaged` is true when `GetCurrentPackageFullName` does not return `APPMODEL_ERROR_NO_PACKAGE`. It's
  a runtime check, so one code path serves MSIX, the unpackaged publish, and Rider's *Unpackaged* launch
  profile.
- `IsWine` is true when `ntdll` exports `wine_get_version`, which real Windows never does.

Packaged-only APIs throw without package identity. Where they are, and what unpackaged does instead:

| Where | Packaged | Unpackaged |
|---|---|---|
| `App.OnLaunched` JIT startup profile | `ApplicationData.Current.LocalFolder` | `FoxyFileManager` Cache folder |
| `App.OnLaunched` | `CoreApplication.EnablePrelaunch` | skipped |
| `App.RequestRestartAfterClose` | powershell → `shell:AppsFolder\<AUMID>!App` | relaunches `Environment.ProcessPath` with `FOXYBROWSER716_RESTARTED_FROM=<pid>`; see below |
| `AppServer.HandleLaunchEvent` | `StartupTask` (launch on login) | none |
| `MainWindow` ctor icon | `AppContext.BaseDirectory\Assets\...` (same for both) | same |
| `TabManager` WebView2 args | unchanged | under Wine, adds `--no-sandbox` (Chromium's sandbox needs Windows security primitives Wine lacks) |

**Restart:** the new process sees the env var, waits up to 10 s for the old pid to exit (so it becomes
the main instance instead of redirecting to the dying one), and clears the var so WebView2's child
processes don't inherit it. If startup fails *again* in a restarted process, it logs and exits instead of
looping.

**Single instance:** `AppInstance` (WinAppSDK AppLifecycle) supports unpackaged apps. If it throws while
unpackaged (possible under Wine), startup carries on without redirection, so each launch gets its own
process, and arguments come from `Environment.GetCommandLineArgs()`.

**Launch arguments:** an unpackaged launch arrives as `ExtendedActivationKind.Launch`, and its
`Arguments` is the whole command line. `App.SplitArguments` splits it, keeping quoted paths together,
and the leading exe path is dropped. The same splitter now also handles `CommandLineLaunch` (the
app-execution alias).

## What unpackaged doesn't have

- Launch on login, protocol/file associations, and the `FoxyBrowser716.exe` execution alias. These all
  come from `Package.appxmanifest`. On Linux, default-browser registration is done by a desktop entry
  instead.
- A separate data folder. Both builds use `%APPDATA%\FoxyBrowser716`, though MSIX may redirect a packaged
  install's AppData writes into its private package store. Don't run a packaged and an unpackaged copy
  at the same time against the same folder.
- File pickers (`Windows.Storage.Pickers` + `InitializeWithWindow`) work unpackaged on Windows. Whether
  they work under Wine is unknown.

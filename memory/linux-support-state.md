---
name: linux-support-state
description: Where the Linux/Proton work lives, its merge state, and the DirectComposition blocker (as of 2026-10-09)
metadata:
  type: project
---

Linux support = run the Windows build under Proton/Wine (WinUI 3 + WebView2 have no native Linux
path). The work lives on `origin/copilot/create-implementation-plan-linux-support` (6 commits, remote
only, forked from `master` 4ffb0dc, **not merged** into v0.8.0). Contents: `Program.cs` custom Main +
`Bootstrap.Initialize` (WinAppSDK 1.8), `StaticData/PackageHelper.cs` (`IsPackaged` guards on
StartupTask/prelaunch/ApplicationData), unpackaged publish profile `UnpackagedWin-x64.pubxml`,
quoted-arg parsing, a direct-exe restart path when unpackaged (no powershell), and `LINUX_PROTON.md`.

**Merge state (checked 2026-10-09 with `git merge-tree`):** one conflict only, in
`MainWindow.xaml.cs` `SetIcon` — take the branch's `AppContext.BaseDirectory` version (works packaged
and unpackaged). App.xaml.cs auto-merges; d6480e5's restart guard is in `WebviewTab.cs`, not App.

**Known bug on the branch:** `Program.Main` calls `Bootstrap.Initialize` when unpackaged, which needs
the WinAppSDK framework MSIX installed — impossible in a Wine prefix. Unpackaged build needs
`WindowsAppSDKSelfContained=true` + `WindowsPackageType=None` and no bootstrap call.

**Likely hard blocker:** WinUI 3 renders through Microsoft.UI.Composition → DirectComposition, and
WinUI's WebView2 uses composition hosting. Wine's dcomp is stubs (late-2025 MR makes calls "succeed"
with a blank white window). No public report of a WinUI 3 app rendering under Wine/Proton found.
The win8-override trick for WebView2 doesn't apply (it forces HWND hosting, WinUI uses visual hosting).

**How to apply:** treat Proton as an unproven spike — test a blank WinUI 3 window under Proton
Experimental / wine-staging before investing. Long-term Linux answer is [[rust-rewrite-plan]].

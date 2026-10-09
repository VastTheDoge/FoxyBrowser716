---
name: linux-support-state
description: Linux/Proton status: unpackaged build exists on v0.8.0 (untested on Linux); Copilot branch untrusted; DirectComposition is the likely wall (as of 2026-10-09)
metadata:
  type: project
---

The Linux attempt is to run the unpackaged (no MSIX) self-contained build under Wine/Proton. That build
was written fresh on v0.8.0 on 2026-10-09: `-p:FoxyUnpackaged=true` / `Scripts/PublishUnpackaged.ps1`,
plus `StaticData/AppEnvironment` (`IsPackaged`, `IsWine`). It builds and publishes on Windows, but it
**has never been launched**, on Windows or Linux. Mechanism: `Docs/architecture/unpackaged.md`. Setup
and test checklist: `Docs/todo/linux-proton.md`. The long-term Linux answer is
[[rust-rewrite-plan]].

**`origin/copilot/create-implementation-plan-linux-support` is untrusted.** It was generated ~2025 by
Copilot on an old model. Don't merge it, build on it, or cite it. It is superseded by the work above.
One known defect, as a warning: it calls `Bootstrap.Initialize` when unpackaged, which needs the
WinAppSDK framework MSIX, and that can't be installed in a Wine prefix.

**Likely hard blocker:** WinUI 3 and its WebView2 control draw through DirectComposition, and Wine's
dcomp is mostly stubs (a late-2025 MR makes the calls "succeed" but leaves the window blank white). No
public report of a WinUI 3 app rendering under Wine was found. The win8-override trick for WebView2
doesn't apply here, because it forces HWND hosting and WinUI uses visual hosting.

**Gotchas:**
- Build-checking in a git worktree under the scratchpad fails silently in XamlCompiler, because the
  path is over 260 characters. Use a short path such as `RiderProjects\FoxyBrowser716-wt`.
- An unpackaged run on Windows shares `%APPDATA%\FoxyBrowser716` with the user's running browser, so
  don't smoke-launch it while their FoxyBrowser is open.

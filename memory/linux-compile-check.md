---
name: linux-compile-check
description: How to type-check the WinUI app on Linux (Scripts/LinuxCompileCheck) and why the real XAML compiler can't run there
metadata:
  type: reference
---

`Scripts/LinuxCompileCheck/check.sh` compiles all C# against the real package references on Linux, using
generated XAML pass-1 stubs (`genstubs.py`: x:Name fields, `InitializeComponent`, event hookups) instead of
the XAML compiler. It does **not** validate XAML or `{x:Bind}`; build complex UI in C# when you cannot run
Windows, and still run `Scripts/CheckBuild.ps1` on Windows.

**Why not the real compiler (tried 2026-10):** WinAppSDK 1.8's `XamlCompiler.exe` is net472 and, under Mono
or as the in-proc net6.0 task, loads metadata through the Windows COM metadata dispenser
(`RuntimeEnvironment.GetRuntimeInterfaceAsObject`), which neither Mono nor .NET on Linux provides. Shimming
kernel32 file mapping gets past `CreateFile` but not that. CsWinRT projection and MSIX/PRI steps also exec
Windows binaries, so the check project disables them.

**Cloud session setup:** the dotnet CDN is blocked by the egress proxy, but `https://archive.ubuntu.com`
and nuget.org are reachable: switch `/etc/apt/sources.list.d/ubuntu.sources` to https and
`apt-get install dotnet-sdk-10.0` (it targets net9.0 fine). See [[foxybrowser-doc-generation]] for API docs.

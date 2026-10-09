# Linux compile check

Type-checks all of FoxyBrowser716's C# on Linux (e.g. a cloud agent session) where the real build cannot
run: WinUI's XAML compiler is a .NET Framework exe that needs Windows' COM metadata APIs, and CsWinRT/MSIX
steps call Windows executables.

```sh
Scripts/LinuxCompileCheck/check.sh   # prints compiler errors, exit 1 if any
```

Needs a .NET SDK (9 or later; Ubuntu's `dotnet-sdk-10.0` works) and `python3`. First run restores
packages from nuget.org (~10 s after that).

How it works (everything lands in the gitignored `.work/`):

1. `make_csproj.py` writes a project with the app's `.cs` files and `PackageReference`s but none of the
   Windows-only steps (no XAML pages, CsWinRT projection off, no MSIX/PRI).
2. `WinUiMap/` dumps every public `Microsoft.UI.Xaml*` type and its events from the restored packages
   (metadata only), so the stub generator knows element types and which attributes are events.
3. `genstubs.py` generates what XAML "pass 1" would: a partial class per `x:Class` with a field for each
   `x:Name`/`Name`, an empty `InitializeComponent()`, and `element.Event += this.Handler;` lines so event
   handler names and signatures are type-checked.
4. `dotnet msbuild -t:Compile` on that project.

What it does **not** check: XAML itself (unknown attributes, bad values, `{x:Bind}` paths, resources) and
anything at runtime. Prefer building complex UI in C# (as `Controls/WebUi/` does) so the compiler sees it,
and still run `Scripts/CheckBuild.ps1` on Windows before shipping.

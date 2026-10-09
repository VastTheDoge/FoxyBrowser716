# Memory Index

- [Doc generation](foxybrowser-doc-generation.md) — Scripts/GenerateApiDocs.ps1 + docfx.json produce Docs/api/ markdown (gitignored); single WinUI project = no linked-doc workaround; regenerate after type-shape changes
- [NuGet lookup scripts](nuget-lookup-scripts.md) — GetNugetDoc/GetNugetApi (default -Project FoxyBrowser716); WinUI caveat: GetNugetApi -Package can't resolve Microsoft.Windows.SDK.NET projections, use -Dll against bin\...\win-x64\
- [Linux support state](linux-support-state.md) — unpackaged build (-p:FoxyUnpackaged=true) on v0.8.0, never launched; copilot branch untrusted/superseded; Wine dcomp stubs likely the wall; long-path XamlCompiler + shared-AppData gotchas
- [Rust rewrite plan](rust-rewrite-plan.md) — cef-rs + Slint, new branch, spike-first; OSR settled (zero-airspace constraint), clean-slate data; extension bands + spike checklist (2026-07)
- [Linux compile check](linux-compile-check.md) — Scripts/LinuxCompileCheck/check.sh type-checks C# on Linux via generated XAML stubs (no XAML validation); real XAML compiler needs Windows COM metadata APIs; install SDK via https Ubuntu archive in cloud sessions

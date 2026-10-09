# Architecture docs

Deep per-subsystem docs — how a subsystem works *now*, not a changelog. One file per subsystem,
kept in sync with the code (git is the history).

Add a file when a subsystem is complex enough that the map in `CLAUDE.md` plus the generated
`Docs/api/` reference aren't enough — e.g. the extension loader, the multi-instance model, the AI chat pipeline.

- `web-ui.md` — themed replacements for WebView2's UI (context menu, prompts, downloads), site permissions,
  history, private windows, how settings reach tabs, extension management.
- `unpackaged.md` — the no-MSIX self-contained build (`-p:FoxyUnpackaged=true`), `AppEnvironment`, packaged-only call sites.

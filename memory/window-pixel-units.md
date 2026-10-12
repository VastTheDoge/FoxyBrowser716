---
name: window-pixel-units
description: AppWindow is physical pixels, WinUIEx MoveAndResize/SetWindowSize scale size by DPI; mixing them broke window restore at >100% scaling
metadata:
  type: project
---

`AppWindow.Position`/`Size`/`MoveAndResize(RectInt32)` and `DisplayArea.WorkArea` are **physical
pixels**. WinUIEx's `WindowExtensions.MoveAndResize(x, y, w, h)` (2.9.0, checked by IL) passes x/y
through unscaled but multiplies **w/h by dpi/96**; `SetWindowSize`/`CenterOnScreen` take DIPs too.

Saving `AppWindow.Size` into `Backup.json` and restoring with WinUIEx `MoveAndResize` grew the window
by the display scale on every crash/shutdown restore (only visible on scaled displays, e.g. 150%).
Window bounds now go through `MainWindow.ApplyBounds` (physical px, clamped to the nearest monitor's
work area); callers with a DIP size (tab/group tear-off) multiply by `XamlRoot.RasterizationScale`.

**How to apply:** keep anything persisted or passed as window bounds in physical pixels and apply it
with `AppWindow`, not the WinUIEx size helpers.

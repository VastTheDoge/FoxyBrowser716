# Theming: themes, the theme store, the gallery and editor, the color picker

A `Theme` (`DataObjects/Basic/Theme.cs`) is a struct of ten opaque colors; the "slight" and "very" transparent
variants are computed (alpha 200 and 100) and are not editable. Every control has a `CurrentTheme` and recolors
itself in `ApplyTheme`; `Instance.CurrentTheme` pushes a theme to every window of the instance, and
`MainWindow.ApplyTheme` fans it out to the bars, pages and panels.

## Which theme an instance uses

`BrowserSettings.ThemeName` holds a **theme id**: a built-in's display name ("Vast Seas") or a custom theme's
file name (a GUID). `Instance` resolves it with `ThemeManager.Resolve` when it loads, whenever the setting changes,
and whenever `ThemeManager.Changed` fires (a custom theme in use was edited or deleted). An id that no longer
exists resolves to Dark Mode; the setting itself is only rewritten by the gallery's Delete, which points every
instance using the deleted theme at the first built-in.

## ThemeManager (`DataManagement/ThemeManager.cs`)

The one list of themes, global to the app:

- **Built-ins** come from `DefaultThemes.BuiltIn` (ordered; the names double as ids, so renaming one orphans
  saved choices).
- **Custom themes** are one `.foxytheme` file each in `<data folder>/Data/Themes/`, shared by every instance and
  moving with the data folder (`FoxyFileManager.FolderType.Data`). They are read once, lazily, on first use;
  files that fail to parse are logged and skipped.
- `Save(id, name, theme)` writes or overwrites (a null or built-in id makes a new file), `Delete`, `Import(path)`
  (reads any `.foxytheme` and saves it as a new custom theme) and `Export(entry, path)`. Names are made unique with
  a " (2)" suffix. All raise `Changed` on the calling thread; call from the UI thread.

## File format (`DataObjects/Basic/SavedTheme.cs`)

JSON with the name and ten `#RRGGBB` strings (`#AARRGGBB` if not opaque):

```json
{ "Name": "Vast Seas", "PrimaryBackground": "#283C50", "SecondaryBackground": "#14283C", "PrimaryForeground": "#FFFFFF", … }
```

`SavedTheme.From(name, theme)` and `ToTheme()` convert; `ToTheme` is null when any color isn't valid hex.
`SavedTheme.Hex` / `TryParseHex` are the shared hex helpers (the color picker uses them too). The same shape is
used on disk and for export, so an exported file can be dropped straight into the Themes folder.

## Settings UI (`Controls/SettingsPage/SettingsCustomControls/ThemeController.cs`)

The Appearance section. Two views, rebuilt from scratch on each switch:

- **Gallery**: "New theme" (starts from the colors in use) and "Import…", then a card per theme: a
  `ThemePreview`, the name, Built-in/Custom, and buttons. Clicking the preview or Use sets
  `BrowserSettings.ThemeName`. Custom themes get Edit and a confirming Delete; everything gets a Copy (the way to
  edit a built-in: it opens the editor as a new theme) and Export (a `FileSavePicker`). The gallery refreshes on
  `ThemeManager.Changed` and on `ThemeName` changes, coalesced into one rebuild.
- **Editor**: name box, a large preview and ten rows (label, what the color is used for, a swatch, the hex).
  A swatch opens one shared `FColorPicker` in a flyout; changes go into a draft `Theme` that only the editor's
  preview shows. **Nothing is applied live**: Save writes the file (and the instance recolors only if that theme is
  the one in use), Cancel drops the draft. A new theme is not selected after saving; the user picks it in the gallery.

The home picture: `MainWindow.HomeImageUri` / `HomeImageChanged` mirror what the home page loads, and every
preview draws it behind the bars so colors are judged against the real backdrop. The picture is not part of a
theme.

`ThemePreview` is a miniature window (frame, top bar, sidebar, a home-page card with text, a selected and a
normal button, and the yes/no dots), drawn at 320×200 inside a `Viewbox`. It mirrors the color roles of
`MainWindow`, `TopBar`, `NewLeftBar`, `Widget.ApplyCardTheme` and `FTextButton`; when those change their
colors, update it.

## FColorPicker (`Controls/Generic/FColorPicker.cs`)

Code-built, opaque colors only: a saturation/value square (hue fill under a white→transparent and a
transparent→black gradient) and a vertical hue bar, both dragged with pointer capture, plus a swatch, a hex
`FTextInput` and the existing `FRGBInput`. State is HSV so a grey keeps its hue while dragging. `Color` sets the
picker silently; `ColorChanged` fires for user changes from any of the inputs. Each input is skipped when it is
the source of the change, so typing in the hex box isn't rewritten under the caret.

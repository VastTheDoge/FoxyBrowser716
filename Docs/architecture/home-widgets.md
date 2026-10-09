# Home page widgets

The home page (`Controls/HomePage/HomePage.xaml(.cs)`) is a 40×40 star-sized grid over a background
image. Each widget occupies a `Row/Column/RowSpan/ColumnSpan` rectangle (`WidgetData`), so a widget's
pixel size and aspect ratio change with the window and with how the user resized it in edit mode.

## Adding a widget

1. Create `Widgets/FooWidget.xaml(.cs)` with root `homePage:WidgetBase` and a `protected` constructor.
2. Put `[WidgetInfo("Foo Widget", MaterialIconKind.X, WidgetCategory.Y)]` on the class. Registration is
   automatic (`WidgetBase.InitializeWidgets` is a `[ModuleInitializer]` that reflects over all subclasses);
   the name is the persisted key, so don't rename shipped widgets.
3. Override `Initialize()` (runs after saved settings are applied) and `ApplyTheme()` (runs whenever
   `CurrentTheme` is set — the home page sets it right after creation and on every theme change).

Helpers on `WidgetBase`:

| Helper | Use |
|---|---|
| `ApplyCardTheme(Border)` | standard translucent card background + border |
| `CreateLiveTimer(interval, tick)` | UI-thread timer that ticks immediately on `Loaded` and stops on `Unloaded` — use this, not `System.Threading.Timer`, so deleted/reloaded widgets stop ticking |
| `OpenInNewTab(urlOrSearch)` | `TabManager.AddTab` + switch to it (non-URLs are searched) |
| `CreateFavicon(url, size, fallbackBrush)` | image with a globe fallback on empty/failed URLs |
| `NormalizeUrl(text)` | adds `https://` to bare domains |
| `RequestSave()` | persist settings outside edit mode (e.g. the sticky note's text) |

## Sizing conventions

Pick one per widget:

- **Uniform scale** (most widgets): `Border` card → `Viewbox Stretch="Uniform"` → content with a **fixed
  design width**. The fixed width matters: if content width depends on text (e.g. a ticking clock),
  the Viewbox rescales every tick and the widget visibly jitters. Use `Consolas` for changing digits.
- **Reflow** (lists/text): no Viewbox; compute layout in `SizeChanged`. `BookmarksWidget` picks a column
  count from the width and scrolls vertically; `StickyNoteWidget` wraps text at a fixed font size;
  `SearchWidget` derives its font size from the height (capped by width).

## Widgets with external resources

- **Network**: one static `HttpClient` per widget class with a timeout; every call is async, caught, and shows a
  state in the widget. Nothing fetches during settings load — fetch from `Initialize`/`Loaded`/the timer.
  `SpeedTestWidget` only contacts `speed.cloudflare.com` when its button is pressed; its live graph reads local
  adapter counters (adapters with a gateway only, so Hyper-V/WSL switches don't double count).
- **WebView2** (`WebPageWidget`): created in code on `Loaded` from `TabManager.WebsiteEnvironment` (same profile
  as the window's tabs; InPrivate in a private instance) and closed on `Unloaded`. That is safe because the home page
  is only collapsed, never unloaded, when a tab is shown, so `Unloaded` means the widget was removed.

## Settings

`WidgetSettings` is a `List<ISetting>` built in the constructor; each `Setting<T>`'s change callback
applies the value live. In edit mode the overlay's settings button opens them via
`MainWindow.OpenSettings`. Values are saved into `WidgetData.Settings` (keyed by setting name) whenever
the layout is saved (`SaveWidgetsToJson` snapshots `GetSettingsMap()` from every live widget) and when a
widget calls `RequestSave()` outside edit mode. On load they come back as `JsonElement` and are
deserialized to the setting's `T` in `SetSetting`; a value that no longer fits keeps the default.
Avoid `ColorSetting` for now — `Windows.UI.Color` exposes fields, which `System.Text.Json` doesn't save.

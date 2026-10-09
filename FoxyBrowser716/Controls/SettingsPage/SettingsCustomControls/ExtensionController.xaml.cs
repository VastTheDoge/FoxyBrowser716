

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.Controls.WebUi;
using FoxyBrowser716.DataManagement;
using FoxyBrowser716.DataObjects.Settings;
using FoxyBrowser716.ErrorHandeler;
using Material.Icons.WinUI3;
using Windows.Storage.Pickers;
using WinRT.Interop;
using WinUIEx;

namespace FoxyBrowser716.Controls.SettingsPage.SettingsCustomControls;

/// <summary>
/// Settings section listing this instance's extensions with on/off, options, update and remove, plus
/// store links, loading an unpacked extension and checking everything for updates.
/// </summary>
public sealed partial class ExtensionsController : ThemedUserControl
{
    private readonly MainWindow.MainWindow? _mainWindow;
    private readonly List<FTextButton> _buttons = [];
    private readonly List<(FTextButton button, bool danger)> _themedButtons = [];
    private readonly List<TextBlock> _primaryText = [];
    private readonly List<TextBlock> _secondaryText = [];
    private readonly List<Border> _rowBorders = [];
    private readonly List<MaterialIcon> _icons = [];
    private TextBlock? _statusText;
    private bool _busy;

    public ExtensionsController()
    {
        InitializeComponent();
    }

    public ExtensionsController(MainWindow.MainWindow mainWindow)
    {
        InitializeComponent();

        _mainWindow = mainWindow;

        // only listen while shown, so closed windows do not stay referenced by the static event
        Loaded += (_, _) =>
        {
            ExtensionManager.ExtensionsModified += OnExtensionsModified;
            Refresh();
        };
        Unloaded += (_, _) => ExtensionManager.ExtensionsModified -= OnExtensionsModified;
    }

    private void OnExtensionsModified(string instanceName)
    {
        if (_mainWindow is null || instanceName != _mainWindow.Instance.Name) return;
        DispatcherQueue.TryEnqueue(Refresh);
    }

    protected override void ApplyTheme()
    {
        Root.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
        Root.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);

        foreach (var (button, danger) in _themedButtons)
        {
            button.CurrentTheme = danger ? WebUiStyle.DangerTheme(CurrentTheme) : CurrentTheme;
            WebUiStyle.ThemeIcon(button.Icon, CurrentTheme.PrimaryForegroundColor);
        }
        foreach (var text in _primaryText) text.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
        foreach (var text in _secondaryText) text.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
        foreach (var icon in _icons) icon.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
        foreach (var border in _rowBorders)
        {
            border.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColor);
            border.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
        }
    }

    private void Refresh()
    {
        if (_mainWindow is null) return;

        Root.Children.Clear();
        _buttons.Clear();
        _themedButtons.Clear();
        _primaryText.Clear();
        _secondaryText.Clear();
        _rowBorders.Clear();
        _icons.Clear();

        Root.Padding = new Thickness(6);
        Root.Spacing = 6;
        Root.Margin = new Thickness(0, 6, 0, 6);

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        toolbar.Children.Add(Button("Chrome Web Store", () => OpenInTab(ExtensionManager.ChromeWebStoreUrl), MaterialIconKind.Store));
        toolbar.Children.Add(Button("Edge Add-ons", () => OpenInTab(ExtensionManager.EdgeAddonsUrl), MaterialIconKind.Store));
        toolbar.Children.Add(Button("Load unpacked…", LoadUnpacked, MaterialIconKind.FolderPlus));
        toolbar.Children.Add(Button("Check for updates", CheckAllForUpdates, MaterialIconKind.Update));
        Root.Children.Add(new ScrollViewer
        {
            Content = toolbar,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
        });

        _statusText = Secondary(string.Empty);
        _statusText.Visibility = Visibility.Collapsed;
        Root.Children.Add(_statusText);

        var extensions = _mainWindow.Instance.GetSavedExtensions()
            .OrderBy(ExtensionManager.GetDisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (extensions.Count == 0)
        {
            var empty = Secondary("No extensions installed. Install one from the Chrome Web Store or Edge Add-ons, or load an unpacked folder.");
            empty.Margin = new Thickness(4);
            Root.Children.Add(empty);
        }

        foreach (var extension in extensions)
            Root.Children.Add(BuildRow(extension));

        ApplyTheme();
    }

    private UIElement BuildRow(Extension extension)
    {
        var icon = ExtensionManager.CreateIconElement(extension, 32);
        if (icon is MaterialIcon materialIcon) _icons.Add(materialIcon);
        if (icon is FrameworkElement iconElement)
        {
            iconElement.VerticalAlignment = VerticalAlignment.Top;
            iconElement.Margin = new Thickness(2, 2, 8, 0);
            iconElement.Opacity = extension.IsEnabled ? 1 : 0.5;
        }

        var name = Primary(ExtensionManager.GetDisplayName(extension));
        name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        var source = ExtensionManager.GetStoreSource(extension) switch
        {
            ExtensionManager.ExtensionSource.Chrome => "Chrome Web Store",
            ExtensionManager.ExtensionSource.Microsoft => "Edge Add-ons",
            _ => "Unpacked",
        };
        var details = Secondary($"Version {extension.Manifest.Version ?? "?"} · {source}{(extension.IsEnabled ? "" : " · Off")}");
        var description = ExtensionManager.GetDescription(extension) is { } text ? Secondary(text) : null;
        if (description is not null)
        {
            description.TextWrapping = TextWrapping.Wrap;
            description.MaxLines = 2;
            description.TextTrimming = TextTrimming.CharacterEllipsis;
        }

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(name);
        info.Children.Add(details);
        if (description is not null) info.Children.Add(description);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };

        var on = Button("On", () => SetEnabled(extension, true));
        var off = Button("Off", () => SetEnabled(extension, false));
        on.ForceHighlight = extension.IsEnabled;
        off.ForceHighlight = !extension.IsEnabled;
        actions.Children.Add(on);
        actions.Children.Add(off);

        if (ExtensionManager.GetOptionsUrl(extension) is { } optionsUrl)
            actions.Children.Add(Button("Options", () => OpenInTab(optionsUrl), MaterialIconKind.Cog));

        if (ExtensionManager.GetStoreSource(extension) is not null)
            actions.Children.Add(Button("Update", () => Update(extension), MaterialIconKind.Update));

        var remove = Button("Remove", () => { }, MaterialIconKind.Delete, danger: true);
        WebUiStyle.MakeConfirming(remove, "Click to confirm", () => Remove(extension));
        actions.Children.Add(remove);

        var grid = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        Grid.SetColumn(info, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(icon);
        grid.Children.Add(info);
        grid.Children.Add(actions);

        var border = new Border
        {
            Child = grid,
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(2),
        };
        _rowBorders.Add(border);
        return border;
    }

    private FTextButton Button(string text, Action onClick, MaterialIconKind? icon = null, bool danger = false)
    {
        var button = WebUiStyle.TextButton(text, () =>
        {
            if (!_busy) onClick();
        }, icon);
        _buttons.Add(button);
        _themedButtons.Add((button, danger));
        return button;
    }

    private TextBlock Primary(string text)
    {
        var block = WebUiStyle.Text(text, 14, wrap: false);
        _primaryText.Add(block);
        return block;
    }

    private TextBlock Secondary(string text)
    {
        var block = WebUiStyle.Text(text, 12);
        _secondaryText.Add(block);
        return block;
    }

    private void SetStatus(string? text)
    {
        if (_statusText is null) return;
        _statusText.Text = text ?? string.Empty;
        _statusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Runs an extension operation with the buttons locked and errors reported as a toast.</summary>
    private async Task RunBusy(string status, Func<WebView2, Task> work)
    {
        if (_mainWindow is null || _busy) return;

        if (_mainWindow.ExtensionPopupWebview.CoreWebView2 is null)
        {
            _mainWindow.ShowToast("Extensions are still loading", "Try again in a moment.", MaterialIconKind.Puzzle);
            return;
        }

        _busy = true;
        SetStatus(status);
        foreach (var button in _buttons) button.Opacity = 0.5;
        try
        {
            await work(_mainWindow.ExtensionPopupWebview);
        }
        catch (Exception e)
        {
            FoxyLogger.AddError(e);
            _mainWindow.ShowToast("Extension action failed", e.Message, MaterialIconKind.AlertCircle, isError: true);
        }
        finally
        {
            _busy = false;
            SetStatus(null);
            foreach (var button in _buttons) button.Opacity = 1;
        }
    }

    private async void SetEnabled(Extension extension, bool enabled)
    {
        if (extension.IsEnabled == enabled) return;
        await RunBusy(enabled ? "Turning on…" : "Turning off…",
            webview => _mainWindow!.Instance.SetExtensionEnabled(webview, extension, enabled));
    }

    private async void Remove(Extension extension)
    {
        await RunBusy("Removing…", webview => _mainWindow!.Instance.RemoveExtension(webview, extension.Id));
    }

    private async void Update(Extension extension)
    {
        await RunBusy($"Checking {ExtensionManager.GetDisplayName(extension)} for updates…", async webview =>
        {
            var (result, version) = await _mainWindow!.Instance.UpdateExtension(webview, extension);
            if (result == ExtensionManager.UpdateResult.UpToDate)
                _mainWindow.ShowToast($"{ExtensionManager.GetDisplayName(extension)} is up to date", $"Version {version}", MaterialIconKind.Puzzle);
        });
    }

    private async void CheckAllForUpdates()
    {
        if (_mainWindow is null) return;

        await RunBusy("Checking all extensions for updates…", async webview =>
        {
            var updated = 0;
            var failed = new List<string>();
            foreach (var extension in _mainWindow.Instance.GetSavedExtensions().ToList())
            {
                if (ExtensionManager.GetStoreSource(extension) is null) continue;
                try
                {
                    if ((await _mainWindow.Instance.UpdateExtension(webview, extension)).result == ExtensionManager.UpdateResult.Updated)
                        updated++;
                }
                catch (Exception e)
                {
                    FoxyLogger.AddError(e);
                    failed.Add(ExtensionManager.GetDisplayName(extension));
                }
            }

            if (failed.Count > 0)
                _mainWindow.ShowToast("Some updates failed", string.Join(", ", failed), MaterialIconKind.AlertCircle, isError: true);
            else if (updated == 0)
                _mainWindow.ShowToast("Extensions are up to date", null, MaterialIconKind.Puzzle);
        });
    }

    private async void LoadUnpacked()
    {
        if (_mainWindow is null) return;

        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _mainWindow.GetWindowHandle());
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        await RunBusy("Loading unpacked extension…", async webview =>
        {
            var extension = await _mainWindow.Instance.InstallUnpacked(webview, folder.Path);
            _mainWindow.ShowToast("Extension loaded", ExtensionManager.GetDisplayName(extension), MaterialIconKind.Puzzle);
        });
    }

    private void OpenInTab(string url)
    {
        if (_mainWindow is null) return;
        _mainWindow.TabManager.SwapActiveTabTo(_mainWindow.TabManager.AddTab(url));
    }
}

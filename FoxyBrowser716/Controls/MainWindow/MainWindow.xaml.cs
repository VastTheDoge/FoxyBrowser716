using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using Windows.Graphics.Display;
using Windows.UI.ViewManagement;
using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.DataManagement;
using FoxyBrowser716.DataObjects.Basic;
using FoxyBrowser716.DataObjects.Complex;
using Material.Icons;
using Material.Icons.WinUI3;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUIEx;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Windows.Win32.Graphics.Dwm;
using CommunityToolkit.WinUI.Animations;
using CommunityToolkit.WinUI.UI;
using FoxyBrowser716.Controls.Helpers;
using FoxyBrowser716.DataObjects.Settings;
using FoxyBrowser716.ErrorHandeler;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Web.WebView2.Core;


// using CommunityToolkit.WinUI.Helpers;
//

namespace FoxyBrowser716.Controls.MainWindow;

public sealed partial class MainWindow : WinUIEx.WindowEx
{
    public TabManager TabManager { get; private set; }
    public Instance Instance { get; private set; }

    private static int _windowCounter;
    // Claude Doc
    /// <summary>Unique for this run across every instance, like tab ids; the assistant's window_id.</summary>
    public int WindowNumber { get; } = System.Threading.Interlocked.Increment(ref _windowCounter);

    public Action<InfoGetter.SearchEngine> SearchEngineChangeRequested;
    
    // private VisualCaptureHelper? _captureHelper;
    
    private MainWindow()
    {
        InitializeComponent();

        try
        {
            //C the exe's folder, which is also the install folder when packaged (Package.Current throws unpackaged)
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Foxybrowser716.ico"));

        }
        catch (Exception e)
        {

        }
        
        // initial is needed to allow clicks for other buttons
        SetTitleBar(TopBar.DragZone); 
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            ExtendsContentIntoTitleBar = true;
            p.SetBorderAndTitleBar(true, false);
        }
        else
            throw new Exception("AppWindowPresenterKind is not OverlappedPresenter, cannot setup the window properly!");
        
        TopBar.DragZone.PointerEntered += (_, _) =>
        {
            // to fix a bug with this becoming unset for whatever reason
            SetTitleBar(TopBar.DragZone);
        };
        
        TopBar.UpdateMaximizeRestore(WindowState);
        ApplyTheme();

        //test.Child = new CustomWebView2(this) { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 50, MinWidth = 50};
    }

    public static async Task<MainWindow> Create(Instance instance)
    {
        var win = new MainWindow();
        await win.Initialize(instance);
        return win;
    }

    private void HandleCacheChanged()
    {
        TopBar.UpdateSearchEngineIcon(Instance.Cache.CurrentSearchEngine);
        TopBar.SetSidebarLockedState(Instance.Cache.LeftBarLocked);
        LeftBar.SetLockedState(Instance.Cache.LeftBarLocked);
    }

    private async Task Initialize(Instance instance)
    {
        Instance = instance;
        TabManager = await TabManager.Create(Instance, this);
        InitializeWebUi();

        await Task.WhenAll(LeftBar.Initialize(TabManager), HomePage.Initialize(this), ChatWindow.Initialize(this), SettingsPage.Initialize(this));
        
        // link events from tab manager
        TabManager.ActiveTabChanged += TabManagerOnActiveTabChanged;
        TabManager.TabAdded += TabManagerOnTabAdded;
        TabManager.TabRemoved += TabManagerOnTabRemoved;
        
        // link events from chat window
        ChatWindow.CloseRequested += () => TabHolder.Margin = TabHolder.Margin with { Right = 0 };
        
        // link events from the instance
        instance.Cache.PropertyChanged += (_, _) => HandleCacheChanged();
        
        // link events from home page
        HomePage.ToggleEditMode += HomePageOnToggleEditMode;
        
        TabManager.TryGetTab(TabManager.ActiveTabId, out var tab);
        RefreshCurrentTabUi(tab, TabManager.ActiveTabId < 0 ? TabManager.ActiveTabId : null);
        
        await ExtensionPopupWebview.EnsureCoreWebView2Async(TabManager.WebsiteEnvironment);
        await Instance.SetupExtensionSupport(ExtensionPopupWebview); // loads up those extensions!
        ExtensionPopupWebview.CoreWebView2.NewWindowRequested += (_, args2) => TabManager.OpenNewWindowAsTab(args2, null);
        
        ExtensionPopupWebview.CoreWebView2.WebMessageReceived += (_, args) =>
        {
            if (!args.Source.StartsWith("chrome-extension://")) return;
            try { ApplyExtensionPopupSize(args.TryGetWebMessageAsString()); }
            catch (Exception e) when (e is ArgumentException or JsonException) { } // not one of our size reports
        };
        ExtensionPopupWebview.NavigationCompleted += async (_, _) => await MeasureExtensionPopup();
        _popupSizePoll.Tick += async (_, _) => await MeasureExtensionPopup();
        ExtensionPopupRoot.Closed += (_, _) => _popupSizePoll.Stop();
        
        // _captureHelper = new VisualCaptureHelper(TabHolder, BlurredBackgroundGrid);

        // refresh all data
        HandleCacheChanged();

        if (FoxyFileManager.TakeDataFolderWarning() is { } warning)
            ShowToast("Data folder", warning, MaterialIconKind.FolderAlert, isError: true);
    }

    private void HomePageOnToggleEditMode(bool inEdit)
    {
        LeftBar.ToggleEditMode(inEdit, HomePage);
        TopBar.ToggleEditMode(inEdit);
    }

    private void TabManagerOnTabRemoved(WebviewTab tab)
    {
        TabHolder.Children.Remove(tab.Core);
        _prompts?.CancelTab(tab.Id);
    }

    private async void TabManagerOnTabAdded(WebviewTab tab)
    {
        //C only SwapActiveTabTo updates tab visibility, so a tab added without becoming active (backup restore onto home) would cover the page
        tab.Core.Visibility = tab.Id == TabManager.ActiveTabId ? Visibility.Visible : Visibility.Collapsed;
        TabHolder.Children.Add(tab.Core);

        //TODO handle this memory leak in the remove function above!
        
        tab.Info.PropertyChanged += (_, _) =>
        {
            if (TabManager.ActiveTabId == tab.Id)
            {
                RefreshCurrentTabUi(tab);
            }
        };
        
        await tab.InitializeTask;
        tab.Core.CoreWebView2.HistoryChanged += (_, _) =>
        {
            if (TabManager.ActiveTabId == tab.Id)
            {
                RefreshCurrentTabUi(tab);
            }
        };
    }

    //C Chrome's limits for extension popups
    private const double PopupMinSize = 25, PopupMaxWidth = 800, PopupMaxHeight = 600;

    // Claude Doc
    /// <summary>Set when an extension popup was clicked; it opens once its page has loaded and been measured.</summary>
    private bool _extensionPopupPending;
    private readonly DispatcherTimer _popupSizePoll = new() { Interval = TimeSpan.FromMilliseconds(250) };

    // Claude Doc
    /// <summary>
    /// Measures the popup page the way Chrome's popup autosize does: its min-content width and the height that gives.
    /// Reading <c>documentElement.scrollWidth/Height</c> doesn't work, since those never report less than the current
    /// viewport. Also installs observers that report later size changes through <c>chrome.webview.postMessage</c>.
    /// </summary>
    private const string PopupSizeScript =
        """
        (() => {
          if (location.protocol !== 'chrome-extension:' || !document.body) return null;
          if (!window.__foxyPopupSize) {
            const de = document.documentElement;
            const probe = document.createElement('div');
            probe.style.cssText = 'position:absolute;top:-9999px;width:100px;height:100px;overflow:scroll';
            de.appendChild(probe);
            const scrollbar = probe.offsetWidth - probe.clientWidth;
            probe.remove();

            let last = '', queued = false;
            const mo = new MutationObserver(() => queue());
            const measure = () => {
              const body = document.body, saved = de.getAttribute('style');
              de.style.setProperty('width', 'min-content', 'important');
              de.style.setProperty('height', 'auto', 'important');
              const rect = de.getBoundingClientRect(), cs = getComputedStyle(body);
              //C a body that scrolls on its own keeps its overflow inside; otherwise the overflow grows the popup
              const clips = getComputedStyle(de).overflowY !== 'visible' && cs.overflowY !== 'visible';
              const width = clips ? rect.width : Math.max(rect.width, body.scrollWidth + parseFloat(cs.marginLeft) + parseFloat(cs.marginRight));
              const height = clips ? rect.height : Math.max(rect.height, body.scrollHeight + parseFloat(cs.marginTop) + parseFloat(cs.marginBottom));
              if (saved === null) de.removeAttribute('style'); else de.setAttribute('style', saved);
              mo.takeRecords(); // the style flip above is not a page change
              const size = { type: 'foxyPopupSize', width: Math.ceil(width), height: Math.ceil(height), scrollbar, live: !!window.chrome?.webview };
              last = size.width + 'x' + size.height;
              return size;
            };
            const report = () => {
              queued = false;
              const prev = last, size = measure();
              if (last !== prev) window.chrome?.webview?.postMessage(JSON.stringify(size));
            };
            const queue = () => { if (!queued) { queued = true; requestAnimationFrame(report); } };
            const ro = new ResizeObserver(queue);
            ro.observe(de);
            ro.observe(document.body);
            mo.observe(de, { subtree: true, childList: true, attributes: true, characterData: true });
            document.fonts?.ready.then(queue);
            window.__foxyPopupSize = measure;
          }
          return window.__foxyPopupSize();
        })();
        """;

    private record PopupSize(string? type, double width, double height, double scrollbar, bool live);

    private async Task MeasureExtensionPopup()
    {
        PopupSize? size = null;
        if (ExtensionPopupWebview.CoreWebView2?.Source.StartsWith("chrome-extension://") == true)
        {
            try { size = ApplyExtensionPopupSize(await ExtensionPopupWebview.ExecuteScriptAsync(PopupSizeScript)); }
            catch (Exception e) { FoxyLogger.AddError(e); }
        }

        if (_extensionPopupPending)
        {
            _extensionPopupPending = false;
            ExtensionPopupRoot.IsOpen = true;
        }

        //C without the WebView2 bridge the page can't report its own size changes, so poll it while it is open
        if (size is { live: false } && ExtensionPopupRoot.IsOpen) _popupSizePoll.Start();
        else _popupSizePoll.Stop();
    }

    private PopupSize? ApplyExtensionPopupSize(string json)
    {
        var size = JsonSerializer.Deserialize<PopupSize>(json);
        if (size is not { type: "foxyPopupSize" } || size.width <= 0 || size.height <= 0) return null;

        var margin = ExtensionPopupRoot.Margin;
        var maxWidth = Math.Max(PopupMinSize, Math.Min(PopupMaxWidth, BorderGrid.ActualWidth - margin.Left - 8));
        var maxHeight = Math.Max(PopupMinSize, Math.Min(PopupMaxHeight, BorderGrid.ActualHeight - margin.Top - 8));
        //C content past a limit scrolls, and that scrollbar takes room from the other direction
        var width = size.width + (size.height > maxHeight ? size.scrollbar : 0);
        var height = size.height + (size.width > maxWidth ? size.scrollbar : 0);
        ExtensionPopupWebview.Width = Math.Clamp(width, PopupMinSize, maxWidth);
        ExtensionPopupWebview.Height = Math.Clamp(height, PopupMinSize, maxHeight);
        return size;
    }

    private async void RefreshCurrentTabUi(WebviewTab? tab, int? browserWindowId = null)
    {
        if (tab is not null)
        {
            TopBar.UpdateSearchBar(true, tab.Core.CanGoBack, tab.Core.CanGoForward, tab.Info.Url);
        }
        else if (browserWindowId is not null)
        {
            TopBar.UpdateSearchBar(false, false, false, string.Empty);
            HomePage.Visibility = browserWindowId == -1 ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = browserWindowId == -2 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
            throw new Exception("when refresh ui is called, tab or browserWindowId must be provided.");
    }

    private void TabManagerOnActiveTabChanged((int oldId, int newId) pair)
    {
        _extensionPopupPending = false;
        ExtensionPopupRoot.IsOpen = false;
        ExtensionPopupWebview.NavigateToString(""); // clear it
        _webContextMenu?.Close();
        _prompts?.SetActiveTab(pair.newId);
        
        if (pair.newId < 0)
            RefreshCurrentTabUi(null, pair.newId);
        else if (TabManager.TryGetTab(pair.newId, out var tab))
            RefreshCurrentTabUi(tab);
        else
            throw new Exception($"Could not retrieve tab with id '{pair.newId}'");
        
        //TODO test and fine tune
        
        // apply fading:
        // var fadeOut = AnimationBuilder.Create();
        // var fadeIn = AnimationBuilder.Create();
        
        // already collapsed?
        // if (pair.oldId == -1)
        // {
        //     Canvas.SetZIndex(HomePage, 0);
        //     fadeOut.Opacity(0, null, null, TimeSpan.FromSeconds(1), null, EasingType.Quintic, EasingMode.EaseOut, FrameworkLayer.Xaml);
        //     _ = fadeOut.StartAsync(HomePage);
        // }
        // else if (pair.oldId == -2)
        // {
        //     Canvas.SetZIndex(SettingsPage, 0);
        //     fadeOut.Opacity(0, null, null, TimeSpan.FromSeconds(1), null, EasingType.Quintic, EasingMode.EaseOut, FrameworkLayer.Xaml);
        //     _ = fadeOut.StartAsync(SettingsPage);
        // } 
        // else if (TabManager.TryGetTab(pair.oldId, out var oldTab))
        // {
        //     Canvas.SetZIndex(oldTab!.Core, 0);
        //     fadeOut.Opacity(0, null, null, TimeSpan.FromSeconds(1), null, EasingType.Quintic, EasingMode.EaseOut, FrameworkLayer.Xaml);
        //     _ = fadeOut.StartAsync(oldTab!.Core);
        // }
        
        // if (pair.newId == -1)
        // {
        //     Canvas.SetZIndex(HomePage, 1);
        //     fadeIn.Opacity(1, 0, null, TimeSpan.FromSeconds(0.25), null, EasingType.Quintic, EasingMode.EaseOut, FrameworkLayer.Xaml);
        //     _ = fadeIn.StartAsync(HomePage);
        // }
        // else if (pair.newId == -2)
        // {
        //     Canvas.SetZIndex(SettingsPage, 1);
        //     fadeIn.Opacity(1, 0, null, TimeSpan.FromSeconds(0.25), null, EasingType.Quintic, EasingMode.EaseOut, FrameworkLayer.Xaml);
        //     _ = fadeIn.StartAsync(SettingsPage);
        // } 
        // else if (TabManager.TryGetTab(pair.newId, out var newTab))
        // {
        //     Canvas.SetZIndex(newTab!.Core, 1);
        //     fadeIn.Opacity(1, 0, null, TimeSpan.FromSeconds(0.25), null, EasingType.Quintic, EasingMode.EaseOut, FrameworkLayer.Xaml);
        //     _ = fadeIn.StartAsync(newTab!.Core);
        // }
    }

    internal Theme CurrentTheme
    {
        get;
        set
        {
            field = value;
            ApplyTheme();
        }
    } = DefaultThemes.DaybreakFoxy;

    private void ApplyTheme()
    {
        //TODO: remove apply theme from other constructors
        TopBar.CurrentTheme = CurrentTheme;
        LeftBar.CurrentTheme = CurrentTheme;
        ContextMenuPopup.CurrentTheme = CurrentTheme;
        HomePage.CurrentTheme = CurrentTheme;
        ChatWindow.CurrentTheme = CurrentTheme;
        SettingsPage.CurrentTheme = CurrentTheme;

        SuggestionPanel.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
        SuggestionPanel.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
        
        PopupRoot.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
        PopupRoot.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
        
        CenterPopupRoot.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
        CenterPopupRoot.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
        CenterPopupCloseButton.CurrentTheme = CurrentTheme;
        
        Root.Background = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);
        BorderGrid.BorderBrush = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);
        TabHolder.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColor);

        ApplyWebUiTheme();
    }

    #region Window Events
    private void TopBar_OnMinimizeClicked()
    {
        // can't click this in fullscreen, and will cause an error if this runs while in fullscreen
        if (InFullscreen) return;
        
        this.Minimize();
    }

    private void TopBar_OnMaximizeClicked()
    {
        // can't click this in fullscreen, and will cause an error if this runs while in fullscreen
        if (InFullscreen) return;
        
        if (WindowState == WindowState.Maximized)
            this.Restore();
        else
            this.Maximize();
    }

    private void TopBar_OnCloseClicked()
    {
        this.Close();
    }
    
    private void TopBar_OnBorderlessToggled()
    {
        AppWindow.SetPresenter(TopBar.IsBorderless
            ? AppWindowPresenterKind.FullScreen
            : AppWindowPresenterKind.Default);
    }
    
    public enum BrowserWindowState
    {
        Minimized,
        Normal,
        Maximized,
        Borderless
    }

    public BrowserWindowState StateFromWindow()
    {
        if (InFullscreen) return BrowserWindowState.Borderless;

        return WindowState switch
        {
            WindowState.Minimized => BrowserWindowState.Minimized,
            WindowState.Normal => BrowserWindowState.Normal,
            WindowState.Maximized => BrowserWindowState.Maximized,
        };
    }

    private Rect _prevBounds = new Rect(0, 0, 400, 200);
    public Rect GetBounds()
    {
        if (InFullscreen) return _prevBounds;
        
        switch (WindowState)
        {
            case WindowState.Maximized:
            case WindowState.Minimized:
                return _prevBounds;
            case WindowState.Normal:
                var rect = new Rect(
                    AppWindow.Position.X,
                    AppWindow.Position.Y,
                    AppWindow.Size.Width,
                    AppWindow.Size.Height
                );
                _prevBounds = rect;
                return rect;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    // Claude Doc
    /// <summary>
    /// Moves and resizes the window to <paramref name="bounds"/>, in physical screen pixels (the same units
    /// <see cref="GetBounds"/> returns), fitted inside the work area of the nearest monitor.
    /// </summary>
    public void ApplyBounds(Rect bounds)
    {
        var rect = new Windows.Graphics.RectInt32((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);

        //C saved bounds can be from a monitor that is no longer attached, or larger than this one
        var work = DisplayArea.GetFromRect(rect, DisplayAreaFallback.Nearest).WorkArea;
        rect.Width = Math.Min(rect.Width, work.Width);
        rect.Height = Math.Min(rect.Height, work.Height);
        rect.X = Math.Clamp(rect.X, work.X, work.X + work.Width - rect.Width);
        rect.Y = Math.Clamp(rect.Y, work.Y, work.Y + work.Height - rect.Height);

        //C moving onto a monitor with a different DPI rescales the window, the second call lands the exact size
        AppWindow.MoveAndResize(rect);
        AppWindow.MoveAndResize(rect);

        //C a window restored maximized is never Normal for GetBounds to record its restore bounds
        _prevBounds = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public void ApplyWindowState(BrowserWindowState windowState)
    {
        TopBar.ToggleBorderless(windowState == BrowserWindowState.Borderless);
        
        AppWindow.SetPresenter(TopBar.IsBorderless
            ? AppWindowPresenterKind.FullScreen
            : AppWindowPresenterKind.Default);
        
        switch (windowState)
        {
            case BrowserWindowState.Minimized:
                this.Minimize();
                break;
            case BrowserWindowState.Normal:
                this.Restore();
                break;
            case BrowserWindowState.Maximized:
                this.Maximize();
                break;
            case BrowserWindowState.Borderless:
                //already there with the fullscreen presenter
                break;
        }
    }
    
    internal bool InFullscreen => AppWindow.Presenter is FullScreenPresenter;
    #endregion

    private async void TopBar_OnSearchClicked(string searchText)
    {
        if (TabManager.ActiveTabId >= 0 && TabManager.TryGetTab(TabManager.ActiveTabId, out var tab))
        {
            await tab!.NavigateOrSearch(searchText);
        }
        else if (TabManager.ActiveTabId < 0)
        {
            TabManager.SwapActiveTabTo(TabManager.AddTab(searchText));
        }
    }

    private void TopBar_OnMenuClicked()
    {
        if (HomePage.InEditMode)
        {
            return;
        }
        
        _contextmenuUsedForSearchEngine = false;

        List<FContextMenu.MenuItem> items =
        [
            new(new MaterialIcon {Kind = MaterialIconKind.CardMultiple}, 1, "Instances", InstancesClick),
            new(new MaterialIcon {Kind = MaterialIconKind.BookmarkMultiple}, 1, "Bookmarks", BookmarkClick),
            new(new MaterialIcon {Kind = MaterialIconKind.History}, 1, "History", HistoryClick),
            new(new MaterialIcon {Kind = MaterialIconKind.Download}, 1, "Downloads", DownloadClick),
            new(new MaterialIcon {Kind = MaterialIconKind.Puzzle}, 1, "Extensions", ExtensionsClick, false),
        ];
        if (Instance.Settings.AiEnabled)
            items.Insert(0, new(new MaterialIcon {Kind = MaterialIconKind.Assistant}, 1, "AI Assistant", AssistantClick));

        ContextMenuPopup.Margin = new Thickness(32, 28, 0, 0);
        switch (TabManager.ActiveTabId)
        {
            case >= 0:
                ContextMenuPopup.SetItems(items
                    .Prepend(new(new MaterialIcon { Kind = MaterialIconKind.Cogs }, 1, "Settings",
                        () => TabManager.SwapActiveTabTo(-2)))
                    .Concat(PageMenuItems()));
                break;
            case -1:
                ContextMenuPopup.SetItems(items
                    .Prepend(new(new MaterialIcon {Kind = MaterialIconKind.Cogs}, 1, "Settings",() => TabManager.SwapActiveTabTo(-2)))
                    .Append(new(new MaterialIcon {Kind = MaterialIconKind.Pencil}, 1, "Edit Home", EditHomeClick)));
                break;
            case -2:
                ContextMenuPopup.SetItems(items);
                break;
        }
    }

    private IEnumerable<FContextMenu.MenuItem> PageMenuItems()
    {
        if (!TabManager.TryGetTab(TabManager.ActiveTabId, out var tab) || tab!.Core.CoreWebView2 is not { } core)
            return [];

        return
        [
            new(new MaterialIcon { Kind = MaterialIconKind.FindReplace }, 1, "Find in page", async () =>
            {
                try
                {
                    var options = core.Environment.CreateFindOptions();
                    options.FindTerm = "";
                    options.ShouldHighlightAllMatches = true;
                    await core.Find.StartAsync(options);
                }
                catch (Exception e) { FoxyLogger.AddError(e); }
            }),
            new(new MaterialIcon { Kind = MaterialIconKind.Printer }, 1, "Print", () => core.ShowPrintUI(CoreWebView2PrintDialogKind.Browser)),
            new(new MaterialIcon { Kind = MaterialIconKind.ContentSave }, 1, "Save page as", async () =>
            {
                try { await core.ShowSaveAsUIAsync(); }
                catch (Exception e) { FoxyLogger.AddError(e); }
            }),
            new(new MaterialIcon { Kind = MaterialIconKind.ShareVariant }, 1, "Share", () => SharePage(core.Source, core.DocumentTitle)),
        ];
    }

    private (string url, string title)? _pageToShare;
    private bool _shareHooked;

    private void SharePage(string url, string title)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) return;
        try
        {
            var hwnd = this.GetWindowHandle();
            if (!_shareHooked)
            {
                global::Windows.ApplicationModel.DataTransfer.DataTransferManagerInterop.GetForWindow(hwnd).DataRequested += (_, args) =>
                {
                    if (_pageToShare is not { } page) return;
                    args.Request.Data.Properties.Title = string.IsNullOrWhiteSpace(page.title) ? page.url : page.title;
                    args.Request.Data.SetWebLink(new Uri(page.url));
                };
                _shareHooked = true;
            }
            _pageToShare = (url, title);
            global::Windows.ApplicationModel.DataTransfer.DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
        }
        catch (Exception e)
        {
            FoxyLogger.AddError(e);
            ShowToast("Share", "Windows couldn't open the share window.", MaterialIconKind.ShareVariant);
        }
    }

    private void AssistantClick()
    {
        ChatWindow.Visibility = Visibility.Visible;
        TabHolder.Margin = TabHolder.Margin with { Right = 450 };
    }

    private void InstancesClick()
    {
        PopupContainer.Children.Clear();
        AppServer.Instances
            .Select(i =>
                {
                    var ic = new InstanceCard(i, i.Name == Instance.Name);
                    ic.OpenRequested += async () => await i.CreateWindow();
                    ic.TransferRequested += async () =>
                    {
                        var currentPos = new Rect(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
                        var tabUrls = TabManager
                            .GetAllTabs()
                            .Select(t => t.Value.Info.Url)
                            .ToArray();
                        
                        await i.CreateWindow(tabUrls, currentPos, StateFromWindow());
                        this.Close();
                    };
                    ic.CurrentTheme = CurrentTheme;
                    return ic;
                })
            .ToList()
            .ForEach(ic => PopupContainer.Children.Add(ic));
        
        PopupRootRoot.IsOpen = true;
    }

    private void BookmarkClick()
    {
        PopupContainer.Children.Clear();
        Instance.Bookmarks
            .Select(b =>
                {
                    var bc = new BookmarkCard(b);
                    bc.NoteChanged += s => b.Note = s;
                    bc.RemoveRequested += () =>
                    {
                        Instance.Bookmarks.Remove(b);
                        PopupContainer.Children.Remove(bc);
                    };
                    bc.OnClick += () =>
                    {
                        TabManager.SwapActiveTabTo(TabManager.AddTab(b.Url));
                        PopupRootRoot.IsOpen = false;
                    };
                    bc.CurrentTheme = CurrentTheme;
                    return bc;
                })
            .ToList()
            .ForEach(bc => PopupContainer.Children.Add(bc));
        
        PopupRootRoot.IsOpen = true;
    }

    private void HistoryClick() => ShowHistoryPanel();

    private void ExtensionsClick()
    {
        //C disabled extensions have no running popup, so they are left out of the quick menu
        var items = Instance
            .GetSavedExtensions()
            .Where(e => e.IsEnabled)
            .Select(e =>
            {
                var popup = ExtensionManager.GetPopupPage(e.Manifest);
                return new FContextMenu.MenuItem(
                    ExtensionManager.CreateIconElement(e, 20), 1,
                    ExtensionManager.GetDisplayName(e),
                    () =>
                    {
                        if (popup is null)
                        {
                            ShowToast(ExtensionManager.GetDisplayName(e), "This extension has no popup. Its options are in Settings > Extensions.", MaterialIconKind.Puzzle);
                            return;
                        }
                        //C opened by MeasureExtensionPopup once loaded, so it appears at its own size
                        _extensionPopupPending = true;
                        ExtensionPopupWebview.CoreWebView2.Navigate($"chrome-extension://{e.Id}/{popup}");
                    });
            })
            .ToList();

        if (items.Count == 0)
            items.Add(new FContextMenu.MenuItem(new MaterialIcon { Kind = MaterialIconKind.Store }, 1, "Get extensions",
                () => TabManager.SwapActiveTabTo(TabManager.AddTab(ExtensionManager.ChromeWebStoreUrl))));

        items.Add(FContextMenu.MenuItem.Separator());
        items.Add(new FContextMenu.MenuItem(new MaterialIcon { Kind = MaterialIconKind.Cog }, 1, "Manage extensions",
            () => OpenSettings(SettingsCategory.Extensions)));

        ContextMenuPopup.SetItems(items);
    }

    public void OpenSettings(SettingsCategory category)
    {
        TabManager.SwapActiveTabTo(-2);
        SettingsPage.ScrollToCategory(category);
    }

    private void DownloadClick()
    {
        if (Instance.Settings.ThemedDownloads)
            ShowDownloadsPanel();
        else if (TabManager.TryGetTab(TabManager.ActiveTabId, out var tab))
            if (tab.Core.CoreWebView2.IsDefaultDownloadDialogOpen) 
                tab.Core.CoreWebView2.CloseDefaultDownloadDialog();
            else tab.Core.CoreWebView2.OpenDefaultDownloadDialog();
    }

    private void EditHomeClick()
    {
        HomePage.EditModeStart();
    }

    private bool _contextmenuUsedForSearchEngine;
    private void TopBar_OnEngineClicked()
    {
        if (_contextmenuUsedForSearchEngine)
        {
            ContextMenuPopup.SetItems([]);
            _contextmenuUsedForSearchEngine = false;
            return;
        }
        
        _contextmenuUsedForSearchEngine = true;
        
        ContextMenuPopup.Margin = new Thickness( TopBar.GetSearchEngineOffset() - 4, 4, 0, 0);
        ContextMenuPopup.SetItems(
            Enum.GetValues<InfoGetter.SearchEngine>()
                .Where(e => e != Instance.Cache.CurrentSearchEngine)
                .Prepend(Instance.Cache.CurrentSearchEngine)
                .Select(se => 
                    new FContextMenu.MenuItem(
                        new Image
                        {
                            Source = new BitmapImage(new Uri(InfoGetter.GetSearchEngineIcon(se))),
                            Stretch = Stretch.Uniform,
                            VerticalAlignment = VerticalAlignment.Stretch,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                        },
                        2,
                        null/*InfoGetter.GetSearchEngineName(se)*/, 
                        () => SearchEngineChangeRequested?.Invoke(se)
                    )
                ), 22);
    }

    private void TopBar_OnBackClicked()
    {
        if (TabManager.TryGetTab(TabManager.ActiveTabId, out var tab))
            tab!.Core.GoBack();
    }
    
    private void TopBar_OnForwardClicked()
    {
        if (TabManager.TryGetTab(TabManager.ActiveTabId, out var tab))
            tab!.Core.GoForward();
    }

    private void TopBar_OnRefreshClicked()
    {
        if (TabManager.TryGetTab(TabManager.ActiveTabId, out var tab))
            tab!.Core.Reload();
    }

    private void ContextMenuPopup_OnOnClose()
    {
        _contextmenuUsedForSearchEngine = false;
    }
    
    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // _captureHelper?.Dispose();
        ShutdownWebUi();
    }

    private void MainWindow_OnWindowStateChanged(object? sender, WindowState e)
    {
        TopBar.UpdateMaximizeRestore(e);
    }
    
    private void MainWindow_OnActivated(object sender, WindowActivatedEventArgs args)
    {
        
    }

    private void TopBar_OnToggleSidebarLock(bool isLocked)
    {
        Instance.Cache.LeftBarLocked = isLocked;
    }

    private void CenterPopupCloseButton_OnOnClick(object sender, RoutedEventArgs e)
    {
        CenterPopupRootRoot.IsOpen = false;
    }

    public event Action? PopupClosed;
    private void CenterPopupRootRoot_OnClosed(object? sender, object e)
    {
        PopupClosed?.Invoke();
    }
    
    public void OpenSettings(List<ISetting> settings)
    {
        CenterPopupContainer.Children.Clear();
        
        if (settings.Count == 0) return;
        
        settings.ForEach(s =>
            {
                var e = s.GetEditor(this);
                e.CurrentTheme = CurrentTheme;
                CenterPopupContainer.Children.Add(e);
            });
        
        CenterPopupRootRoot.IsOpen = true;
    }

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(4) };
    private int _suggestionRequest;

    private async void TopBar_OnSearchTextChanged(string searchText)
    {
        var request = ++_suggestionRequest;

        if (string.IsNullOrWhiteSpace(searchText))
        {
            SuggestionPanel.Visibility = Visibility.Collapsed;
            return;
        }

        //C local results first so they show instantly; network suggestions fill in afterwards
        FillSuggestionSection(OpenTabsLabel, OpenTabSuggestions,
            TabManager.Groups.SelectMany(g => g.Tabs)
                .Concat(TabManager.Tabs)
                .Where(t => t.Info.Title.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                            t.Info.Url.Contains(searchText, StringComparison.CurrentCultureIgnoreCase))
                .Select(t => (UrlToImageControlConverter.StaticConvert(t.Info.FavIconUrl), $"{t.Info.Title} - {t.Info.Url}", (Action)(() => TabManager.SwapActiveTabTo(t.Id)))));

        var history = Instance.Settings.HistorySuggestionsEnabled
            ? Instance.History.Search(searchText, 5)
                .Select(h => (UrlToImageControlConverter.StaticConvert(h.FavIconUrl),
                    string.IsNullOrWhiteSpace(h.Title) ? h.Url : $"{h.Title} - {h.Url}",
                    (Action)(() => TopBar_OnSearchClicked(h.Url))))
                .ToList()
            : [];
        if (history.Count > 0)
            history.Add((new MaterialIcon { Kind = MaterialIconKind.TextBoxSearch }, $"Search history for \u201c{searchText.Trim()}\u201d",
                () => ShowHistoryPanel(searchText)));
        FillSuggestionSection(HistorySuggestionsLabel, HistorySuggestions, history);

        FillSuggestionSection(BookmarkSuggestionsLabel, BookmarkSuggestions,
            Instance.Bookmarks
                .Where(b => b.Url.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
                            || b.Title.Contains(searchText, StringComparison.CurrentCultureIgnoreCase))
                .Select(b => (UrlToImageControlConverter.StaticConvert(b.FavIconUrl), $"{b.Title} - {b.Url}", (Action)(() => TopBar_OnSearchClicked(b.Url)))));

        FillSuggestionSection(PinSuggestionsLabel, PinSuggestions,
            Instance.Pins
                .Where(b => b.Url.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
                            || b.Title.Contains(searchText, StringComparison.CurrentCultureIgnoreCase))
                .Select(p => (UrlToImageControlConverter.StaticConvert(p.FavIconUrl), $"{p.Title} - {p.Url}", (Action)(() => TopBar_OnSearchClicked(p.Url)))));

        FillSuggestionSection(SearchCompletionsLabel, SearchCompletions, []);
        ShowSuggestionPanel();

        if (!Instance.Settings.SearchSuggestionsEnabled) return;

        List<string> suggestions = [];
        try
        {
            var json = await _httpClient.GetStringAsync(InfoGetter.GetSearchCompletionUrl(searchText));
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            if (data.GetArrayLength() > 1)
                foreach (var item in data[1].EnumerateArray())
                    if (item.GetString() is { } suggestion)
                        suggestions.Add(suggestion);
        }
        catch (Exception e)
        {
            //C offline or rate limited: the local results are still useful, so this is not an error
            Debug.WriteLine(e);
        }

        //C the user kept typing (or cleared the box) while this request was in flight
        if (request != _suggestionRequest) return;

        FillSuggestionSection(SearchCompletionsLabel, SearchCompletions,
            suggestions.Select(suggestion => ((UIElement?)null, suggestion, (Action)(() => TopBar_OnSearchClicked(suggestion)))));
        ShowSuggestionPanel();
    }

    private void ShowSuggestionPanel()
    {
        var anyResults = new[] { OpenTabSuggestions, SearchCompletions, HistorySuggestions, BookmarkSuggestions, PinSuggestions }
            .Any(section => section.Children.Count > 0);
        if (!anyResults)
        {
            SuggestionPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var offsetWidth = TopBar.GetSearchBarOffsetAndWidth();
        SuggestionPanel.Margin = new Thickness(offsetWidth.offset, 
            SuggestionPanel.Margin.Top, SuggestionPanel.Margin.Right, 
            SuggestionPanel.Margin.Bottom);
        SuggestionPanel.Width = offsetWidth.width;
        SuggestionPanel.Visibility = Visibility.Visible;
    }

    private void FillSuggestionSection(FrameworkElement label, StackPanel section, IEnumerable<(UIElement? icon, string text, Action onClick)> entries)
    {
        section.Children.Clear();
        foreach (var (icon, text, onClick) in entries)
        {
            var button = new FTextButton {
                Icon = icon,
                ButtonText = text, CurrentTheme = CurrentTheme,
                CornerRadius = new CornerRadius(8), Margin = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            button.OnClick += (_, _) =>
            {
                HideSuggestions();
                onClick();
            };
            section.Children.Add(button);
        }
        label.Visibility = section.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void TopBar_OnSearchBarUnfocused() => HideSuggestions();

    private void HideSuggestions()
    {
        _suggestionRequest++; // a network response still in flight must not reopen the panel
        SuggestionPanel.Visibility = Visibility.Collapsed;
    }

    private void TopBar_OnUpdateClicked()
    {
        // throw new NotImplementedException();
        //TODO open MS Store
    }

    private void HomePage_OnHomeImageUrlChanged(Uri? obj)
    {
        BackImage.Source = obj is null ? null : new BitmapImage(new Uri(obj.ToString())); { };
    }
}
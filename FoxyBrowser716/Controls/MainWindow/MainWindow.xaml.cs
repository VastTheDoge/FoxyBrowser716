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

    public Action<InfoGetter.SearchEngine> SearchEngineChangeRequested;
    
    // private VisualCaptureHelper? _captureHelper;
    
    private MainWindow()
    {
        InitializeComponent();

        try
        {
            // the exe's folder, which is also the install folder when packaged (Package.Current throws unpackaged)
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
        ExtensionPopupWebview.CoreWebView2.NewWindowRequested +=
            (_, args2) =>
            {
                TabManager.SwapActiveTabTo(TabManager.AddTab(args2.Uri));
                args2.Handled = true;
            };
        
        ExtensionPopupWebview.NavigationCompleted += async (_, _) =>
        {
            //TODO: not working as expected (crazy high width + height)
            /*var result = await ExtensionPopupWebview.ExecuteScriptAsync(
                """
                (function(){
                    return {
                        width: document.documentElement.scrollWidth,
                        height: document.documentElement.scrollHeight
                    };
                })();
                """
                );*/
            //var size = JsonSerializer.Deserialize<PopupSize>(result);
            ExtensionPopupWebview.Width = 300;//size.width;
            ExtensionPopupWebview.Height = 500; //size.height;
        };
        
        // _captureHelper = new VisualCaptureHelper(TabHolder, BlurredBackgroundGrid);

        // refresh all data
        HandleCacheChanged();
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

    private record PopupSize(double width, double height);
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
            new(new MaterialIcon {Kind = MaterialIconKind.Assistant}, 1, "AI Assistant", AssistantClick),
            new(new MaterialIcon {Kind = MaterialIconKind.CardMultiple}, 1, "Instances", InstancesClick),
            new(new MaterialIcon {Kind = MaterialIconKind.BookmarkMultiple}, 1, "Bookmarks", BookmarkClick),
            new(new MaterialIcon {Kind = MaterialIconKind.History}, 1, "History", HistoryClick),
            new(new MaterialIcon {Kind = MaterialIconKind.Download}, 1, "Downloads", DownloadClick),
            new(new MaterialIcon {Kind = MaterialIconKind.Puzzle}, 1, "Extensions", ExtensionsClick, false),
        ];

        ContextMenuPopup.Margin = new Thickness(32, 28, 0, 0);
        switch (TabManager.ActiveTabId)
        {
            case >= 0:
                ContextMenuPopup.SetItems(items
                    .Prepend(new(new MaterialIcon { Kind = MaterialIconKind.Cogs }, 1, "Settings",
                        () => TabManager.SwapActiveTabTo(-2))));
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
        // disabled extensions have no running popup, so they are left out of the quick menu
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
                        ExtensionPopupWebview.CoreWebView2.Navigate($"chrome-extension://{e.Id}/{popup}");
                        ExtensionPopupRoot.IsOpen = true;
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

    /// <summary>Shows the settings page scrolled to <paramref name="category"/>.</summary>
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

        // local results first so they show instantly; network suggestions fill in afterwards
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
            // offline or rate limited: the local results are still useful, so this is not an error
            Debug.WriteLine(e);
        }

        // the user kept typing (or cleared the box) while this request was in flight
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
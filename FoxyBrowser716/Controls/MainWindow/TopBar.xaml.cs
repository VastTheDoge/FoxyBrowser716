
// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

using System.Net.Http;
using System.Threading;
using Material.Icons;
using Material.Icons.WinUI3;
using WinUIEx;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Windows.Win32.Graphics.Dwm;
using FoxyBrowser716.DataManagement;


namespace FoxyBrowser716.Controls.MainWindow;

public sealed partial class TopBar : UserControl
{
    public bool IsBorderless { get; private set; }
    public event Action? BorderlessToggled;
    
    public event Action? MenuClicked;
    
    public event Action? MinimizeClicked;
    public event Action? MaximizeClicked;
    public event Action? CloseClicked;
    
    public event Action? BackClicked;
    public event Action? ForwardClicked;
    public event Action? RefreshClicked;
    public event Action<string>? SearchClicked;
    public event Action? EngineClicked;

    public event Action<string>? SearchTextChanged;
    
    public event Action? UpdateClicked;
    
    public event Action? SearchBarUnfocused;
    
    public event Action<bool>? RequestSnapLayout; //TODO implement
    
    private Timer refreshTimer;
    
    internal Theme CurrentTheme
    {
        get;
        set
        {
            field = value;
            ApplyTheme();
        }
    } = DefaultThemes.DarkMode;

    private void ApplyTheme()
    {
        Root.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColor);
        Root.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
        
        UpdateButton.CurrentTheme = CurrentTheme with { PrimaryAccentColor = CurrentTheme.PrimaryHighlightColor, SecondaryAccentColor = CurrentTheme.SecondaryHighlightColor};
        ButtonMenu.CurrentTheme = CurrentTheme;
        ButtonBorderlessToggle.CurrentTheme = CurrentTheme;
        ButtonMinimize.CurrentTheme = CurrentTheme;
        ButtonMaximize.CurrentTheme = CurrentTheme;
        ButtonClose.CurrentTheme = CurrentTheme with
        {
            SecondaryForegroundColor = CurrentTheme.NoColor,
            PrimaryHighlightColor = CurrentTheme.NoColor
        };
        ButtonBack.CurrentTheme = CurrentTheme;
        ButtonForward.CurrentTheme = CurrentTheme;
        ButtonRefresh.CurrentTheme = CurrentTheme;
        ButtonSearch.CurrentTheme = CurrentTheme;
        ButtonEngine.CurrentTheme = CurrentTheme;
        ButtonToggleSidebarExpand.CurrentTheme = CurrentTheme;
        
        SearchBackground.Background = new SolidColorBrush(CurrentTheme.PrimaryAccentColorSlightTransparent);
        SearchBackground.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryAccentColorSlightTransparent);
        SearchBox.SelectionHighlightColor = new SolidColorBrush(CurrentTheme.SecondaryHighlightColorVeryTransparent);
        SearchBox.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
    }
    
    public TopBar()
    {
        InitializeComponent();
        
        ApplyTheme();
        
        refreshTimer = new Timer(RefreshTimer_Tick, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    private void BorderlessToggle_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleBorderless(!IsBorderless);
        
    }

    public void ToggleBorderless(bool isBorderless)
    {
        IsBorderless = isBorderless;
        
        BorderlessToggled?.Invoke();
        ButtonBorderlessToggle.ForceHighlight = IsBorderless;
        if (ButtonBorderlessToggle.Content is MaterialIcon icon)
            icon.Kind = IsBorderless ? MaterialIconKind.ArrowCollapse : MaterialIconKind.ArrowExpand;
        
        DragZone.Visibility = IsBorderless ? Visibility.Collapsed : Visibility.Visible;
        ButtonMaximize.Visibility = IsBorderless ? Visibility.Collapsed : Visibility.Visible;
        ButtonMinimize.Visibility = IsBorderless ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ButtonMenu_OnClick(object sender, RoutedEventArgs e)
    {
        MenuClicked?.Invoke();
    }

    private void ButtonRefresh_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshClicked?.Invoke();
    }

    private void ButtonBack_OnClick(object sender, RoutedEventArgs e)
    {
        BackClicked?.Invoke();
    }

    private void ButtonForward_OnClick(object sender, RoutedEventArgs e)
    {
        ForwardClicked?.Invoke();
    }

    private void ButtonSearch_OnClick(object sender, RoutedEventArgs e)
    {
        var searchText = SearchBox.Text.Trim();
        if (!string.IsNullOrEmpty(searchText))
        {
            // should update later with a webview2 getting the focus, but doing so now can't hurt:
            SearchBackground.Focus(FocusState.Programmatic);
            
            SearchClicked?.Invoke(searchText);
        }
    }

    private void ButtonEngine_OnClick(object sender, RoutedEventArgs e)
    {
        EngineClicked?.Invoke();
    }

    private void ButtonMinimize_OnClick(object sender, RoutedEventArgs e)
    {
        MinimizeClicked?.Invoke();
    }

    private void ButtonMaximize_OnClick(object sender, RoutedEventArgs e)
    {
        MaximizeClicked?.Invoke();
    }

    private void ButtonClose_OnClick(object sender, RoutedEventArgs e)
    {
        CloseClicked?.Invoke();
    }

    private void SearchBox_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            ButtonSearch_OnClick(this, e);
        }
    }

    private void SearchBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        ChangeColorAnimation(SearchBackground.BorderBrush, CurrentTheme.SecondaryAccentColorSlightTransparent);
        ChangeColorAnimation(SearchBackground.Background, CurrentTheme.PrimaryAccentColorSlightTransparent);
        SearchBarUnfocused?.Invoke();
    }

    private void SearchBox_OnGotFocus(object sender, RoutedEventArgs e)
    {
        ChangeColorAnimation(SearchBackground.BorderBrush, CurrentTheme.PrimaryHighlightColor);
        ChangeColorAnimation(SearchBackground.Background, CurrentTheme.PrimaryBackgroundColorSlightTransparent);
    }

    public void UpdateSearchBar(bool showRefresh, bool showBack, bool showForward, string? searchText=null)
    {
        ButtonRefresh.Visibility = showRefresh ? Visibility.Visible : Visibility.Collapsed;
        ButtonBack.Visibility = showBack ? Visibility.Visible : Visibility.Collapsed;
        ButtonForward.Visibility = showForward ? Visibility.Visible : Visibility.Collapsed;

        if (searchText is { } text)
        {
            LastChanged = text;
            SearchBox.Text = text;
        }
    }

    public double GetSearchEngineOffset()
    {
        var transform = ButtonEngine.TransformToVisual(null);
        return transform.TransformPoint(new Point(0, 0)).X;
    }

    public (double offset, double width) GetSearchBarOffsetAndWidth()
    {
        var transform = SearchBackground.TransformToVisual(null);
        return (transform.TransformPoint(new Point(0, 0)).X, SearchBackground.ActualWidth);
    }
    
    public void UpdateSearchEngineIcon(InfoGetter.SearchEngine se)
    {
        ButtonEngine.Content = new Image
        {
            Source = new BitmapImage(new Uri(InfoGetter.GetSearchEngineIcon(se))),
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    public void UpdateMaximizeRestore(WindowState state)
    {
        if (ButtonMaximize.Content is MaterialIcon icon)
            icon.Kind = state == WindowState.Maximized ? MaterialIconKind.CheckboxMultipleBlankOutline : MaterialIconKind.Maximize;
    }

    public void ToggleEditMode(bool inEdit)
    {
        SearchBackground.Visibility = inEdit ? Visibility.Collapsed : Visibility.Visible;
        ButtonMenu.Visibility = inEdit ? Visibility.Collapsed : Visibility.Visible;
    }
    
    private void ButtonToggleSidebarExpand_OnOnClick(object sender, RoutedEventArgs e)
    {
        ToggleSidebarLock?.Invoke(!SidebarLocked);
    }

    public event Action<bool>? ToggleSidebarLock;
    private bool SidebarLocked;
    public void SetSidebarLockedState(bool sl)
    {
        SidebarLocked = sl;
        ButtonToggleSidebarExpand.ForceHighlight = !SidebarLocked;
    }
    
    private JsonSerializerOptions serializerOptions = new() { PropertyNameCaseInsensitive = true };
    private HttpClient client = new();
    
    private void RefreshTimer_Tick(object? state)
    {
        AppServer.UiDispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                using var response = await client.GetAsync(InfoGetter.LatestVersionApiUrl);
                response.EnsureSuccessStatusCode();
            
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<VersionInfo>(json, serializerOptions);
                
                if (result is { Version: not null } && result.Version.Replace("v", string.Empty) != AppServer.VersionInfo.Version.Replace("v", string.Empty))
                {
                    var normalizedCurrentVersion = AppServer.VersionInfo.Version.StartsWith('v')
                        ? AppServer.VersionInfo.Version
                        : $"v{AppServer.VersionInfo.Version}";
                    
                    var normalizedNewVersion = result.Version.StartsWith('v') 
                        ? result.Version 
                        : $"v{result.Version}";
                    
                    var newVerNum = result.Version
                        .Replace("v", string.Empty)
                        .Split('.')
                        .Select(int.Parse)
                        .ToArray();
                    
                    var curVerNum = AppServer.VersionInfo.Version
                        .Replace("v", string.Empty)
                        .Split('.')
                        .Select(int.Parse)
                        .ToArray();

                    for (var i = 0; i < newVerNum.Length && i < curVerNum.Length; i++)
                    {
                        // on old version
                        if (newVerNum[i] > curVerNum[i])
                        {
                            UpdateButton.ButtonText = $"Update Available: {normalizedCurrentVersion} → {normalizedNewVersion}";
                            UpdateButton.Visibility = Visibility.Visible;
                            return;
                        }
                        if (newVerNum[i] < curVerNum[i]) 
                            break; // on new version
                    }
                    
                    // on new version
                    UpdateButton.ButtonText = $"Dev Version: {normalizedCurrentVersion} ← {normalizedNewVersion}";
                    UpdateButton.Visibility = Visibility.Visible;
                }
                else
                {
                    // do not worry about swaping back, the app needs a restart to update.
                }
            }
            catch (Exception ex)
            {
                ErrorHandeler.FoxyLogger.AddError(ex);
            }
        });
    }

    private string? LastChanged = null;
    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchBox.Text == LastChanged && SearchBox.Text != string.Empty) return;
        SearchTextChanged?.Invoke(SearchBox.Text);
    }

    private void UpdateButton_OnOnClick(object sender, RoutedEventArgs e)
    {
        UpdateClicked?.Invoke();
            
    }
}

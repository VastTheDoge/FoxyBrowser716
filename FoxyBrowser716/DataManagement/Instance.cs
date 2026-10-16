using System.ComponentModel;
using Windows.Foundation;
using FoxyBrowser716.Controls.MainWindow;
using FoxyBrowser716.DataObjects;
using FoxyBrowser716.DataObjects.Basic;
using FoxyBrowser716.DataObjects.Complex;
using FoxyBrowser716.DataObjects.Settings;
using FoxyBrowser716.ErrorHandeler;
using FoxyBrowser716.StaticData;
using WinUIEx;

namespace FoxyBrowser716.DataManagement;

public partial class Instance : ObservableObject
{
	public string Name { get; private set; }
	
	//TODO:
	public DefaultThemes DefaultThemeObject { get; } = new();
	
	public BrowserSettings Settings => _settings.Item;
	public InstanceCache Cache => _cache.Item;
	public ObservableCollection<WebsiteInfo> Pins => _pins.Items;
	public ObservableCollection<WebsiteInfo> Bookmarks => _bookmarks.Items;

	public HistoryManager History { get; private set; } = null!;
	public SitePermissionManager SitePermissions { get; private set; } = null!;
	public DownloadManager Downloads { get; private set; } = null!;

	private FoxyAutoSaverField<ObservableCollection<string>> PinnedExtensionsList;

	[ObservableProperty]
	public partial Lazy<ObservableCollection<Extension>> pinnedExtensions { get; set; }
	private ObservableCollection<Extension> GetAndVerifyPinnedExtensions()
	{
		return []; //TODO: needs a webview to verify, but need this for a window?
	}

	private FoxyAutoSaverField<BrowserSettings> _settings
	{
		get;
		set => SetProperty(ref field, value, nameof(Settings));
	}

	private FoxyAutoSaverField<InstanceCache> _cache
	{
		get;
		set => SetProperty(ref field, value, nameof(Cache));
	}

	[ObservableProperty] public partial FoxyAutoSaverList<WebsiteInfo> _pins { get; set; }

	private FoxyAutoSaverList<WebsiteInfo> _bookmarks
	{
		get;
		set => SetProperty(ref field, value, nameof(Bookmarks));
	}
	
	public bool IsPinned(string url) => FindPage(Pins, url) is not null;
	public bool IsBookmarked(string url) => FindPage(Bookmarks, url) is not null;
	public void TogglePin(WebsiteInfo page) => TogglePage(Pins, page);
	public void ToggleBookmark(WebsiteInfo page) => TogglePage(Bookmarks, page);

	private static WebsiteInfo? FindPage(ObservableCollection<WebsiteInfo> list, string url) =>
		list.FirstOrDefault(p => string.Equals(p.Url, url, StringComparison.CurrentCultureIgnoreCase));

	//C stores a copy: the tab's own WebsiteInfo would make the pin/bookmark follow the tab as it navigates
	private static void TogglePage(ObservableCollection<WebsiteInfo> list, WebsiteInfo page)
	{
		if (FindPage(list, page.Url) is { } existing)
			list.Remove(existing);
		else
			list.Add(new WebsiteInfo
			{
				Url = page.Url,
				Title = page.Title,
				FavIconUrl = page.FavIconUrl,
				DateAdded = DateTime.Now,
			});
	}

	public bool IsPrimaryInstance => Name == AppServer.PrimaryInstance.Name;

	// Claude Doc
	/// <summary>
	/// Every tab of this instance runs InPrivate (<see cref="BrowserSettings.PrivateBrowsing"/>, read once at startup so
	/// all of the instance's windows share one mode).
	/// </summary>
	public bool IsPrivate { get; private set; }

	public LinkedList<MainWindow> Windows = [];
	public MainWindow? CurrentWindow => Windows.FirstOrDefault();
	
	public event Action<Theme>? ThemeUpdated;

	public Theme CurrentTheme
	{
		get;
		private set
		{
			SetProperty(ref field, value);
			foreach (var window in Windows)
				window.CurrentTheme = value;
		}
	} = DefaultThemes.DarkMode;

	public event Action<Instance>? Focused;
	
	private Instance()
	{}

	private void HandleSettingsChange(object? sender, PropertyChangedEventArgs propertyChangedEventArgs)
	{
		switch (propertyChangedEventArgs.PropertyName)
		{
			case nameof(BrowserSettings.ThemeName):
				if (DefaultThemeObject.Themes.TryGetValue(Settings.ThemeName, out var theme)) CurrentTheme = theme;
				break;
			default:
				break;
		}
	}
	
	public static async Task<Instance> Create(string name, InstanceRetoreData? retoreData = null)
	{
		var newInstance = new Instance();
		await newInstance.Initialize(name, retoreData);
		return newInstance;
	}

	public async Task Initialize(string name, InstanceRetoreData? retoreData = null)
	{
		var isNewInstance = false;
		
		Name = name;
		
		_cache = new(() => new InstanceCache(), "Cache.json", FoxyFileManager.FolderType.Data, Name);
		_settings = new(() => new BrowserSettings(), "Settings.json", FoxyFileManager.FolderType.Data, Name, SavePriority.High);

		PinnedExtensionsList = new(() => [], "PinnedExtensions.json", FoxyFileManager.FolderType.Data, Name);
		
		_pins = new("Pins.json", FoxyFileManager.FolderType.Data, Name);
		_bookmarks = new("Bookmarks.json", FoxyFileManager.FolderType.Data, Name);

		History = new HistoryManager(Name);
		SitePermissions = new SitePermissionManager(Name);
		Downloads = new DownloadManager(Name);
			
		await AppServer.AutoSaver.AddItems([
			_settings,
			_cache,
			_pins,
			_bookmarks,
			PinnedExtensionsList,
			History.SaverItem,
			SitePermissions.SaverItem,
			Downloads.SaverItem,
		]);

		IsPrivate = Settings.PrivateBrowsing;
		History.Initialize(Settings.HistoryRetentionDays);
		Downloads.Initialize();

		pinnedExtensions = new Lazy<ObservableCollection<Extension>>(GetAndVerifyPinnedExtensions);
		
		_settings.Item.PropertyChanged += HandleSettingsChange;
		if (DefaultThemeObject.Themes.TryGetValue(Settings.ThemeName, out var theme)) CurrentTheme = theme;
	}
	
	// Claude Doc
	/// <param name="startLocation">Window bounds in physical screen pixels, see <see cref="MainWindow.ApplyBounds"/>.</param>
	public async Task<MainWindow> CreateWindow(string[]? urls = null, Rect? startLocation = null, MainWindow.BrowserWindowState windowState = MainWindow.BrowserWindowState.Normal)
	{
		try
		{
			var newWindow = await MainWindow.Create(this);

			Focused?.Invoke(this);

			if (startLocation is { } sl)
				newWindow.ApplyBounds(sl);
			
			Windows.AddFirst(newWindow);
			newWindow.Activated += (s, e) =>
			{
				if (e.WindowActivationState != WindowActivationState.Deactivated && s is MainWindow win)
				{
					Windows.Remove(win);
					Windows.AddFirst(win);
					Focused?.Invoke(this);
				}
			};
			newWindow.Closed += (w, _) =>
			{
				//C saved before removal so the last window is in it; StartupMode decides on next start whether it comes back
				if (AppServer.Instances.All(i => i.Windows.All(x => x == w)))
					BackupManagement.SaveOnLastWindowClosing();

				if (w is MainWindow baw)
					Windows.Remove(baw);
				else
					throw new InvalidOperationException(
						$"Cannot remove browser application window from instance '{Name}', sender type mismatch.");
			};

			newWindow.SearchEngineChangeRequested += (se) => { Cache.CurrentSearchEngine = se; };

			newWindow.CurrentTheme = CurrentTheme;

			urls?.ToList().ForEach(url => newWindow.TabManager.SwapActiveTabTo(newWindow.TabManager.AddTab(url)));

			newWindow.Activate();
			
			newWindow.ApplyWindowState(windowState);

			return newWindow;
		}
		catch (Exception ex) //TODO: does nothing I think
		{
			FoxyLogger.AddError(ex); 
			
			return null;
		}
	}
	
}
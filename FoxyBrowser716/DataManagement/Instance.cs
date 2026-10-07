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
	
	public bool IsPrimaryInstance => Name == AppServer.PrimaryInstance.Name;

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
			Downloads.SaverItem!,
		]);

		History.Initialize(Settings.HistoryRetentionDays);
		Downloads.Initialize();

		pinnedExtensions = new Lazy<ObservableCollection<Extension>>(GetAndVerifyPinnedExtensions);
		
		_settings.Item.PropertyChanged += HandleSettingsChange;
		if (DefaultThemeObject.Themes.TryGetValue(Settings.ThemeName, out var theme)) CurrentTheme = theme;
	}
	
	public async Task<MainWindow> CreateWindow(string[]? urls = null, Rect? startLocation = null, MainWindow.BrowserWindowState windowState = MainWindow.BrowserWindowState.Normal, bool isPrivate = false)
	{
		try
		{
			var newWindow = await MainWindow.Create(this, isPrivate);

			Focused?.Invoke(this);

			if (startLocation is { } sl)
			{
				newWindow.MoveAndResize(sl.X, sl.Y, sl.Width, sl.Height);
				;
			}
			
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
				if (w is MainWindow baw)
					Windows.Remove(baw);
				else
					throw new InvalidOperationException(
						$"Cannot remove browser application window from instance '{Name}', sender type mismatch.");

				// last window closed
				if (AppServer.Instances.All(i => i.Windows.Count == 0))
					BackupManagement.ClearBackup(); 
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
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading;
using Windows.Foundation;
using FoxyBrowser716.DataManagement;
using FoxyBrowser716.DataObjects.Basic;
using FoxyBrowser716.ErrorHandeler;
using Microsoft.Web.WebView2.Core;

namespace FoxyBrowser716.DataObjects.Complex;

public partial class WebviewTab : ObservableObject
{
	private static int _tabCounter;
	
	[ObservableProperty] public partial bool IsActive { get; set; }
	[ObservableProperty] public partial WebsiteInfo Info { get; set; }
	public TabManager TabManager;
	private string? _startingUrl;
	public int Id { get; private set; }
	[JsonIgnore] [ObservableProperty] public partial bool MovingTab { get; set; }
	
	[ObservableProperty] public partial WebView2 Core { get; private set; }
	
	public Task InitializeTask { get; private set; }
	
	private static readonly HashSet<int> _boostedProcessIds = [];
	private static readonly object _boostLock = new();
	
	public WebviewTab(TabManager tabManager, string? url)
	{
		var core = new WebView2();
		var info = new WebsiteInfo
		{
			Url = url ?? "",
			Title = "Loading...",
			FavIconUrl = "",
		};
		
		TabManager = tabManager;
		_startingUrl = url;
				
		Id = Interlocked.Increment(ref _tabCounter);
		Info = info;
		Core = core;
		
		InitializeTask = CoreWebView2Initialization();
		
		GetMenuItems = () =>
		{
			var items = new ObservableCollection<FMenuItem>(BaseItems);

			if (!TabManager.Tabs.Contains(this))
			{
				items.Add(new() { Text = "Remove from group", Action = () => TabManager.MoveTabToGroup(Id, -1) });
			}

			foreach (var group in TabManager.Groups.Where(g => !g.Tabs.Contains(this)))
			{
				items.Add(new() { Text = $"Move to {group.Name}", Action = () => TabManager.MoveTabToGroup(Id, group.Id) });
			}

			return items;
		};
	}

	private async Task CoreWebView2Initialization()
	{
		if (TabManager.Instance.IsPrivate)
		{
			// InPrivate: an off-the-record profile shared by the instance's tabs, discarded when they all close
			var options = TabManager.WebsiteEnvironment!.CreateCoreWebView2ControllerOptions();
			options.IsInPrivateModeEnabled = true;
			await Core.EnsureCoreWebView2Async(TabManager.WebsiteEnvironment, options);
		}
		else
			await Core.EnsureCoreWebView2Async(TabManager.WebsiteEnvironment);
		
		// await TabManager.Instance.AddExtensions(Core);
		//  TabManager.Instance.RegisterStoreButtonCallback(json => {
		// 	// json contains: host, url, originalText, type
		// 	// e.g. read the url:
		// 	if (json.TryGetProperty("url", out var urlProp)) {
		// 		var url = urlProp.GetString();
		// 		// do something: log, open UI, start extension install, etc.
		// 		System.Diagnostics.Debug.WriteLine("Clicked store button on: " + url);
		// 	}
		// });
		
		//await TabManager.Instance.InjectStoreButtonInterceptor(Core);

		var extensionSetupTask = TabManager.Instance.SetupExtensionSupport(Core);
		
		// Core.AllowExternalDrop = true;
		Core.AllowDrop = true;
		// Core.CompositeMode = ElementCompositeMode.MinBlend;
		
		// only seen when the themed downloads panel is turned off in settings
		Core.CoreWebView2.DefaultDownloadDialogCornerAlignment = CoreWebView2DefaultDownloadDialogCornerAlignment.TopLeft;
		Core.CoreWebView2.DefaultDownloadDialogMargin = new Point(0, 0);

		_defaultUserAgent = Core.CoreWebView2.Settings.UserAgent;
		ApplySettings();

		// handle events
		Core.CoreWebView2.DocumentTitleChanged += OnDocumentTitleChanged;
		Core.CoreWebView2.FaviconChanged += OnFaviconChanged;
		Core.CoreWebView2.SourceChanged += CoreWebView2OnSourceChanged;
		Core.CoreWebView2.NavigationCompleted += CoreWebView2OnNavigationCompleted;
		Core.CoreWebView2.NewWindowRequested += CoreWebView2OnNewWindowRequested;
		Core.CoreWebView2.NavigationStarting += CoreWebView2OnNavigationStarting;
		Core.CoreWebView2.WindowCloseRequested += CoreWebView2OnWindowCloseRequested;
		Core.CoreWebView2.ProcessFailed += CoreWebView2OnProcessFailed;

		// themed replacements for the browser engine's own UI, shown by the window that hosts this tab
		Core.CoreWebView2.PermissionRequested += (_, args) => TabManager.Window.OnTabPermissionRequested(this, args);
		Core.CoreWebView2.ContextMenuRequested += (_, args) => TabManager.Window.OnTabContextMenuRequested(this, args);
		Core.CoreWebView2.ScriptDialogOpening += (_, args) => TabManager.Window.OnTabScriptDialogOpening(this, args);
		Core.CoreWebView2.BasicAuthenticationRequested += (_, args) => TabManager.Window.OnTabBasicAuthenticationRequested(this, args);
		Core.CoreWebView2.DownloadStarting += (_, args) => TabManager.Window.OnTabDownloadStarting(this, args);
		
		//performance stuff
		var processId = Core.CoreWebView2.BrowserProcessId;
		
		_ = Task.Run(() =>
		{
			try
			{
				BoostProcessPriority((int)processId);
			}
			catch { /* Ignore */ }
		});

		await Task.WhenAll(extensionSetupTask, NavigateOrSearch(_startingUrl, true));
	}

	private string? _defaultUserAgent;

	/// <summary>
	/// Pushes <see cref="DataObjects.Settings.BrowserSettings"/> into this tab's WebView2. Called once the core
	/// exists and again by the window whenever a setting changes, so changes apply without reopening tabs.
	/// </summary>
	public void ApplySettings()
	{
		if (Core.CoreWebView2 is not { } core) return;
		var s = TabManager.Instance.Settings;

		try
		{
			var settings = core.Settings;
			settings.AreDevToolsEnabled = s.DevToolsEnabled;
			settings.AreBrowserAcceleratorKeysEnabled = s.BrowserAcceleratorKeysEnabled;
			settings.IsStatusBarEnabled = s.StatusBarEnabled;
			settings.IsZoomControlEnabled = s.ZoomControlEnabled;
			settings.IsPinchZoomEnabled = s.PinchZoomEnabled;
			settings.IsSwipeNavigationEnabled = s.SwipeNavigationEnabled;
			settings.IsGeneralAutofillEnabled = s.GeneralAutofillEnabled;
			settings.IsPasswordAutosaveEnabled = s.PasswordAutosaveEnabled;
			settings.IsReputationCheckingRequired = s.SmartScreenEnabled;
			// with this off, WebView2 raises ScriptDialogOpening and the window shows the themed dialog
			settings.AreDefaultScriptDialogsEnabled = !s.ThemedDialogs;

			var userAgent = string.IsNullOrWhiteSpace(s.CustomUserAgent) ? _defaultUserAgent : s.CustomUserAgent.Trim();
			if (userAgent is not null && settings.UserAgent != userAgent)
				settings.UserAgent = userAgent;

			var profile = core.Profile;
			profile.PreferredColorScheme = s.WebsiteColorScheme;
			profile.PreferredTrackingPreventionLevel = s.TrackingPrevention;
			profile.IsPasswordAutosaveEnabled = s.PasswordAutosaveEnabled;
			profile.IsGeneralAutofillEnabled = s.GeneralAutofillEnabled;

			var downloadFolder = string.IsNullOrWhiteSpace(s.DownloadFolder) || !Directory.Exists(s.DownloadFolder)
				? DownloadManager.GetSystemDownloadsFolder()
				: s.DownloadFolder;
			if (downloadFolder is not null && profile.DefaultDownloadFolderPath != downloadFolder)
				profile.DefaultDownloadFolderPath = downloadFolder;
		}
		catch (Exception e)
		{
			// the core can be closing while settings change
			FoxyLogger.AddError(e);
		}
	}
	
	private static void BoostProcessPriority(int processId)
	{
		lock (_boostLock)
		{
			if (_boostedProcessIds.Contains(processId))
				return;

			try
			{
				var process = Process.GetProcessById(processId);
				process.PriorityClass = ProcessPriorityClass.AboveNormal;
				_boostedProcessIds.Add(processId);
			}
			catch { /* Process may have exited */ }
		}
	}

	private void CoreWebView2OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
	{
		//TODO: temp fix
		if (args.Uri.StartsWith("chrome-search://local-ntp/local-ntp.html"))
		{
			args.Cancel = true;
		}
	}

	private void CoreWebView2OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args) 
	{
		FoxyLogger.AddWarning($"{nameof(CoreWebView2OnProcessFailed)} - {args.Reason} ({args.ExitCode})", 
			$"{nameof(args.FailureSourceModulePath)}: {args.FailureSourceModulePath}\n{nameof(args.ProcessFailedKind)}: {args.ProcessFailedKind}\n{nameof(args.ProcessDescription)}: {args.ProcessDescription}");
		
		// Only recover if the render process or the main browser process failed.
		// Ignoring utility process and GPU process failures prevents restart loops.
		if (args.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited 
		    or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive 
		    or CoreWebView2ProcessFailedKind.BrowserProcessExited 
		    or CoreWebView2ProcessFailedKind.FrameRenderProcessExited)
		{
			TabManager.RemoveTab(Id);
			TabManager.SwapActiveTabTo(TabManager.AddTab(Core.Source.ToString()));
		}
	}

	private void CoreWebView2OnWindowCloseRequested(CoreWebView2 sender, object args)
	{
		TabManager.RemoveTab(Id);
	}

	private void CoreWebView2OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
	{
		TabManager.SwapActiveTabTo(TabManager.AddTab(args.Uri));
		args.Handled = true;
	}

	private void CoreWebView2OnSourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
	{
		Info.Url = Core.CoreWebView2.Source;

		// same-document navigations (single-page apps) never raise NavigationCompleted
		if (!args.IsNewDocument)
			RecordHistory();
	}

	private void CoreWebView2OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
	{
		if (args.IsSuccess)
			RecordHistory();
	}

	private bool ShouldRecordHistory => TabManager.Instance.Settings.SaveHistory;

	private void RecordHistory()
	{
		if (!ShouldRecordHistory) return;
		var core = Core.CoreWebView2;
		TabManager.Instance.History.RecordVisit(core.Source, core.DocumentTitle, core.FaviconUri);
	}

	public async Task NavigateOrSearch(string? url) => await NavigateOrSearch(url, false);
	
	private async Task NavigateOrSearch(string? url, bool forceNav)
	{
		if (url is null) return;
		
		if (!InitializeTask.IsCompleted && !forceNav)
		{
			_startingUrl = url;
			return;
		}

		try
		{
			Core.CoreWebView2.Navigate(url);
		}
		catch
		{
			if (url.Contains('.') || url.Contains(':') || url.Contains('/')) //TODO: got to fix this
			{
				if (!await TryNavigateAsync("https://" + url))
					if (!await TryNavigateAsync("http://" + url))
						Core.CoreWebView2.Navigate(
							InfoGetter.GetSearchUrl(TabManager.Instance.Cache.CurrentSearchEngine, url));
			}
			else
				Core.CoreWebView2.Navigate(InfoGetter.GetSearchUrl(TabManager.Instance.Cache.CurrentSearchEngine, url));
		}
	}
	
	private async Task<bool> TryNavigateAsync(string url)
	{
		var tcs = new TaskCompletionSource<bool>();

		void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
		{
			Core.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
			tcs.TrySetResult(args.IsSuccess || args.WebErrorStatus == CoreWebView2WebErrorStatus.Unknown);
		}

		try
		{
			Core.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
			Core.CoreWebView2.Navigate(url);

			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			cts.Token.Register(() => tcs.TrySetResult(false));

			return await tcs.Task;
		}
		catch
		{
			Core.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
			return false;
		}
	}

	private void OnDocumentTitleChanged(object sender, object e)
	{
		Info.Title = Core.CoreWebView2.DocumentTitle;
		if (ShouldRecordHistory)
			TabManager.Instance.History.UpdateDetails(Core.CoreWebView2.Source, Info.Title, null);
	}

	private void OnFaviconChanged(object sender, object e)
	{
		Info.FavIconUrl = Core.CoreWebView2.FaviconUri ?? "";
		if (ShouldRecordHistory)
			TabManager.Instance.History.UpdateDetails(Core.CoreWebView2.Source, null, Info.FavIconUrl);
	}
	
	private ObservableCollection<FMenuItem> BaseItems =>
	[
		new() { Text = "Close", Action = () => TabManager.RemoveTab(Id) },
		new() { Text = "Duplicate", Action = () =>
		{
			var groupId = TabManager.Groups.FirstOrDefault(g => g.Tabs.Contains(this))?.Id;
			var id = TabManager.AddTab(Core.Source.ToString());
			TabManager.SwapActiveTabTo(id);
			if (groupId is { } gid)
				TabManager.MoveTabToGroup(id, gid);
			
		} },
		new() { Text = "Move to new group", Action = () => TabManager.MoveTabToGroup(Id, TabManager.CreateGroup().Id) },
	];

	public readonly Func<ObservableCollection<FMenuItem>> GetMenuItems;
}
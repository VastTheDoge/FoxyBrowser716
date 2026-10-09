using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using FoxyBrowser716.Controls.MainWindow;
using FoxyBrowser716.DataObjects.Complex;
using FoxyBrowser716.ErrorHandeler;
using Microsoft.Web.WebView2.Core;
using WinUIEx;

namespace FoxyBrowser716.DataManagement;

public partial class TabManager : ObservableObject
{
	[ObservableProperty] public partial Instance Instance { get; private set; }

	/// <summary>The window these tabs live in; it hosts their themed web UI (menus, prompts, downloads).</summary>
	public MainWindow Window { get; private set; } = null!;

	/// <summary>Tabs use an InPrivate WebView2 session: nothing is written to history, downloads or permissions.</summary>
	public bool IsPrivate { get; private set; }

	[ObservableProperty] public partial ObservableCollection<WebviewTab> Tabs { get; set; } = [];
	[ObservableProperty] public partial ObservableCollection<TabGroup> Groups { get; set; } = [];
	
	private ConcurrentDictionary<int, WebviewTab> _tabs { get; set; } = [];
	
	public event Action<WebviewTab> TabAdded;
	public event Action<WebviewTab> TabRemoved;
	public event Action<(int oldId, int newId)> ActiveTabChanged; 
	
	public int ActiveTabId { get; private set; } = -1;
	
	public CoreWebView2Environment? WebsiteEnvironment { get; private set; }

	private TabManager()
	{ }

	public static async Task<TabManager> Create(Instance instance, MainWindow window, bool isPrivate)
	{
		var newManager = new TabManager();
		await newManager.Initialize(instance, window, isPrivate);
		return newManager;
	}

	private async Task Initialize(Instance instance, MainWindow window, bool isPrivate)
	{
		Instance = instance;
		Window = window;
		IsPrivate = isPrivate;
		
		var options = new CoreWebView2EnvironmentOptions
		{
			AreBrowserExtensionsEnabled = true,
			AllowSingleSignOnUsingOSPrimaryAccount = true,
			//EnableTrackingPrevention = true, login issues sometimes, this might be a reason for that?
			//TODO: look into these flags and make sure each are secure
			AdditionalBrowserArguments = $"--disable-features=AudioServiceOutOfProcess " //TODO: 100% something here causing the lag
				+ "--site-per-process "
				+ "--enable-gpu-rasterization "
				+ "--enable-features=UseSkiaRenderer,CanvasOopRasterization "
			
			/*"--enable-gpu " +
				"--enable-gpu-rasterization " +
				"--enable-hardware-overlays " +
				"--enable-webgl2-compute-context " +
				"--enable-accelerated-2d-canvas " +
				// "--enable-gpu-memory-buffer " +
				// "--enable-native-gpu-memory-buffers " +
				// "--site-per-process " +
				"--disable-background-timer-throttling " +
				// "--disable-backgrounding-occluded-windows " +
				"--disable-renderer-backgrounding " +
				"--enable-features=UseSkiaRenderer,CanvasOopRasterization " +
				"--disable-frame-rate-limit " +
				// "--enable-features=VaapiVideoDecoder,VaapiVideoEncoder " +
				"--enable-quic " +
				// "--enable-experimental-web-platform-features " +
				"--memory-pressure-on " +
				"--max-gum-fps=144 " + 
				// "--max-unused-resource-memory-usage-percentage=5 " +
				"--disable-background-media-suspend " //+
				// "--disable-low-res-tiling " +
				// "--enable-lcd-text "*/
		};
			
		WebsiteEnvironment ??= await CoreWebView2Environment.CreateWithOptionsAsync(null, FoxyFileManager.BuildFolderPath(FoxyFileManager.FolderType.WebView2, Instance.Name), options);
	}
	
	/// <summary>
	/// Whether tabs dragged from <paramref name="source"/> may drop into <paramref name="target"/>: same instance, and
	/// never between a private and a normal window (the tab would be re-opened in the other profile).
	/// </summary>
	public static bool CanAcceptTabsFrom(TabManager? target, TabManager source) =>
		target is not null && target.Instance.Name == source.Instance.Name && target.IsPrivate == source.IsPrivate;

	public bool TryGetTab(int tabId, out WebviewTab? tab)
	{
		if (_tabs.GetValueOrDefault(tabId) is { } t)
		{
			tab = t;
			return true;
		}
		else
		{
			tab = null;
			return false;
		}
	}
	
	public Dictionary<int,WebviewTab> GetAllTabs() => _tabs.ToDictionary();
	
	public void RemoveTab(int tabId) => RemoveTab(tabId, false);
	
	public void RemoveTab(int tabId, bool keepCore) {
		if (_tabs.Count > 1)
		{
			var newId = ActiveTabId == tabId ? _tabs.First(t => t.Key != tabId).Key : ActiveTabId;
			SwapActiveTabTo(newId);
		}
		else
		{
			SwapActiveTabTo(-1);
		}
		
		if (_tabs.TryRemove(tabId, out var tab))
		{
			var removeFrom = Groups
				.FirstOrDefault(g => g.Tabs.Contains(tab));
		
			if (removeFrom is not null)
			{
				removeFrom.Tabs.Remove(tab);
				if (removeFrom.Tabs.Count == 0)
					Groups.Remove(removeFrom);
			}
			else 
				Tabs.Remove(tab);
			
			if (tab.Core.Parent is Grid grid)
			{
				grid.Children.Remove(tab.Core);
				if (!keepCore)
				{
					tab.Core.Close();
				}
			}			
			else
				throw new Exception($"Tab (Id = {tabId}) could not be removed. Unknown Core Parent.");
			TabRemoved?.Invoke(tab);
		}
	}
	
	public int AddTab(string? url)
	{
		var tab = new WebviewTab(this, url);
		if (_tabs.TryAdd(tab.Id, tab))
			Tabs.Add(tab);
		TabAdded?.Invoke(tab);
		return tab.Id;
	}

	private int? _nextToSwap;
	private bool _swaping;
	
	public void SwapActiveTabTo(int tabId) => _ = SwapActiveTabTo(tabId, true);
	
	public async Task SwapActiveTabTo(int tabId, bool waitForTabToInitialize)
	{
		FoxyLogger.AddInfo($"{ActiveTabId} => {tabId}");
		if (ActiveTabId == tabId)
			return;

		if (waitForTabToInitialize && ActiveTabId >= 0 && _tabs.TryGetValue(ActiveTabId, out var activeTab))
		{
			await activeTab.InitializeTask;
		}
		
		if (_swaping)
		{
			_nextToSwap = tabId;
			return;
		}
		_swaping = true;
		
		var oldId = ActiveTabId;

		if (tabId >= 0 && _tabs.TryGetValue(tabId, out var tab))
		{
			await tab.InitializeTask;

			if (tab.Core.CoreWebView2.IsSuspended)
				tab.Core.CoreWebView2.Resume();
			tab.Core.Focus(FocusState.Programmatic);
			tab.IsActive = true;
			
		} else if (tabId >= 0)
		{
			// should not happen, return.
			_swaping = false;
			return;
		}
		
		if (oldId >= 0 && _tabs.TryGetValue(oldId, out var oldTab))
		{
			oldTab.IsActive = false;
		}
		
		ActiveTabId = tabId;
		
		if (false) //TODO
		{
			List<Task> initTasks = [];

			async Task Suspend(KeyValuePair<int, WebviewTab> idTabPair)
			{
				await idTabPair.Value.InitializeTask;
				await idTabPair.Value.Core.EnsureCoreWebView2Async(WebsiteEnvironment);
				if (idTabPair.Value.Core.CoreWebView2.IsDocumentPlayingAudio)
					return;
				await idTabPair.Value.Core.CoreWebView2.TrySuspendAsync();
			}

			// call the above function with every tab that is not the active tab and add the task to a list.
			initTasks.AddRange(_tabs.Where(t => t.Value.Id != tabId).Select(Suspend));
			await Task.WhenAll(initTasks);
		}
		
		foreach (var t in _tabs)
			t.Value.Core.Visibility = t.Value.Id == ActiveTabId
				? Visibility.Visible
				: Visibility.Collapsed;
		
		ActiveTabChanged?.Invoke((oldId, ActiveTabId));

		_swaping = false;
		if (_nextToSwap is { } next)
		{
			_nextToSwap = null;
			SwapActiveTabTo(next);
		}
	}

	/// <summary>
	/// -1 group id for no group
	/// </summary>
	public void MoveTabToGroup(int tabId, int groupId, int? targetIndex = null)
	{
		var tab = _tabs[tabId];
		
		var removeFrom = Groups
			.FirstOrDefault(g => g.Tabs.Contains(tab));
		
		if (removeFrom is { } group)
		{
			group.Tabs.Remove(tab);
			if (group.Tabs.Count == 0)
				Groups.Remove(group);
		}	
		else 
			Tabs.Remove(tab);

        if (groupId >= 0)
        {
	        var groupToAdd = Groups.FirstOrDefault(g => g.Id == groupId);
	        if (targetIndex is { } index) groupToAdd?.Tabs.Insert(index, tab);
	        else groupToAdd?.Tabs.Add(tab);
        }		else
        if (targetIndex is { } index) Tabs.Insert(index, tab);
        else Tabs.Add(tab);	}

	public TabGroup CreateGroup()
	{
		var group = new TabGroup(this);
		group.Name = $"Group {group.Id}";
		
		Groups.Add(group);
		
		return group;
	}

	public void RemoveGroup(int groupId)
	{
		var group = Groups.FirstOrDefault(g => g.Id == groupId);
		if (group is null) return;
		
		foreach (var tab in group.Tabs.ToList())
			RemoveTab(tab.Id);
		Groups.Remove(group);
	}

	public void MoveTabFromWindowToGroup(WebviewTab tab, TabManager sourceManager, int groupId, int targetIndex = -1)
	{
		MoveTabToGroup(AddTab(tab.Info.Url), groupId, targetIndex);
		sourceManager.RemoveTab(tab.Id);
	}

	public void MoveGroupFromWindow(TabGroup group, TabManager sourceManager)
	{
		sourceManager.Groups.Remove(group);
		
		var nGroup = new TabGroup(this)
		{
			Name = group.Name,
			GroupColor = group.GroupColor
		};
		
		Groups.Add(nGroup);

		var i = 0;
		foreach (var tab in group.Tabs)
		{
			var nTab = new WebviewTab(this, tab.Info.Url);
			nGroup.Tabs.Add(nTab);
			_tabs.TryAdd(nTab.Id, nTab);
			TabAdded?.Invoke(nTab);
			i++;
		}
	}

	public void MoveTabFromWindowToNewGroup(WebviewTab tab, TabManager sourceManager)
	{
		MoveTabFromWindowToGroup(tab, sourceManager, CreateGroup().Id);
	}

	public void MoveGroupTabsFromWindow(TabGroup group, TabManager sourceManager, int targetIndex)
	{
		var tabsToMove = group.Tabs.ToList();
		
		sourceManager.Groups.Remove(group);

		foreach (var tab in tabsToMove)
		{
			MoveTabFromWindow(tab, sourceManager, targetIndex);
			targetIndex++;
		}
	}

	public void MoveTabFromWindow(WebviewTab tab, TabManager sourceManager, int targetIndex)
	{
		sourceManager.RemoveTab(tab.Id);
		
		var nTab = new WebviewTab(this, tab.Info.Url);
		
		if (_tabs.TryAdd(nTab.Id, nTab))
		{
			if (targetIndex >= 0 && targetIndex <= Tabs.Count)
				Tabs.Insert(targetIndex, nTab);
			else
				Tabs.Add(nTab);
			
			TabAdded?.Invoke(nTab);
			
			SwapActiveTabTo(nTab.Id);
		}
	}
	
	public async Task CreateWindowWithGroup(TabGroup group, Rect bounds)
	{
		var window = await Instance.CreateWindow([], bounds, isPrivate: IsPrivate);
		window.TabManager.MoveGroupFromWindow(group, this);
		window.Show();
	}

	public async Task CreateWindowWithTab(WebviewTab tab, Rect bounds)
	{
		var window = await Instance.CreateWindow([], bounds, isPrivate: IsPrivate);
		window.TabManager.MoveTabFromWindow(tab, this, 0);
		window.Show();
	}

	public void DissolveGroup(TabGroup group, int? targetIndex = null)
	{
		var index = targetIndex ?? Tabs.Count;
		
		foreach (var tab in group.Tabs.ToList())
		{
			group.Tabs.Remove(tab);
			Tabs.Insert(index++, tab);
		}
		
		Groups.Remove(group);
	}
}
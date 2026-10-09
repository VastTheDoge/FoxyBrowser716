using System.Diagnostics;
using CommunityToolkit.WinUI;
using FoxyBrowser716.ErrorHandeler;

namespace FoxyBrowser716.DataManagement;

public static class BackupManagement
{
	private static readonly string BackupPath = FoxyFileManager.BuildFilePath("Backup.json", FoxyFileManager.FolderType.Cache);
	
	public static bool CheckForRestore()
	{
		var response = FoxyFileManager.ReadFromFile<AppBackupModel>(BackupPath);
		
		// a backup with no windows has nothing to restore; treating it as a restore would leave the app
		// running with no window at all
		return response is { code: FoxyFileManager.ReturnCode.Success, content.Windows.Length: > 0 };
	}

	public static void ClearBackup()
	{
		FoxyFileManager.DeleteFile(BackupPath);
	}

	public static async Task RestoreBackup()
	{
		try
		{
			var response = FoxyFileManager.ReadFromFile<AppBackupModel>(BackupPath);
		
			if (response is { code: FoxyFileManager.ReturnCode.Success, content: { } backup })
			{
				List<Task> windowInitTasks = [];
				foreach (var window in backup.Windows)
				{
					if (AppServer.Instances.FirstOrDefault(x => x.Name == window.InstanceName) is { } instance)
					{
						windowInitTasks.Add(Task.Run(() => AppServer.UiDispatcherQueue.TryEnqueue(async () =>
						{
							var newWindow = await instance.CreateWindow(null, window.Bounds, window.State);
							List<Task> tabInitTasks = [];
							tabInitTasks.AddRange(
								window.Tabs.Select(tab => 
									Task.Run(() => AppServer.UiDispatcherQueue.TryEnqueue(async () => 
									{
										var id = newWindow.TabManager.AddTab(tab.Value);
										if (tab.Key == window.ActiveTabId) newWindow.TabManager.SwapActiveTabTo(id); 
									})))
							);
							tabInitTasks.AddRange(
								window.TabGroups.Select(g => 
									Task.Run(() => AppServer.UiDispatcherQueue.TryEnqueue(async () => 
									{
										var group = newWindow.TabManager.CreateGroup();
										group.Name = g.Name;
										group.GroupColor = g.GroupColor;
										foreach (var tab in g.Tabs)
										{
											var id = newWindow.TabManager.AddTab(tab.Value);
											newWindow.TabManager.MoveTabToGroup(id, group.Id);
											if (tab.Key == window.ActiveTabId) 
												newWindow.TabManager.SwapActiveTabTo(id);
										}
									})))
							);
							await Task.WhenAll(tabInitTasks);
						})));
					}
				}
				await Task.WhenAll(windowInitTasks);
			}	
		}
		catch (Exception e)
		{
			FoxyLogger.AddError(e);
			Debug.WriteLine(e);
		}
	}

	public static async Task BackupData()
	{
		if (AppServer.Instances.All(i => i.Windows.Count == 0))
			return;
		
		try
		{
			AppServer.UiDispatcherQueue.TryEnqueue(async () =>
			{
				try
				{
					List<WindowBackupModel> windows = [];
					windows.AddRange(
						from instance in AppServer.Instances 
						from window in instance.Windows
						select new WindowBackupModel
						{
							InstanceName = instance.Name,
							State = window.StateFromWindow(),
							Bounds = window.GetBounds(),
							ActiveTabId = window.TabManager.ActiveTabId,
							Tabs = window.TabManager.Tabs.ToDictionary(x => x.Id, y => y.Info.Url),
							TabGroups = window.TabManager.Groups.Select(g => new TabGroupBackupModel
							{
								Name = g.Name, 
								GroupColor = g.GroupColor, 
								Tabs = g.Tabs.ToDictionary(x => x.Id, y => y.Info.Url)
							}).ToArray()
						});

					var backup = new AppBackupModel
					{
						Windows = windows.ToArray(),
					};
		
					await FoxyFileManager.SaveToFileAsync(BackupPath, backup); // saving to appdata, not cache
				}
				catch (Exception e)
				{
					FoxyLogger.AddError(e);
					Debug.WriteLine(e);
				}
			});
			
		}
		catch (Exception e)
		{
			FoxyLogger.AddError(e);
			Debug.WriteLine(e);
		}
		
	}
}
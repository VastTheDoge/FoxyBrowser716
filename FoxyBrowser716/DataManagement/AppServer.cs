using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.ApplicationModel;
using FoxyBrowser716.Controls.MainWindow;
using FoxyBrowser716.DataObjects.Basic;
using FoxyBrowser716.DataObjects.Complex;
using FoxyBrowser716.ErrorHandeler;
using Microsoft.UI.Dispatching;

namespace FoxyBrowser716.DataManagement;

public static class AppServer
{
	// private static Timer _backupTimer = new Timer();

	public static DispatcherQueue UiDispatcherQueue = null!;

	public static FoxyAutoSaver AutoSaver = null!;
	
	public static Instance CurrentInstance = null!;
	public static Instance PrimaryInstance = null!;
	
	public static VersionInfo VersionInfo => _versionInfo.Item!;
	private static readonly FoxyAutoSaverField<VersionInfo> _versionInfo =
		new(() => new VersionInfo { Version = null }, "VersionInfo.json", FoxyFileManager.FolderType.Cache);
	
	public static MainWindow? CurrentWindow => CurrentInstance?.CurrentWindow;
	
	public static readonly List<Instance> Instances = [];

	private static Timer _backupTimer;

#if DEBUG
	[DllImport("kernel32.dll")]
	private static extern bool AllocConsole();

	[DllImport("kernel32.dll")]
	private static extern bool FreeConsole();

	[DllImport("kernel32.dll")]
	private static extern IntPtr GetConsoleWindow();
	
	[DllImport("kernel32.dll")]
	private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

	[DllImport("kernel32.dll")]
	private static extern IntPtr GetStdHandle(int nStdHandle);

	[DllImport("kernel32.dll")]
	private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

	[DllImport("kernel32.dll")]
	private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

	private const int STD_INPUT_HANDLE = -10;
	private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
	private const uint ENABLE_EXTENDED_FLAGS = 0x0080;
	
	private delegate bool ConsoleCtrlDelegate(CtrlType sig);

	private enum CtrlType
	{
		CTRL_C_EVENT = 0,
		CTRL_BREAK_EVENT = 1,
		CTRL_CLOSE_EVENT = 2,
		CTRL_LOGOFF_EVENT = 5,
		CTRL_SHUTDOWN_EVENT = 6
	}

	// console close should not close the app
	private static bool ConsoleCtrlCheck(CtrlType sig)
	{
		if (sig == CtrlType.CTRL_CLOSE_EVENT)
		{
			FreeConsole();
			return true; 
		}
		return false;
	}
#endif
	
	
	public static async Task HandleLaunchEvent(string[] uris, bool setupNeeded, bool fromStartup = false)
	{
		try
		{
			if (setupNeeded)
			{
#if DEBUG
				AllocConsole();
				SetConsoleCtrlHandler(ConsoleCtrlCheck, true);

				IntPtr consoleHandle = GetStdHandle(STD_INPUT_HANDLE);
				if (GetConsoleMode(consoleHandle, out uint consoleMode))
				{
					consoleMode &= ~ENABLE_QUICK_EDIT_MODE;
					consoleMode |= ENABLE_EXTENDED_FLAGS;
					SetConsoleMode(consoleHandle, consoleMode);
				}

				Console.WriteLine("FoxyBrowser716 Debug Console");
				Console.WriteLine("----------------------------");
#endif
				
				// StartupTask comes from the MSIX manifest, so unpackaged builds have no launch-on-login
				if (AppEnvironment.IsPackaged)
				{
					var startupTask = await StartupTask.GetAsync("FoxyBrowserStartup");

					switch (startupTask.State)
					{
						case StartupTaskState.Disabled:
							var newState = await startupTask.RequestEnableAsync();
							break;
						case StartupTaskState.DisabledByUser:
							//TODO: popup to re-enable
							break;
					}
				}
			
				UiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
				
				List<Task> tasks =
				[
					FoxyAutoSaver.Create([_versionInfo]).ContinueWith(task => AutoSaver = task.Result)
				];
				
				//TODO check for recovery file
			
				//TODO check for existing files and do any first time setup
			
				var instanceFolderPath = FoxyFileManager.BuildFolderPath(FoxyFileManager.FolderType.Instance);
				var result = await FoxyFileManager.GetChildrenOfFolderAsync(instanceFolderPath, FoxyFileManager.ItemType.Folder);
				Task<Instance[]>? instanceTasks = null;
				if (result is { code: FoxyFileManager.ReturnCode.Success, items: not null })
				{
					instanceTasks = Task.WhenAll(
						result.items.Select(item => Instance.Create(item.path.Split(@"\")[^1]))
					);
					tasks.Add(instanceTasks);
				}
				else if (result.code == FoxyFileManager.ReturnCode.NotFound)
				{
					// TODO this is running on laptop:
				
					//TODO: first time setup
					//throw new NotImplementedException();
				
					Instances.Add(await Instance.Create("Default"));
				}
				else
					throw new Exception($"Failed to get instances in {instanceFolderPath}: {result.code}");
			
				await Task.WhenAll(tasks);
				
				if (instanceTasks is not null)
					Instances.AddRange(await instanceTasks);

				//TODO: need a proper way to identify the primary instance
				PrimaryInstance = Instances.FirstOrDefault(i => i.Name == "Default")
				                  ?? Instances.FirstOrDefault()
				                  ?? throw new Exception("No instances found");

				CurrentInstance = PrimaryInstance;
			
				var backupRestored = false;
				if (BackupManagement.CheckForRestore())
				{
					await BackupManagement.RestoreBackup();
					backupRestored = true;
				}
				
				_backupTimer = new Timer(async _ => await BackupManagement.BackupData(), null, 10000, 5000);
				
				if (VersionInfo?.Version is null)
				{
					//TODO: 100% new
					// show a "thanks for downloading"
					
					VersionInfo!.Version = InfoGetter.VersionString;
				}
				else if (VersionInfo.Version != InfoGetter.VersionString)
				{
					//TODO: new update, go to changelog
					//TODO verify that this is not a dev version
					VersionInfo.Version = InfoGetter.VersionString;

					uris = [..uris, $"{InfoGetter.WebsiteUrl}/changelog/{VersionInfo.Version}"];
				}

				if (uris.Length > 0)
					await CurrentInstance.CreateWindow(uris);
				else if (!fromStartup && !backupRestored)
					await CurrentInstance.CreateWindow();
			}
			else if (!fromStartup)
			{
				UiDispatcherQueue.TryEnqueue(async () =>
				{
					try
					{
						if (uris.Length > 0)
							// links opened from other apps never go into a private window
							if (CurrentInstance.Windows.FirstOrDefault(w => !w.IsPrivate) is { } window)
								foreach (var uri in uris)
									window.TabManager.SwapActiveTabTo(window.TabManager.AddTab(uri));
							else
								await CurrentInstance.CreateWindow(uris);
						else
							await CurrentInstance.CreateWindow();
					}
					catch (Exception e)
					{
						Debug.WriteLine(e);
						FoxyLogger.AddError(e);
					}
				});
			}
		}
		catch (Exception e)
		{
			Debug.WriteLine(e);
			FoxyLogger.AddError(e);
		}
	}
}
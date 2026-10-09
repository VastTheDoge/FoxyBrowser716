using System.Diagnostics;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.Graphics.Display;
using Windows.UI.ViewManagement;
using FoxyBrowser716.Controls.MainWindow;
using FoxyBrowser716.DataManagement;
using FoxyBrowser716.ErrorHandeler;
using Microsoft.Windows.AppLifecycle;
using static System.Diagnostics.Process;
using UnhandledExceptionEventArgs = Microsoft.UI.Xaml.UnhandledExceptionEventArgs;
using System.Linq;

namespace FoxyBrowser716;

public partial class App : Application
{
    private const string AppKey =
#if DEBUG
        "FoxyBrowser716-Debug";
#else
            "FoxyBrowser716-Prod";
#endif

    /// <summary>
    /// Set by an unpackaged <see cref="RequestRestartAfterClose"/> to the old process id, so the new process can
    /// wait for it to exit (and not restart again if startup fails a second time).
    /// </summary>
    private const string RestartedFromEnvVar = "FOXYBROWSER716_RESTARTED_FROM";
    private static bool _isRestart;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var s = "1234";
        var s2 = @"""
                
                """;
        s2 = s2.Replace("x","BOX");
            
            
        try
        {
            if (!AppEnvironment.IsPackaged)
                WaitForRestartedProcess();

            // performance optimizations:
            // compiles JIT code for the startup profile which is reused after the first launch
            var profileRoot = AppEnvironment.IsPackaged
                ? Windows.Storage.ApplicationData.Current.LocalFolder.Path
                : Directory.CreateDirectory(FoxyFileManager.BuildFolderPath(FoxyFileManager.FolderType.Cache)).FullName;
            ProfileOptimization.SetProfileRoot(profileRoot);
            ProfileOptimization.StartProfile("Startup.profile");

#if DEBUG
            this.DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
#endif

            AppInstance? currentInstance;
            try
            {
                // Get the current app instance
                currentInstance = AppInstance.GetCurrent();

                // Check if this is the first instance
                var mainInstance = AppInstance.FindOrRegisterForKey(AppKey);

                if (!mainInstance.IsCurrent)
                {
                    var activationArgs = currentInstance.GetActivatedEventArgs();
                    try
                    {
                        await mainInstance.RedirectActivationToAsync(activationArgs).AsTask();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"RedirectActivationToAsync failed: {ex}");
                        FoxyLogger.AddError(ex);
                    }

                    Environment.Exit(0);
                    return;
                }
            }
            catch (Exception ex) when (!AppEnvironment.IsPackaged)
            {
                // AppLifecycle may not work unpackaged (e.g. under Wine): run without single-instance redirection
                FoxyLogger.AddError(ex);
                currentInstance = null;
            }

            FoxyLogger.LoadLog();
            
            this.UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
            AppDomain.CurrentDomain.FirstChanceException += CurrentDomainOnFirstChanceException;
            TaskScheduler.UnobservedTaskException += TaskSchedulerOnUnobservedTaskException;
            
            // performance optimizations:
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
            if (AppEnvironment.IsPackaged)
                CoreApplication.EnablePrelaunch(true);
            _ = Task.Run(() =>
            {
                try
                {
                    GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
                    //ThreadPool.SetMinThreads(Environment.ProcessorCount * 4, Environment.ProcessorCount * 2);
                    //ThreadPool.SetMaxThreads(Environment.ProcessorCount * 8, Environment.ProcessorCount * 4);
                }
                catch { /* ignore if fails */ }
            });
            
            // note that webview2 has its own similar optimizations in WebviewTab.cs.

            if (currentInstance is not null)
            {
                // This is the main instance, set up activation handling
                currentInstance.Activated += OnActivated;

                var e = currentInstance.GetActivatedEventArgs();

                await HandleActivationArgs(e, true);
            }
            else
                await AppServer.HandleLaunchEvent(Environment.GetCommandLineArgs()[1..], true);
        }
        catch (Exception e)
        {
            FoxyLogger.AddCritical($"Final Catch App Error: {e.Message}", e.StackTrace);
            
            RequestRestartAfterClose();
        }
    }

    private void CurrentDomainOnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
    {
        if (e.Exception is Exception ex)
            FoxyLogger.AddCritical($"Uncaught App Error: {ex.Message}", ex.StackTrace);
        else
            FoxyLogger.AddCritical("Uncaught App Error: Unknown", "No error details available.");
    }

    private void TaskSchedulerOnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (e.Exception is Exception ex)
            FoxyLogger.AddCritical($"Uncaught App Error: {ex.Message}", ex.StackTrace);
        else
            FoxyLogger.AddCritical("Uncaught App Error: Unknown", "No error details available.");
        
        e.SetObserved();
    }

    private void CurrentDomainOnUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            FoxyLogger.AddCritical($"Uncaught App Error: {ex.Message}", ex.StackTrace);
        else
            FoxyLogger.AddCritical("Uncaught App Error: Unknown", "No error details available.");
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.Exception is Exception ex)
            FoxyLogger.AddCritical($"Uncaught App Error: {ex.Message}", ex.StackTrace);
        else
            FoxyLogger.AddCritical("Uncaught App Error: Unknown", "No error details available.");

        e.Handled = true;
    }

    private async void OnActivated(object? sender, AppActivationArguments e)
    {
        await HandleActivationArgs(e, false);
    }
        
    private async Task HandleActivationArgs(AppActivationArguments args, bool isFirst)
    {
        switch (args.Kind)
        {
            case ExtendedActivationKind.StartupTask:
                if (args.Data is IStartupTaskActivatedEventArgs startupArgs)
                {
                    await AppServer.HandleLaunchEvent([], isFirst, true);
                }
                break;
            case ExtendedActivationKind.Launch:
                if (args.Data is ILaunchActivatedEventArgs launchArgs)
                {
                    var arguments = SplitArguments(launchArgs.Arguments);
                    // unpackaged launches pass the whole command line, exe path included
                    if (arguments.Length > 0 && IsOwnExecutable(arguments[0]))
                        arguments = arguments[1..];
                    await AppServer.HandleLaunchEvent(arguments, isFirst);
                }
                break;
            case ExtendedActivationKind.Protocol:
                if (args.Data is IProtocolActivatedEventArgs protocolArgs)
                {
                    var uri = protocolArgs.Uri;
                    await AppServer.HandleLaunchEvent([uri.ToString()], isFirst);
                }
                break;
            case ExtendedActivationKind.File:
                if (args.Data is IFileActivatedEventArgs fileArgs)
                {
                    var uris = fileArgs.Files.Select(f => f.Path).ToArray();
                    await AppServer.HandleLaunchEvent(uris, isFirst);
                }
                break;
            case ExtendedActivationKind.CommandLineLaunch:
                if (args.Data is ICommandLineActivatedEventArgs commandArgs)
                {
                    await AppServer.HandleLaunchEvent(
                        SplitArguments(commandArgs.Operation.Arguments)
                            .Skip(1 /*command name, such as FoxyBrowser716.exe or FoxyBrowser716*/)
                            .ToArray(), isFirst
                        );
                }
                break;
        }
    }

    /// <summary>
    /// Splits a command line on whitespace, keeping quoted parts (paths with spaces) together.
    /// </summary>
    private static string[] SplitArguments(string? commandLine)
    {
        List<string> args = [];
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in commandLine ?? "")
        {
            if (c == '"')
                inQuotes = !inQuotes;
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                    args.Add(current.ToString());
                current.Clear();
            }
            else
                current.Append(c);
        }

        if (current.Length > 0)
            args.Add(current.ToString());

        return args.ToArray();
    }

    private static bool IsOwnExecutable(string arg) =>
        !arg.Contains("://")
        && string.Equals(Path.GetFileName(arg), Path.GetFileName(Environment.ProcessPath), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// After an unpackaged restart, waits for the old process to exit so this one becomes the main instance
    /// instead of redirecting to the dying one.
    /// </summary>
    private static void WaitForRestartedProcess()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable(RestartedFromEnvVar), out var oldPid))
            return;

        _isRestart = true;
        // keep the marker out of WebView2's child processes
        Environment.SetEnvironmentVariable(RestartedFromEnvVar, null);

        try
        {
            using var oldProcess = GetProcessById(oldPid);
            oldProcess.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch { /* already exited */ }
    }

    private void RequestRestartAfterClose()
    {
        try
        {
            var currentPid = Environment.ProcessId;

            ProcessStartInfo psi;
            if (AppEnvironment.IsPackaged)
            {
                var appUserModelId = Windows.ApplicationModel.AppInfo.Current.AppUserModelId;

                psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        $"-WindowStyle Hidden -Command \"Wait-Process -Id {currentPid}; Start-Process shell:AppsFolder\\{appUserModelId}!App\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else
            {
                // no powershell (or shell:AppsFolder) under Wine: relaunch the exe directly.
                // if startup already failed once after a restart, another restart would just loop.
                if (_isRestart || Environment.ProcessPath is not { } exePath)
                {
                    FoxyLogger.AddCritical("Startup failed after a restart, exiting instead of restarting again.");
                    Environment.Exit(1);
                    return;
                }

                psi = new ProcessStartInfo(exePath) { UseShellExecute = false };
                psi.Environment[RestartedFromEnvVar] = currentPid.ToString();
            }

            Process.Start(psi);
            Environment.Exit(1);
        }
        catch (Exception e)
        {
            FoxyLogger.AddError(e);
        }
    }
}
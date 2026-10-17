using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.ApplicationModel.WindowsAppRuntime;

namespace FoxyBrowser716;

// Claude Doc
/// <summary>
/// Entry point, replacing the XAML-generated Main. An MSIX build started by path instead of through package
/// activation (e.g. a JetBrains IDE running the default browser's registry command) has no package identity, so the
/// Windows App SDK can't load; that process hands its arguments to the package through the execution alias and exits.
/// </summary>
public static class Program
{
    //C must match the ExecutionAlias in Package.appxmanifest
    private const string ExecutionAlias = "FoxyBrowser716.exe";
    private const string RelaunchedEnvVar = "FOXYBROWSER716_RELAUNCHED";

    [STAThread]
    private static void Main(string[] args)
    {
#if !FOXY_UNPACKAGED
        if (!AppEnvironment.IsPackaged)
        {
            RelaunchThroughExecutionAlias(args);
            return;
        }

        //C the marker only guards the alias relaunch; keep it out of processes this one starts
        Environment.SetEnvironmentVariable(RelaunchedEnvVar, null);

        if (!InitializeDeployment())
            return;
#endif

        StartApp();
    }

    private static void RelaunchThroughExecutionAlias(string[] args)
    {
        //C the alias launch also came up without identity; relaunching again would loop
        if (Environment.GetEnvironmentVariable(RelaunchedEnvVar) is not null)
            return;

        var alias = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", ExecutionAlias);
        if (!File.Exists(alias))
            return;

        var startInfo = new ProcessStartInfo(alias) { UseShellExecute = false };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);
        startInfo.Environment[RelaunchedEnvVar] = "1";

        try
        {
            Process.Start(startInfo)?.Dispose();
        }
        catch { /* nothing else can open the link without identity */ }
    }

    //C stands in for the Windows App SDK deployment auto-initializer (disabled in the csproj), which runs before Main and throws without identity.
    //C NoInlining here and on StartApp keeps Windows App SDK types out of Main until identity is confirmed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InitializeDeployment()
    {
        var result = DeploymentManager.Initialize(new DeploymentInitializeOptions { OnErrorShowUI = true });
        if (result.Status == DeploymentStatus.Ok)
            return true;

        Environment.Exit(result.ExtendedError.HResult);
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartApp()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }
}

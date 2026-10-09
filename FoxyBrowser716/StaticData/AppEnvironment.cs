using System.Runtime.InteropServices;

namespace FoxyBrowser716.StaticData;

/// <summary>
/// How the app is hosted. Packaged = installed as MSIX (has package identity); unpackaged = a plain exe folder
/// (built with -p:FoxyUnpackaged=true), which is also how it runs under Wine/Proton on Linux.
/// </summary>
public static class AppEnvironment
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);

    private const int AppModelErrorNoPackage = 15700;

    /// <summary>
    /// True when running with MSIX package identity. Packaged-only APIs (ApplicationData.Current, Package.Current,
    /// StartupTask, AppInfo.Current, prelaunch) throw without it, so guard them with this.
    /// </summary>
    public static readonly bool IsPackaged = CheckIsPackaged();

    /// <summary>
    /// True when running under Wine/Proton (ntdll exports wine_get_version, which real Windows never does).
    /// </summary>
    public static readonly bool IsWine = CheckIsWine();

    private static bool CheckIsPackaged()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) != AppModelErrorNoPackage;
    }

    private static bool CheckIsWine()
    {
        var ntdll = GetModuleHandle("ntdll.dll");
        return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
    }
}

using System.Reflection;
using System.Text.Json;

// Usage: WinUiMap <nuget packages folder> <output json>
// Writes { "Microsoft.UI.Xaml.Controls.Grid": ["Loaded", ...], ... } for every public Microsoft.UI.Xaml* type
// in the restored WinUI and WebView2 packages, read as metadata only (nothing Windows-specific is executed).
var packages = args[0];
var output = args[1];

static string? Newest(IEnumerable<string> paths) => paths
    .OrderByDescending(p => p, StringComparer.Ordinal)
    .FirstOrDefault();

var winui = Newest(Directory.GetFiles(Path.Combine(packages, "microsoft.windowsappsdk.winui"), "Microsoft.WinUI.dll", SearchOption.AllDirectories)
    .Where(p => p.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}")))
    ?? throw new Exception("Microsoft.WinUI.dll not found; restore the compile-check project first.");
var webview = Newest(Directory.GetFiles(Path.Combine(packages, "microsoft.web.webview2"), "Microsoft.Web.WebView2.Core.Projection.dll", SearchOption.AllDirectories)
    .Where(p => p.Contains("net8.0")));

// everything the two assemblies might reference, so base types (and their events) resolve
var searchRoots = new[]
{
    "microsoft.netcore.app.ref", "microsoft.windows.sdk.net.ref", "microsoft.windows.cswinrt",
    "microsoft.windowsappsdk.winui", "microsoft.windowsappsdk.foundation", "microsoft.windowsappsdk.interactiveexperiences",
    "microsoft.windowsappsdk.base", "microsoft.windowsappsdk", "microsoft.web.webview2",
};
var files = searchRoots
    .Select(r => Path.Combine(packages, r))
    .Where(Directory.Exists)
    .SelectMany(d => Directory.GetFiles(d, "*.dll", SearchOption.AllDirectories))
    .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}runtimes{Path.DirectorySeparatorChar}") && !p.Contains($"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}"))
    .GroupBy(Path.GetFileName)
    .Select(g => Newest(g)!)
    .ToList();
// the core library must come from the reference pack
files.RemoveAll(f => Path.GetFileName(f) == "System.Runtime.dll");
files.Add(Newest(Directory.GetFiles(Path.Combine(packages, "microsoft.netcore.app.ref"), "System.Runtime.dll", SearchOption.AllDirectories))!);
files.Add(winui);
if (webview is not null) files.Add(webview);

using var context = new MetadataLoadContext(new PathAssemblyResolver(files.Distinct()), "System.Runtime");
var map = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
foreach (var path in new[] { winui, webview }.OfType<string>())
{
    foreach (var type in context.LoadFromAssemblyPath(path).GetExportedTypes())
    {
        if (type.FullName is not { } name || !name.StartsWith("Microsoft.UI.Xaml") || type.IsNested) continue;
        var events = new List<string>();
        try
        {
            for (var t = type; t is not null; t = t.BaseType)
                events.AddRange(t.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(e => e.Name));
        }
        catch (FileNotFoundException) { /* base type outside the scanned packages */ }
        map[name] = events.Distinct().ToList();
    }
}

File.WriteAllText(output, JsonSerializer.Serialize(map));
Console.WriteLine($"WinUiMap: {map.Count} types -> {output}");

using System.Reflection;
using System.Runtime.CompilerServices;
using FoxyBrowser716.DataManagement;
using FoxyBrowser716.DataObjects.Settings;
using Material.Icons;
using Material.Icons.WinUI3;
using Microsoft.UI.Dispatching;

namespace FoxyBrowser716.Controls.HomePage;

public abstract class WidgetBase : UserControl
{
    internal WidgetData LayoutData { get; set; } = null!;
    
    public static string GetWidgetName<T>() where T : WidgetBase => 
        typeof(T).GetCustomAttribute<WidgetInfoAttribute>()?.Name
            ?? throw new InvalidOperationException("Widget does not have a WidgetInfoAttribute");
    
    public static MaterialIconKind GetWidgetIcon<T>() where T : WidgetBase => 
        typeof(T).GetCustomAttribute<WidgetInfoAttribute>()?.Icon 
            ?? throw new InvalidOperationException("Widget does not have a WidgetInfoAttribute");
    
    [ModuleInitializer]
    internal static void InitializeWidgets()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var widgetTypes = assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(WidgetBase)) && !t.IsAbstract);
        
        foreach (var type in widgetTypes)
        {
            RegisterWidgetType(type);
        }
    }
    
    private static void RegisterWidgetType(Type type)
    {
        var attr = type.GetCustomAttribute<WidgetInfoAttribute>();
        
        if (attr != null)
        {
            HomePage.AddWidget(
                attr.Name,
                attr.Icon,
                attr.Category,
                async (manager, settings, layoutData) => 
                {
                    var widget = (WidgetBase)Activator.CreateInstance(type, nonPublic: true);
                    await widget.InitializeBase(manager, settings);
                    await widget.Initialize();
                    widget.LayoutData = layoutData;
                    return widget;

                }
            );
        }
        else
            throw new InvalidOperationException($"Widget {type.Name} must have a WidgetInfoAttribute");
    }
    
    public TabManager TabManager = null!;

    public Theme CurrentTheme { get; set { field = value; ApplyTheme(); } } = DefaultThemes.DarkMode;

    protected abstract void ApplyTheme();
    
    public List<ISetting> WidgetSettings = [];

    private protected WidgetBase() { }

    protected async Task InitializeBase(TabManager manager, Dictionary<string, object>? settingsMap = null)
    {
        TabManager = manager;

        foreach (var rawSetting in WidgetSettings)
            SetSetting(rawSetting, settingsMap);
    }
    
    protected abstract Task Initialize();
    
    /// <summary>Current value of every <see cref="Setting{T}"/> in <see cref="WidgetSettings"/>, keyed by name (what gets saved).</summary>
    public Dictionary<string, object> GetSettingsMap()
    {
        var settingsMap = new Dictionary<string, object>();

        foreach (var setting in WidgetSettings)
        {
            if (GetSettingValueType(setting.GetType()) is null) continue;
            if (setting.GetType().GetProperty("Value")?.GetValue(setting) is { } value)
                settingsMap[setting.Name] = value;
        }

        return settingsMap;
    }

    /// <summary>The <c>T</c> of the <see cref="Setting{T}"/> a concrete setting (e.g. <see cref="BoolSetting"/>) derives from.</summary>
    private static Type? GetSettingValueType(Type settingType)
    {
        for (var t = settingType; t is not null; t = t.BaseType)
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Setting<>))
                return t.GetGenericArguments()[0];
        return null;
    }

    private static void SetSetting(ISetting setting, Dictionary<string, object>? settingsMap)
    {
        if (settingsMap is null || !settingsMap.TryGetValue(setting.Name, out var rawValue)) return;
        if (GetSettingValueType(setting.GetType()) is not { } valueType) return;
        if (setting.GetType().GetProperty("Value") is not { } valueProperty) return;

        try
        {
            var value = rawValue switch
            {
                null => null,
                // values loaded from json arrive as JsonElement, never as the setting's own type
                JsonElement json => json.Deserialize(valueType, FoxyFileManager.FoxyJsonSerializerOptions),
                _ when valueType.IsInstanceOfType(rawValue) => rawValue,
                _ => Convert.ChangeType(rawValue, valueType),
            };
            if (value is null && valueType.IsValueType) return;
            valueProperty.SetValue(setting, value);
        }
        catch { /* a stale or mistyped saved value just keeps the default */ }
    }

    /// <summary>
    /// Raised when the widget wants its settings written to disk outside edit mode (e.g. a note was typed).
    /// Ignored while editing — the edit-mode Save/Exit options decide what is kept.
    /// </summary>
    internal event Action<WidgetBase>? SaveRequested;
    protected void RequestSave() => SaveRequested?.Invoke(this);

    /// <summary>Opens <paramref name="urlOrSearch"/> in a new tab and switches to it.</summary>
    protected void OpenInNewTab(string urlOrSearch) => TabManager.SwapActiveTabTo(TabManager.AddTab(urlOrSearch));

    /// <summary>
    /// UI-thread timer that ticks once immediately and then every <paramref name="interval"/>, but only while the
    /// widget is in the visual tree, so removed or reloaded widgets stop ticking.
    /// </summary>
    protected DispatcherQueueTimer CreateLiveTimer(TimeSpan interval, Action tick)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = interval;
        timer.Tick += (_, _) => tick();
        Loaded += (_, _) => { tick(); timer.Start(); };
        Unloaded += (_, _) => timer.Stop();
        return timer;
    }

    /// <summary>Standard widget card look: translucent background, subtle border.</summary>
    protected void ApplyCardTheme(Border card)
    {
        card.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
        card.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
    }

    /// <summary>
    /// <paramref name="size"/>-square favicon that falls back to a globe icon when the url is empty or fails to load.
    /// </summary>
    protected static FrameworkElement CreateFavicon(string? iconUrl, double size, Brush fallbackBrush)
    {
        var fallback = new MaterialIcon { Kind = MaterialIconKind.Web, Foreground = fallbackBrush, Width = size, Height = size };
        if (string.IsNullOrWhiteSpace(iconUrl) || !Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "ms-appx"))
            return fallback;

        var image = new Image
        {
            Width = size, Height = size, Stretch = Stretch.Uniform,
            Source = uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? new SvgImageSource(uri) : new BitmapImage(uri),
        };
        fallback.Visibility = Visibility.Collapsed;
        image.ImageFailed += (_, _) =>
        {
            image.Visibility = Visibility.Collapsed;
            fallback.Visibility = Visibility.Visible;
        };
        return new Grid { Width = size, Height = size, Children = { fallback, image } };
    }

    /// <summary>Adds https:// when the user typed a bare domain; returns null for anything unusable.</summary>
    protected static Uri? NormalizeUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return uri;
        return Uri.TryCreate("https://" + text, UriKind.Absolute, out uri) ? uri : null;
    }
}

[AttributeUsage(AttributeTargets.Class)]
public class WidgetInfoAttribute : Attribute
{
    public string Name { get; }
    public MaterialIconKind Icon { get; }
    public WidgetCategory Category { get; }

    public WidgetInfoAttribute(string name, MaterialIconKind icon, WidgetCategory category)
    {
        Name = name;
        Icon = icon;
        Category = category;
    }
}

public enum WidgetCategory
{
    General,
    TimeDate,
    WebsiteNavigation,
    FoxyBrowser716,
    Tools,
    Misc,
}

public static class WidgetCategoryExtensions
{
    public static MaterialIconKind GetIcon(this WidgetCategory category)
    {
        return category switch
        {
            WidgetCategory.General => MaterialIconKind.Toolbox,
            WidgetCategory.TimeDate => MaterialIconKind.Clock,
            WidgetCategory.WebsiteNavigation => MaterialIconKind.Web,
            WidgetCategory.FoxyBrowser716 => MaterialIconKind.ApplicationCog,
            WidgetCategory.Tools => MaterialIconKind.Toolbox,
            WidgetCategory.Misc => MaterialIconKind.Inbox,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };
    }
    
    public static string GetName(this WidgetCategory category)
    {
        return category switch
        {
            WidgetCategory.General => "General",
            WidgetCategory.TimeDate => "Time & Date",
            WidgetCategory.WebsiteNavigation => "Website Navigation",
            WidgetCategory.FoxyBrowser716 => "FoxyBrowser716",
            WidgetCategory.Tools => "Tools",
            WidgetCategory.Misc => "Miscellaneous",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };
    }
}
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using FoxyBrowser716.Controls.MainWindow;
using FoxyBrowser716.Controls.SettingsPage.SettingsCustomControls;
using FoxyBrowser716.DataManagement;
using FoxyBrowser716.DataObjects.Complex.Ai;
using Microsoft.Web.WebView2.Core;

namespace FoxyBrowser716.DataObjects.Settings;

//TODO: separate out so that this file is only focused on values.
// Keep enums in a file.
// Keep Attribute in it's own file
// Keep base class in a file (for get settings function).

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class SettingInfoAttribute : Attribute
{
    public string? Name { get; init; } // null = use field name
    public string? Description { get; init; } // null = "" as description
    public required SettingsCategory Category { get; init; } // not null

    // extra for specific controls
    public /*Func<MainWindow, string[]>?*/ string? Options { get; init; }
    public bool PickerEnabled { get; init; } = false;
    public FoxyFileManager.ItemType ItemType { get; init; }
    public bool AllowWebsiteUris { get; init; } = false;
    public int MinValue { get; init; } = int.MinValue;
    public int MaxValue { get; init; } = int.MaxValue;
    // Claude Doc
    /// <summary>String shown as a password box. The property must be [JsonIgnore] and store the value itself (e.g. in the Credential Locker).</summary>
    public bool Secret { get; init; } = false;
}

// Claude Doc
/// <summary>Order here is the order categories appear on the settings page.</summary>
public enum SettingsCategory
{
    General,
    Privacy,
    Permissions,
    Downloads,
    History,
    Extensions,
    Assistant,
    WebView2,
    Misc
}

public static class SettingsCategoryNames
{
    public static string GetDisplayName(this SettingsCategory category) => category switch
    {
        SettingsCategory.Privacy => "Privacy & Security",
        SettingsCategory.Permissions => "Site Permissions",
        SettingsCategory.WebView2 => "Browser Engine",
        SettingsCategory.Assistant => "AI Assistant",
        _ => category.ToString(),
    };
}

public sealed partial class BrowserSettings : ObservableObject
{
    public Dictionary<SettingsCategory, List<ISetting>> GetSettingControls(MainWindow mainWindow)
    {
        var controls = new Dictionary<SettingsCategory, List<ISetting>>();
        var type = typeof(BrowserSettings);
        //var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var field in properties)
        {
            var attributes = (SettingInfoAttribute[])field.GetCustomAttributes(typeof(SettingInfoAttribute), false);

            if (attributes.FirstOrDefault() is not { } attribute) continue;

            if (!controls.TryGetValue(attribute.Category, out var catControls))
            {
                catControls = [];
                controls.Add(attribute.Category, catControls);
            }

            var name = attribute.Name ?? field.Name;
            var description = attribute.Description ?? "";

                switch (field.GetValue(this))
                {
                    case bool b:
                        catControls.Add(new BoolSetting(name, description, b,
                            b1 => field.SetValue(this, b1)));
                        break;
                    case int i:
                        catControls.Add(new IntSetting(name, description, i,
                            i1 => field.SetValue(this, i1),
                            attribute.MinValue == int.MinValue ? null : attribute.MinValue,
                            attribute.MaxValue == int.MaxValue ? null : attribute.MaxValue));
                        break;
                    case decimal d:
                        catControls.Add(new DecimalSetting(name, description, d,
                            d1 => field.SetValue(this, d1)));
                        break;
                    case double d:
                        throw new NotImplementedException();
                        //TODO: need more params in the attribute for max,min and step
                        //controls.Add(new SliderSetting(attribute.Name ?? field.Name, attribute.Description ?? "", d, d1 => field.SetValue(this, d1)));
                        break;
                    case Enum e:
                        var enumType = field.PropertyType;
                        var enumOptions = Enum.GetValues(enumType).Cast<object>()
                            .Select(v => (EnumOptionName(enumType, v), Convert.ToInt32(v)))
                            .ToArray();
                        catControls.Add(new ComboSetting(name, description, Convert.ToInt32(e),
                            id => field.SetValue(this, Enum.ToObject(enumType, id)), enumOptions));
                        break;
                    case string s:
                        if (attribute.Secret)
                            catControls.Add(new SecretSetting(name, description, s, s1 => field.SetValue(this, s1)));
                        else if (attribute.Options is { } comboOptions)
                        {
                            //TODO: clean up and test.
                            var i = 0;
                            var comboFunc = type.GetMethod(comboOptions, BindingFlags.NonPublic | BindingFlags.Static);
                            var options = (comboFunc.Invoke(this, [mainWindow]) as string[]).Select(o => (o, i++))
                                .ToArray();
                            catControls.Add(new ComboSetting(name, description,
                                options.FirstOrDefault(o => o.o == s).Item2, s => field.SetValue(this, options[s].o), options));
                        }
                        else if (attribute.PickerEnabled)
                        {
                            if (attribute.ItemType == FoxyFileManager.ItemType.Folder)
                                catControls.Add(new FolderPickerSetting(name, description, s, s1 => field.SetValue(this, s1)));
                            else
                                catControls.Add(new FilePickerSetting(name, description, s, s1 => field.SetValue(this, s1), attribute.AllowWebsiteUris));
                        }
                        else
                            catControls.Add(new StringSetting(name, description,
                                s, s1 => field.SetValue(this, s1)));

                        break;
                    case Color c:
                        catControls.Add(new ColorSetting(name, description, c,
                            c1 => field.SetValue(this, c1)));
                        break;
                    case Uri u:
                        throw new NotImplementedException();
                        break;
                    default:
                        //throw new ArgumentOutOfRangeException($"Type '{field.PropertyType.Name}' from field '{field.Name}' is not supported.'");
                        break;
                }
        }

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (var field in fields)
        {
            var attributes = (SettingInfoAttribute[])field.GetCustomAttributes(typeof(SettingInfoAttribute), false);

            if (attributes.FirstOrDefault() is not { } attribute) continue;

            if (!controls.TryGetValue(attribute.Category, out var catControls))
            {
                catControls = [];
                controls.Add(attribute.Category, catControls);
            }

            if (field.FieldType.IsSubclassOf(typeof(ThemedUserControl)))
            {
                var constructor = field.FieldType.GetConstructor([typeof(MainWindow)]);
                if (constructor is null)
                    throw new Exception($"Failed to find constructor for settings control class '{field.Name}'");

                catControls.Add(new CustomControlSetting(attribute.Name ?? field.Name, attribute.Description ?? "",
                    window => constructor.Invoke([window]) as ThemedUserControl));
            }
        }

        return controls
            .OrderBy(p => p.Key)
            .ToDictionary(p => p.Key, p => p.Value);
    }

    private static string HumanizeName(string name) =>
        string.Concat(name.Select((ch, i) => i > 0 && char.IsUpper(ch) && !char.IsUpper(name[i - 1]) ? " " + ch : ch.ToString()));

    private static string EnumOptionName(Type enumType, object value) =>
        enumType.GetField(value.ToString()!)?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? HumanizeName(value.ToString()!);

    #region General
    //C app-wide: stored next to the default data folder by FoxyFileManager, not in this instance's settings
    [JsonIgnore]
    [SettingInfo(Category = SettingsCategory.General,
        PickerEnabled = true, ItemType = FoxyFileManager.ItemType.Folder,
        Name = "Browser Data Folder Path",
        Description = "Where FoxyBrowser keeps its data for every instance: settings, history, pins, bookmarks, extensions and assistant chats. Point it at a OneDrive or Google Drive folder to share that data between computers. A change takes effect the next time FoxyBrowser starts: this computer's data is copied there, unless the folder already holds FoxyBrowser data (for example from your other computer), which is then used instead. Site sign-ins, cookies and caches stay on each computer. Empty means the default folder. Avoid running FoxyBrowser on two computers at the same time from one synced folder.")]
    public string BrowserDataPath
    {
        get => FoxyFileManager.DataFolderSetting;
        set
        {
            FoxyFileManager.DataFolderSetting = value;
            OnPropertyChanged();
        }
    }

    //TODO: enum support would be nice here:
    // dictionary map of enum to human readable.
    // detect enum, grab all values as options.
    // [SettingInfo(Category = SettingsCategory.General, Options = ["0", "1", "2", "3"])]
    // public string TestCombo = "1";

    [SettingInfo(Category = SettingsCategory.General, Name = "Theme", Description = "Colors used across the browser UI, including menus, prompts and the downloads panel.", Options = nameof(GetComboOptions))]
    public string ThemeName { get; set => SetProperty(ref field, value); } = "Vast Seas";

    [SettingInfo(Category = SettingsCategory.General, Name = "On startup",
        Description = "What this instance opens when FoxyBrowser starts. After a crash the last session always comes back.")]
    public StartupMode StartupMode { get; set => SetProperty(ref field, value); } = StartupMode.HomePage;

    [SettingInfo(Category = SettingsCategory.General, Name = "Startup pages",
        Description = "Used when \"On startup\" is \"Open specific pages\". Separate addresses with spaces.")]
    public string StartupPages { get; set => SetProperty(ref field, value); } = string.Empty;

    [SettingInfo(Category = SettingsCategory.General, Name = "Search suggestions",
        Description = "Ask the search provider for suggestions while typing in the address bar. What you type is sent to Google as you type.")]
    public bool SearchSuggestionsEnabled { get; set => SetProperty(ref field, value); } = true;

    private static string[] GetComboOptions(MainWindow mainWindow)
    {
        return mainWindow.Instance.DefaultThemeObject.Themes.Keys
            .Select(k => k.ToString())
            .ToArray();
    }
    #endregion

    #region Privacy
    [SettingInfo(Category = SettingsCategory.Privacy, Name = "Private browsing",
        Description = "Run this instance's pages InPrivate: cookies, site data and cache are thrown away when the browser closes. FoxyBrowser's own history, downloads list and remembered permissions still follow their settings. Takes effect after restarting FoxyBrowser.")]
    public bool PrivateBrowsing { get; set => SetProperty(ref field, value); } = false;

    [SettingInfo(Category = SettingsCategory.Privacy, Name = "Tracking prevention",
        Description = "Blocks trackers across sites. Strict blocks the most but can break some logins and embedded content.")]
    public CoreWebView2TrackingPreventionLevel TrackingPrevention { get; set => SetProperty(ref field, value); } = CoreWebView2TrackingPreventionLevel.Balanced;

    [SettingInfo(Category = SettingsCategory.Privacy, Name = "Offer to save passwords",
        Description = "Let the browser engine offer to save passwords you type into sites.")]
    public bool PasswordAutosaveEnabled { get; set => SetProperty(ref field, value); } = false;

    [SettingInfo(Category = SettingsCategory.Privacy, Name = "Autofill forms",
        Description = "Remember and suggest things you typed into forms, such as names and addresses.")]
    public bool GeneralAutofillEnabled { get; set => SetProperty(ref field, value); } = false;

    [SettingInfo(Category = SettingsCategory.Privacy, Name = "SmartScreen",
        Description = "Check sites and downloads against Microsoft Defender SmartScreen to block phishing and malware.")]
    public bool SmartScreenEnabled { get; set => SetProperty(ref field, value); } = true;

    [JsonIgnore]
    [SettingInfo(Category = SettingsCategory.Privacy, Name = "Clear browsing data", Description = "Delete history, downloads list, cookies and cached files for this instance.")]
    public BrowsingDataController? BrowsingData;
    #endregion

    #region Permissions
    [SettingInfo(Category = SettingsCategory.Permissions, Name = "Remember permission choices",
        Description = "After you allow or block a site's request: Ask follows up with \"Remember this?\", Always saves it straight away, Never applies it to that one request only.")]
    public PermissionRememberMode RememberPermissionChoices { get; set => SetProperty(ref field, value); } = PermissionRememberMode.Ask;

    [SettingInfo(Category = SettingsCategory.Permissions, Name = "Location", Description = "When a site wants to know your location.")]
    public PermissionDefault LocationPermission { get; set => SetProperty(ref field, value); } = PermissionDefault.Ask;

    [SettingInfo(Category = SettingsCategory.Permissions, Name = "Camera", Description = "When a site wants to use your camera.")]
    public PermissionDefault CameraPermission { get; set => SetProperty(ref field, value); } = PermissionDefault.Ask;

    [SettingInfo(Category = SettingsCategory.Permissions, Name = "Microphone", Description = "When a site wants to use your microphone.")]
    public PermissionDefault MicrophonePermission { get; set => SetProperty(ref field, value); } = PermissionDefault.Ask;

    [SettingInfo(Category = SettingsCategory.Permissions, Name = "Notifications", Description = "When a site wants to show notifications.")]
    public PermissionDefault NotificationsPermission { get; set => SetProperty(ref field, value); } = PermissionDefault.Ask;

    [SettingInfo(Category = SettingsCategory.Permissions, Name = "Clipboard", Description = "When a site wants to read text and images you copied.")]
    public PermissionDefault ClipboardReadPermission { get; set => SetProperty(ref field, value); } = PermissionDefault.Ask;

    [SettingInfo(Category = SettingsCategory.Permissions, Name = "Motion sensors", Description = "When a site wants to use motion and light sensors.")]
    public PermissionDefault SensorsPermission { get; set => SetProperty(ref field, value); } = PermissionDefault.Allow;

    [SettingInfo(Category = SettingsCategory.Permissions, Name = "Automatic downloads", Description = "When a site tries to download several files at once.")]
    public PermissionDefault AutomaticDownloadsPermission { get; set => SetProperty(ref field, value); } = PermissionDefault.Ask;

    [SettingInfo(Category = SettingsCategory.Permissions, Name = "File editing", Description = "When a site wants to edit files or folders on your device.")]
    public PermissionDefault FileEditingPermission { get; set => SetProperty(ref field, value); } = PermissionDefault.Ask;

    [SettingInfo(Category = SettingsCategory.Permissions, Name = "Autoplay", Description = "When a site wants to play media with sound automatically.")]
    public PermissionDefault AutoplayPermission { get; set => SetProperty(ref field, value); } = PermissionDefault.Allow;

    [SettingInfo(Category = SettingsCategory.Permissions, Name = "Local fonts", Description = "When a site wants to use fonts installed on your computer.")]
    public PermissionDefault LocalFontsPermission { get; set => SetProperty(ref field, value); } = PermissionDefault.Ask;

    [SettingInfo(Category = SettingsCategory.Permissions, Name = "MIDI devices", Description = "When a site wants full control of MIDI devices.")]
    public PermissionDefault MidiPermission { get; set => SetProperty(ref field, value); } = PermissionDefault.Ask;

    [SettingInfo(Category = SettingsCategory.Permissions, Name = "Window management", Description = "When a site wants to open and place windows on all your displays.")]
    public PermissionDefault WindowManagementPermission { get; set => SetProperty(ref field, value); } = PermissionDefault.Ask;

    [JsonIgnore]
    [SettingInfo(Category = SettingsCategory.Permissions, Name = "Remembered site decisions", Description = "Permissions you allowed or blocked for specific sites.")]
    public SitePermissionsController? SitePermissionsManager;
    #endregion

    #region Downloads
    [SettingInfo(Category = SettingsCategory.Downloads, Name = "Download folder",
        PickerEnabled = true, ItemType = FoxyFileManager.ItemType.Folder,
        Description = "Where downloads are saved. Leave empty to use your Downloads folder.")]
    public string DownloadFolder { get; set => SetProperty(ref field, value); } = string.Empty;

    [SettingInfo(Category = SettingsCategory.Downloads, Name = "Ask where to save each file",
        Description = "Show a save dialog for every download instead of saving straight to the download folder.")]
    public bool AskWhereToSaveDownloads { get; set => SetProperty(ref field, value); } = false;

    [SettingInfo(Category = SettingsCategory.Downloads, Name = "Show downloads when one starts",
        Description = "Open the downloads panel automatically when a download begins.")]
    public bool ShowDownloadsOnStart { get; set => SetProperty(ref field, value); } = true;

    [SettingInfo(Category = SettingsCategory.Downloads, Name = "Themed downloads panel",
        Description = "Use FoxyBrowser's downloads panel. Turn off to use the browser engine's built-in download menu.")]
    public bool ThemedDownloads { get; set => SetProperty(ref field, value); } = true;
    #endregion

    #region History
    [SettingInfo(Category = SettingsCategory.History, Name = "Save browsing history",
        Description = "Remember the pages you visit.")]
    public bool SaveHistory { get; set => SetProperty(ref field, value); } = true;

    [SettingInfo(Category = SettingsCategory.History, Name = "Keep history for (days)",
        Description = "Pages not visited for this many days are forgotten when the browser starts. 0 keeps history forever.", MinValue = 0, MaxValue = 3650)]
    public int HistoryRetentionDays { get; set => SetProperty(ref field, value); } = 90;

    [SettingInfo(Category = SettingsCategory.History, Name = "Suggest history in the address bar",
        Description = "Show matching pages from your history while typing in the address bar.")]
    public bool HistorySuggestionsEnabled { get; set => SetProperty(ref field, value); } = true;
    #endregion

    #region Extensions
    [JsonIgnore] // VERY IMPORTANT. Without this, the entire control will be serialized/saved as JSON. Will inflate the size of the file!!!
    [SettingInfo(Category = SettingsCategory.Extensions, Name = "Installed extensions", Description = "Manage installed extensions for the current instance only.")]
    public ExtensionsController? Extensions;

    // Claude Doc
    /// <summary>Folder names (store ids) of extensions the user turned off. Not shown as a setting; edited by <see cref="ExtensionsController"/>.</summary>
    public List<string> DisabledExtensions { get; set => SetProperty(ref field, value); } = [];
    #endregion

    #region Assistant
    [SettingInfo(Category = SettingsCategory.Assistant, Name = "AI assistant",
        Description = "Show the AI assistant panel (☰ menu → AI Assistant) for this instance. Its models, keys, budget and permissions are set in the panel itself (the gear at its top).")]
    public bool AiEnabled { get; set => SetProperty(ref field, value); } = true;

    //C The rest are edited in the panel's own settings view (AiSettingsView), not the settings page. API keys and the
    //C Claude budget are shared by every instance and live elsewhere (AiCredentials, AiBudget).

    public AiModelChoice AiModel { get; set => SetProperty(ref field, value); } = AiModelChoice.Default;

    public AiEffort AiEffort { get; set => SetProperty(ref field, value); } = AiEffort.Medium;

    // Claude Doc
    /// <summary>The /v1 address of a server that speaks OpenAI's chat completions API.</summary>
    public string OpenAiBaseUrl { get; set => SetProperty(ref field, value); } = "https://api.openai.com/v1";

    public AiPermissionMode AiPermissionMode { get; set => SetProperty(ref field, value); } = AiPermissionMode.ReadOnly;

    // Claude Doc
    /// <summary>Per-tool choices for <see cref="AiPermissionMode.Custom"/>, by tool name. Edited by <see cref="AiToolPermissionsController"/>; replace the dictionary to save.</summary>
    public Dictionary<string, AiToolPermission> AiToolPermissions { get; set => SetProperty(ref field, value); } = [];

    public bool AiNameTabGroups { get; set => SetProperty(ref field, value); } = false;

    public AiModelChoice AiQuickModel { get; set => SetProperty(ref field, value); } = AiModelChoice.DefaultQuick;

    // Claude Doc
    /// <summary>
    /// Let the assistant see and use other instances' windows and tabs; it always has this instance's. Private
    /// instances stay walled off either way.
    /// </summary>
    public bool AiSeeOtherInstances { get; set => SetProperty(ref field, value); } = false;

    //C null until changed: then chats are saved except in private instances
    public bool? AiSaveChats { get; set => SetProperty(ref field, value); }

    [JsonIgnore]
    public bool AiSaveChatsEffective => AiSaveChats ?? !PrivateBrowsing;

    // Claude Doc
    /// <summary>The chat panel's system prompt for new chats; empty uses <see cref="AiChat.DefaultSystemPrompt"/>, so edits to the default still reach users who never changed it.</summary>
    public string AiSystemPrompt { get; set => SetProperty(ref field, value); } = "";
    #endregion

    #region WebView2
    [SettingInfo(Category = SettingsCategory.WebView2, Name = "Themed context menu",
        Description = "Use FoxyBrowser's menu when right-clicking a page. Turn off to use the browser engine's menu.")]
    public bool ThemedContextMenu { get; set => SetProperty(ref field, value); } = true;

    [SettingInfo(Category = SettingsCategory.WebView2, Name = "Themed site dialogs",
        Description = "Use FoxyBrowser's look for alert/confirm/prompt dialogs, sign-in prompts and permission requests.")]
    public bool ThemedDialogs { get; set => SetProperty(ref field, value); } = true;

    [SettingInfo(Category = SettingsCategory.WebView2, Name = "Website color scheme",
        Description = "Which color scheme sites are told you prefer. Auto follows Windows.")]
    public CoreWebView2PreferredColorScheme WebsiteColorScheme { get; set => SetProperty(ref field, value); } = CoreWebView2PreferredColorScheme.Auto;

    [SettingInfo(Category = SettingsCategory.WebView2, Name = "Developer tools", Description = "Allow opening DevTools (F12 / Inspect).")]
    public bool DevToolsEnabled { get; set => SetProperty(ref field, value); } = true;

    [SettingInfo(Category = SettingsCategory.WebView2, Name = "Browser keyboard shortcuts",
        Description = "Built-in shortcuts such as Ctrl+F (find), Ctrl+P (print) and F5 (reload).")]
    public bool BrowserAcceleratorKeysEnabled { get; set => SetProperty(ref field, value); } = true;

    [SettingInfo(Category = SettingsCategory.WebView2, Name = "Link status bar", Description = "Show a link's address in the bottom corner when hovering it.")]
    public bool StatusBarEnabled { get; set => SetProperty(ref field, value); } = true;

    [SettingInfo(Category = SettingsCategory.WebView2, Name = "Zoom with Ctrl + scroll", Description = "Allow zooming pages with Ctrl + mouse wheel and Ctrl +/-.")]
    public bool ZoomControlEnabled { get; set => SetProperty(ref field, value); } = true;

    [SettingInfo(Category = SettingsCategory.WebView2, Name = "Pinch to zoom", Description = "Allow touchpad and touchscreen pinch zoom.")]
    public bool PinchZoomEnabled { get; set => SetProperty(ref field, value); } = true;

    [SettingInfo(Category = SettingsCategory.WebView2, Name = "Swipe navigation", Description = "Swipe left/right on a touchpad or touchscreen to go back and forward.")]
    public bool SwipeNavigationEnabled { get; set => SetProperty(ref field, value); } = true;

    [SettingInfo(Category = SettingsCategory.WebView2, Name = "Custom user agent",
        Description = "Replace the user agent string sites see. Leave empty for the default.")]
    public string CustomUserAgent { get; set => SetProperty(ref field, value); } = string.Empty;
    #endregion

    //TODO: look more into this
    //TODO maybe each category as a sub class?
    /* Necessary Settings:
     *
     * Simple:
     * - performance for webview2
     * - performance for my UI
     * - default zoom
     * - startup mode: none, restore, config
     * - restore browser on close?
     * - sleeping tabs and performance settings
     * - reset to defaults
     * - default search engine, last used or specific one
     * - default browser
     *
     * Complex:
     * Theme pick and editing
     * instance management
     * error viewer/reporter/log-exporter
     * startup config
     *
     */
    //TODO: need default static setting to use if none.
}

public enum StartupMode
{
    [Description("Open the home page")] HomePage,
    [Description("Continue where I left off")] ContinueWhereLeftOff,
    [Description("Open specific pages")] SpecificPages,
}

public partial class PerformanceSettings : ObservableObject
{

}

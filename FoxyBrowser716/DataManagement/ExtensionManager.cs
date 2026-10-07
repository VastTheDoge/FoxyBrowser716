using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Web;
using FoxyBrowser716.DataObjects.Basic;
using FoxyBrowser716.DataObjects.Complex;
using FoxyBrowser716.ErrorHandeler;
using Material.Icons.WinUI3;
using Microsoft.Web.WebView2.Core;
using WinUIEx;

namespace FoxyBrowser716.DataManagement;

//TODO: break into different classes once everything works.
// Outline:
// L get current extensions from instance folder.
// L get manifest from extension folder as object IManifest and Manifest, ManifestV2, and ManifestV3.
// L extract extension id and store type from url.
// L build url from store and id.
// L get extension from url and unpack to a file.


#region  ManifestStuff
[JsonConverter(typeof(IconsConverter))]
public class Icons : Dictionary<string, string> { }

public class ActionInfo
{
    [JsonPropertyName("default_icon")]
    public Icons? DefaultIcon { get; set; }

    [JsonPropertyName("default_popup")]
    public string? DefaultPopup { get; set; }

    [JsonPropertyName("default_title")]
    public string? DefaultTitle { get; set; }
}

public class BackgroundV3
{
    [JsonPropertyName("service_worker")]
    public string? ServiceWorker { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("scripts")]
    public List<string>? Scripts { get; set; }

    [JsonPropertyName("persistent")]
    public bool? Persistent { get; set; }
}

public class BackgroundV2
{
    [JsonPropertyName("page")]
    public string? Page { get; set; }

    [JsonPropertyName("scripts")]
    public List<string>? Scripts { get; set; }

    [JsonPropertyName("persistent")]
    public bool? Persistent { get; set; }
}

public class ContentScript
{
    [JsonPropertyName("matches")]
    public List<string>? Matches { get; set; }

    [JsonPropertyName("exclude_matches")]
    public List<string>? ExcludeMatches { get; set; }

    [JsonPropertyName("js")]
    public List<string>? Js { get; set; }

    [JsonPropertyName("css")]
    public List<string>? Css { get; set; }

    [JsonPropertyName("run_at")]
    public string? RunAt { get; set; }

    [JsonPropertyName("all_frames")]
    public bool? AllFrames { get; set; }

    [JsonPropertyName("match_about_blank")]
    public bool? MatchAboutBlank { get; set; }

    [JsonPropertyName("world")]
    public string? World { get; set; }

    [JsonPropertyName("include_globs")]
    public List<string>? IncludeGlobs { get; set; }

    [JsonPropertyName("exclude_globs")]
    public List<string>? ExcludeGlobs { get; set; }
}

public class WebAccessibleResource
{
    [JsonPropertyName("resources")]
    public List<string>? Resources { get; set; }

    [JsonPropertyName("matches")]
    public List<string>? Matches { get; set; }

    [JsonPropertyName("extension_ids")]
    public List<string>? ExtensionIds { get; set; }

    [JsonPropertyName("use_dynamic_url")]
    public bool? UseDynamicUrl { get; set; }
}

public class OptionsUI
{
    [JsonPropertyName("open_in_tab")]
    public bool? OpenInTab { get; set; }

    [JsonPropertyName("page")]
    public string? Page { get; set; }

    [JsonPropertyName("chrome_style")]
    public bool? ChromeStyle { get; set; }
}

public class CommandDefinition
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("suggested_key")]
    public JsonElement? SuggestedKey { get; set; }

    [JsonPropertyName("global")]
    public bool? Global { get; set; }
}

// Custom converter for ContentSecurityPolicy which can be string or object
[JsonConverter(typeof(ContentSecurityPolicyConverter))]
public class ContentSecurityPolicy
{
    public string? ExtensionPages { get; set; }
    public string? SandboxedPages { get; set; }
    public string? IsolatedWorld { get; set; }
    
    // For backwards compatibility when it's just a string
    public string? Policy { get; set; }

    public override string? ToString() => Policy ?? ExtensionPages;
}

// Custom class for Author field which can be string or object
[JsonConverter(typeof(AuthorConverter))]
public class Author
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    
    public override string? ToString() => Name ?? Email;
    
    public static implicit operator string?(Author? author) => author?.ToString();
    public static implicit operator Author?(string? str) => str == null ? null : new Author { Name = str };
}

public class ExtensionManifestV3 : ExtensionManifestBase
{
    [JsonPropertyName("icons")]
    public Icons? Icons { get; set; }

    [JsonPropertyName("action")]
    public ActionInfo? Action { get; set; }

    [JsonPropertyName("background")]
    public BackgroundV3? Background { get; set; }

    [JsonPropertyName("permissions")]
    public List<string>? Permissions { get; set; }

    [JsonPropertyName("host_permissions")]
    public List<string>? HostPermissions { get; set; }

    [JsonPropertyName("optional_permissions")]
    public List<string>? OptionalPermissions { get; set; }

    [JsonPropertyName("optional_host_permissions")]
    public List<string>? OptionalHostPermissions { get; set; }

    [JsonPropertyName("content_scripts")]
    public List<ContentScript>? ContentScripts { get; set; }

    [JsonPropertyName("web_accessible_resources")]
    [JsonConverter(typeof(WebAccessibleResourcesConverter))]
    public List<WebAccessibleResource>? WebAccessibleResources { get; set; }

    [JsonPropertyName("options_page")]
    public string? OptionsPage { get; set; }

    [JsonPropertyName("options_ui")]
    public OptionsUI? OptionsUI { get; set; }

    [JsonPropertyName("commands")]
    public Dictionary<string, CommandDefinition>? Commands { get; set; }

    [JsonPropertyName("incognito")]
    public string? Incognito { get; set; }

    [JsonPropertyName("author")]
    public Author? Author { get; set; }

    [JsonPropertyName("storage")]
    public JsonElement? Storage { get; set; }

    [JsonPropertyName("externally_connectable")]
    public JsonElement? ExternallyConnectable { get; set; }

    [JsonPropertyName("content_security_policy")]
    public ContentSecurityPolicy? ContentSecurityPolicy { get; set; }
}

public class ExtensionManifestV2 : ExtensionManifestBase
{
    [JsonPropertyName("icons")]
    public Icons? Icons { get; set; }

    [JsonPropertyName("browser_action")]
    public ActionInfo? BrowserAction { get; set; }

    [JsonPropertyName("page_action")]
    public ActionInfo? PageAction { get; set; }

    [JsonPropertyName("background")]
    public BackgroundV2? Background { get; set; }

    [JsonPropertyName("permissions")]
    public List<string>? Permissions { get; set; }

    [JsonPropertyName("optional_permissions")]
    public List<string>? OptionalPermissions { get; set; }

    [JsonPropertyName("content_scripts")]
    public List<ContentScript>? ContentScripts { get; set; }

    [JsonPropertyName("options_ui")]
    public OptionsUI? OptionsUI { get; set; }

    [JsonPropertyName("web_accessible_resources")]
    public List<string>? WebAccessibleResources { get; set; }

    [JsonPropertyName("externally_connectable")]
    public JsonElement? ExternallyConnectable { get; set; }

    [JsonPropertyName("content_security_policy")]
    public string? ContentSecurityPolicy { get; set; }
}

// Converter for Icons (handles string or object)
public class IconsConverter : JsonConverter<Icons>
{
    public override Icons Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var icons = new Icons();

        if (reader.TokenType == JsonTokenType.String)
        {
            var iconPath = reader.GetString();
            if (!string.IsNullOrEmpty(iconPath))
            {
                icons["default"] = iconPath;
            }
        }
        else if (reader.TokenType == JsonTokenType.StartObject)
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options);
            if (dict != null)
            {
                foreach (var kvp in dict)
                {
                    icons[kvp.Key] = kvp.Value;
                }
            }
        }
        else if (reader.TokenType == JsonTokenType.Null)
        {
            // Handle null case
            return icons;
        }

        return icons;
    }

    public override void Write(Utf8JsonWriter writer, Icons value, JsonSerializerOptions options)
    {
        if (value.Count == 1 && value.ContainsKey("default"))
        {
            writer.WriteStringValue(value["default"]);
        }
        else
        {
            JsonSerializer.Serialize(writer, (Dictionary<string, string>)value, options);
        }
    }
}

// Converter for ContentSecurityPolicy (handles string or object)
public class ContentSecurityPolicyConverter : JsonConverter<ContentSecurityPolicy>
{
    public override ContentSecurityPolicy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var csp = new ContentSecurityPolicy();

        if (reader.TokenType == JsonTokenType.String)
        {
            csp.Policy = reader.GetString();
        }
        else if (reader.TokenType == JsonTokenType.StartObject)
        {
            var element = JsonSerializer.Deserialize<JsonElement>(ref reader, options);
            
            if (element.TryGetProperty("extension_pages", out var extPages))
                csp.ExtensionPages = extPages.GetString();
                
            if (element.TryGetProperty("sandboxed_pages", out var sandboxPages))
                csp.SandboxedPages = sandboxPages.GetString();
                
            if (element.TryGetProperty("isolated_world", out var isolatedWorld))
                csp.IsolatedWorld = isolatedWorld.GetString();
        }
        else if (reader.TokenType == JsonTokenType.Null)
        {
            return csp;
        }

        return csp;
    }

    public override void Write(Utf8JsonWriter writer, ContentSecurityPolicy value, JsonSerializerOptions options)
    {
        if (!string.IsNullOrEmpty(value.Policy))
        {
            writer.WriteStringValue(value.Policy);
        }
        else
        {
            writer.WriteStartObject();
            if (!string.IsNullOrEmpty(value.ExtensionPages))
                writer.WriteString("extension_pages", value.ExtensionPages);
            if (!string.IsNullOrEmpty(value.SandboxedPages))
                writer.WriteString("sandboxed_pages", value.SandboxedPages);
            if (!string.IsNullOrEmpty(value.IsolatedWorld))
                writer.WriteString("isolated_world", value.IsolatedWorld);
            writer.WriteEndObject();
        }
    }
}

// Converter for Author (handles string or object)
public class AuthorConverter : JsonConverter<Author>
{
    public override Author Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var author = new Author();

        if (reader.TokenType == JsonTokenType.String)
        {
            author.Name = reader.GetString();
        }
        else if (reader.TokenType == JsonTokenType.StartObject)
        {
            var element = JsonSerializer.Deserialize<JsonElement>(ref reader, options);
            
            if (element.TryGetProperty("name", out var name))
                author.Name = name.GetString();
                
            if (element.TryGetProperty("email", out var email))
                author.Email = email.GetString();
        }
        else if (reader.TokenType == JsonTokenType.Null)
        {
            return author;
        }

        return author;
    }

    public override void Write(Utf8JsonWriter writer, Author value, JsonSerializerOptions options)
    {
        if (!string.IsNullOrEmpty(value.Name) && string.IsNullOrEmpty(value.Email))
        {
            writer.WriteStringValue(value.Name);
        }
        else
        {
            writer.WriteStartObject();
            if (!string.IsNullOrEmpty(value.Name))
                writer.WriteString("name", value.Name);
            if (!string.IsNullOrEmpty(value.Email))
                writer.WriteString("email", value.Email);
            writer.WriteEndObject();
        }
    }
}

// Enhanced WebAccessibleResourcesConverter
public class WebAccessibleResourcesConverter : JsonConverter<List<WebAccessibleResource>>
{
    public override List<WebAccessibleResource> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var outList = new List<WebAccessibleResource>();

        if (reader.TokenType == JsonTokenType.Null)
            return outList;

        var element = JsonSerializer.Deserialize<JsonElement>(ref reader, options);

        if (element.ValueKind != JsonValueKind.Array) 
            return outList;

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                outList.Add(new WebAccessibleResource { Resources = new List<string> { item.GetString()! } });
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var war = new WebAccessibleResource();

                if (item.TryGetProperty("resources", out var resources))
                {
                    var list = new List<string>();
                    if (resources.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var r in resources.EnumerateArray())
                            if (r.ValueKind == JsonValueKind.String) 
                                list.Add(r.GetString()!);
                    }
                    else if (resources.ValueKind == JsonValueKind.String)
                    {
                        list.Add(resources.GetString()!);
                    }
                    war.Resources = list;
                }

                if (item.TryGetProperty("matches", out var matches))
                {
                    var list = new List<string>();
                    if (matches.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in matches.EnumerateArray())
                            if (m.ValueKind == JsonValueKind.String) 
                                list.Add(m.GetString()!);
                    }
                    else if (matches.ValueKind == JsonValueKind.String)
                    {
                        list.Add(matches.GetString()!);
                    }
                    war.Matches = list;
                }

                if (item.TryGetProperty("extension_ids", out var extIds))
                {
                    var list = new List<string>();
                    if (extIds.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in extIds.EnumerateArray())
                            if (e.ValueKind == JsonValueKind.String) 
                                list.Add(e.GetString()!);
                    }
                    else if (extIds.ValueKind == JsonValueKind.String)
                    {
                        list.Add(extIds.GetString()!);
                    }
                    war.ExtensionIds = list;
                }

                if (item.TryGetProperty("use_dynamic_url", out var useDynamicUrl))
                {
                    if (useDynamicUrl.ValueKind == JsonValueKind.True)
                        war.UseDynamicUrl = true;
                    else if (useDynamicUrl.ValueKind == JsonValueKind.False)
                        war.UseDynamicUrl = false;
                }

                outList.Add(war);
            }
        }

        return outList;
    }

    public override void Write(Utf8JsonWriter writer, List<WebAccessibleResource> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}

public class LocalizedMessages
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Messages { get; set; }

    public string? GetMessage(string key)
    {
        if (Messages == null) return null;
        
        if (Messages.TryGetValue(key, out var element))
        {
            if (element.ValueKind == JsonValueKind.Object && 
                element.TryGetProperty("message", out var messageElement))
            {
                return messageElement.GetString();
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }
        }
        return null;
    }
}

public abstract class ExtensionManifestBase
{
    [JsonPropertyName("manifest_version")]
    public int ManifestVersion { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("short_name")]
    public string? ShortName { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("version_name")]
    public string? VersionName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("default_locale")]
    public string? DefaultLocale { get; set; }

    [JsonPropertyName("update_url")]
    public string? UpdateUrl { get; set; }

    [JsonPropertyName("minimum_chrome_version")]
    public string? MinimumChromeVersion { get; set; }

    [JsonPropertyName("homepage_url")]
    public string? HomepageUrl { get; set; }

    [JsonPropertyName("offline_enabled")]
    public bool? OfflineEnabled { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }

    [JsonIgnore]
    public string? ExtensionFolderPath { get; set; }

    public T? GetExtraValue<T>(string key)
    {
        if (ExtraData == null) return default;
        if (!ExtraData.TryGetValue(key, out var el)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(el.GetRawText(), DefaultOptions);
        }
        catch
        {
            return default;
        }
    }

    // Get localized version of a field value
    public string? GetLocalizedValue(string? value, string language = "en")
    {
        if (string.IsNullOrEmpty(value) || !IsLocalizedString(value))
            return value;

        return ExtractLocalizedString(value, language) ?? value;
    }

    // Get localized name
    public string? GetLocalizedName(string language = "en")
    {
        return GetLocalizedValue(Name, language);
    }

    // Get localized short name
    public string? GetLocalizedShortName(string language = "en")
    {
        return GetLocalizedValue(ShortName, language);
    }

    // Get localized description
    public string? GetLocalizedDescription(string language = "en")
    {
        return GetLocalizedValue(Description, language);
    }

    // Check if a string is a localization key
    private static bool IsLocalizedString(string? value)
    {
        return !string.IsNullOrEmpty(value) && 
               value.StartsWith("__MSG_") && 
               value.EndsWith("__");
    }

    private string? ExtractLocalizedString(string localizedKey, string language = "en")
    {
        if (string.IsNullOrEmpty(ExtensionFolderPath) || !IsLocalizedString(localizedKey))
            return null;

        var messageKey = localizedKey.Substring(6, localizedKey.Length - 8);
        
        var localizedMessage = GetLocalizedMessage(messageKey, language);
        
        if (localizedMessage == null && !string.IsNullOrEmpty(DefaultLocale) && DefaultLocale != language)
        {
            localizedMessage = GetLocalizedMessage(messageKey, DefaultLocale);
        }
        
        if (localizedMessage == null && language != "en" && DefaultLocale != "en")
        {
            localizedMessage = GetLocalizedMessage(messageKey, "en");
        }

        return localizedMessage;
    }

    private string? GetLocalizedMessage(string messageKey, string language)
    {
        if (string.IsNullOrEmpty(ExtensionFolderPath))
            return null;

        var messagesPath = Path.Combine(ExtensionFolderPath, "_locales", language, "messages.json");
        
        if (!File.Exists(messagesPath))
            return null;

        try
        {
            var json = File.ReadAllText(messagesPath);
            var messages = JsonSerializer.Deserialize<LocalizedMessages>(json, DefaultOptions);
            return messages?.GetMessage(messageKey);
        }
        catch
        {
            return null;
        }
    }

    protected static JsonSerializerOptions DefaultOptions => new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
}

public static class ExtensionManifestParser
{
    private static JsonSerializerOptions Options
    {
        get
        {
            var o = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            };
            o.Converters.Add(new WebAccessibleResourcesConverter());
            o.Converters.Add(new IconsConverter());
            o.Converters.Add(new ContentSecurityPolicyConverter());
            o.Converters.Add(new AuthorConverter());
            return o;
        }
    }

    public static ExtensionManifestBase Parse(string json, string? extensionFolderPath = null)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int manifestVersion = 2;
        if (root.TryGetProperty("manifest_version", out var mv))
        {
            if (mv.ValueKind == JsonValueKind.Number && mv.TryGetInt32(out var iv)) 
                manifestVersion = iv;
            else if (mv.ValueKind == JsonValueKind.String && int.TryParse(mv.GetString(), out var sval)) 
                manifestVersion = sval;
        }

        ExtensionManifestBase result;
        try
        {
            if (manifestVersion >= 3)
            {
                var v3 = JsonSerializer.Deserialize<ExtensionManifestV3>(json, Options);
                result = v3 ?? throw new InvalidOperationException("Failed to deserialize as V3.");
            }
            else
            {
                var v2 = JsonSerializer.Deserialize<ExtensionManifestV2>(json, Options);
                result = v2 ?? throw new InvalidOperationException("Failed to deserialize as V2.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse extension manifest: {ex.Message}", ex);
        }

        result.ExtensionFolderPath = extensionFolderPath;
        return result;
    }

    public static async Task<ExtensionManifestBase> ParseFromFileAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        var extensionFolderPath = Path.GetDirectoryName(path);
        return Parse(json, extensionFolderPath);
    }
}
#endregion

public static class ExtensionManager
{
    public static event Action<string>? ExtensionsModified;

    public const string ChromeWebStoreUrl = "https://chromewebstore.google.com/";
    public const string EdgeAddonsUrl = "https://microsoftedge.microsoft.com/addons/";
    
	readonly static string[] _whitelist = ["Microsoft Clipboard Extension", "Microsoft Edge PDF Viewer"];
	
	/// <summary>
	/// Instance name to extension list
	/// </summary>
	private static ConcurrentDictionary<string, List<Extension>> _extensions = [];

	/// <summary>
	/// Every tab calls <see cref="SetupExtensionSupport"/>; restoring a session creates many tabs at once, and
	/// without this they would all try to add the same extension folders to the profile concurrently.
	/// </summary>
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> _loadLocks = [];

	/// <summary>Folder name of an extension, which is its store id for store installs. Used as its stable key.</summary>
	public static string GetFolderKey(Extension extension) => Path.GetFileName(extension.FolderPath.TrimEnd('\\', '/'));

	private static bool IsDisabled(Instance instance, Extension extension) =>
		instance.Settings.DisabledExtensions.Contains(GetFolderKey(extension));
	
	/// <summary>
	/// 
	/// </summary>
	/// <param name="webview"></param>
	/// <param name="instance"></param>
	public static async Task SetupExtensionSupport(this Instance instance, WebView2 webview)
	{
        //setup JS for MS Store support:
        webview.CoreWebView2.WebMessageReceived += async (_,e) =>
        {
            string raw = e.TryGetWebMessageAsString();
            try {
                var doc = System.Text.Json.JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if(root.TryGetProperty("type", out var t)){
                    var type = t.GetString();
                    if(type == "installButtonClicked"){
                        var href = root.GetProperty("href").GetString();
                        var buttonId = root.GetProperty("buttonId").GetString();
                        Debug.WriteLine($"Install clicked: href={href} id={buttonId}");
                        var id = ExtractExtensionIdFromUrl(href);
                        if(id is null) return;
                        await instance.AddExtension(webview, id, ExtensionSource.Microsoft);
                    } else {
                        Debug.WriteLine("Page message: " + raw);
                    }
                }
            } catch(Exception ex){
                Debug.WriteLine("Failed parse web message: " + ex.Message + " raw:" + raw);
                FoxyLogger.AddError(ex);
                Notify(instance, webview, "Extension install failed", ex.Message, isError: true);
            }
        };
        
        await webview.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(MicrosoftStoreScript);

        var loadLock = _loadLocks.GetOrAdd(instance.Name, _ => new SemaphoreSlim(1, 1));
        await loadLock.WaitAsync();
        try
        {
            var currentExtensions = (await webview.CoreWebView2.Profile.GetBrowserExtensionsAsync())
                .Where(e => !_whitelist.Contains(e.Name))
                .ToList();

            var extensionFolder = FoxyFileManager.BuildFolderPath(FoxyFileManager.FolderType.Extension, instance.Name);

            // match folders to what the profile already has loaded; anything missing (first run, or a
            // folder added since) is added to the profile in parallel
            // match by the id seen earlier this session when there is one; names can be localized differently
            var known = _extensions.TryGetValue(instance.Name, out var previous) ? previous.ToList() : [];
            List<(Extension folder, CoreWebView2BrowserExtension? loaded)> found = [];
            await foreach (var ex in GetFolderExtensions(extensionFolder))
            {
                var knownId = known.FirstOrDefault(k => string.Equals(k.FolderPath, ex.FolderPath, StringComparison.OrdinalIgnoreCase))?.Id;
                found.Add((ex, currentExtensions.FirstOrDefault(e => e.Id == knownId)
                               ?? currentExtensions.FirstOrDefault(e => IsNamesEqual(e.Name, ex.Manifest))));
            }

            var added = await Task.WhenAll(found.Select(async pair =>
            {
                if (pair.loaded is not null) return pair;
                try
                {
                    return (pair.folder, await webview.CoreWebView2.Profile.AddBrowserExtensionAsync(pair.folder.FolderPath));
                }
                catch (Exception e)
                {
                    FoxyLogger.AddError(e);
                    return pair;
                }
            }));

            List<Extension> extensionsList = [];
            foreach (var (folder, loaded) in added)
            {
                if (loaded is null) continue;

                var enabled = !IsDisabled(instance, folder);
                if (loaded.IsEnabled != enabled)
                    await loaded.EnableAsync(enabled);

                extensionsList.Add(new Extension
                {
                    FolderPath = folder.FolderPath,
                    Manifest = folder.Manifest,
                    WebviewName = loaded.Name,
                    Id = loaded.Id,
                    IsEnabled = enabled,
                });
            }
            _extensions[instance.Name] = extensionsList;
        }
        finally
        {
            loadLock.Release();
        }
        ExtensionsModified?.Invoke(instance.Name);
		
		// setup capturing of extension downloads:
		webview.CoreWebView2.DownloadStarting +=
			async (_, e) =>
			{
				if (webview.CoreWebView2.Source.Contains("chromewebstore.google.com"))
				{
					e.Handled = true;
					try
					{
						await instance.AddExtension(webview, e);
					}
					catch (Exception ex)
					{
						// this is an async void handler: an escaping exception would take the browser down
						FoxyLogger.AddError(ex);
						Notify(instance, webview, "Extension install failed", ex.Message, isError: true);
					}
				}
				
			};
	}

    public static async Task RemoveExtension(this Instance instance, WebView2 webview, string id)
    {
        if (_extensions.TryGetValue(instance.Name, out var extensions))
        {
            var webviewEx = (await webview.CoreWebView2.Profile.GetBrowserExtensionsAsync())
                .FirstOrDefault(e => e.Id == id);
            var localEx = extensions.FirstOrDefault(e => e.Id == id);
            
            if (localEx is null) return;

            if (webviewEx is not null)
                await webviewEx.RemoveAsync();
            extensions.Remove(localEx);
            FoxyFileManager.DeleteFolder(localEx.FolderPath);

            var key = GetFolderKey(localEx);
            if (instance.Settings.DisabledExtensions.Contains(key))
                instance.Settings.DisabledExtensions = instance.Settings.DisabledExtensions.Where(k => k != key).ToList();

            ExtensionsModified?.Invoke(instance.Name);
        }
    }

    /// <summary>Turns an extension on or off in the profile and remembers the choice across restarts.</summary>
    public static async Task SetExtensionEnabled(this Instance instance, WebView2 webview, Extension extension, bool enabled)
    {
        var key = GetFolderKey(extension);
        var disabled = instance.Settings.DisabledExtensions.Where(k => k != key).ToList();
        if (!enabled) disabled.Add(key);
        // a new list (not an in-place edit) so the property setter fires and the settings get saved
        instance.Settings.DisabledExtensions = disabled;

        var webviewEx = (await webview.CoreWebView2.Profile.GetBrowserExtensionsAsync())
            .FirstOrDefault(e => e.Id == extension.Id);
        if (webviewEx is not null && webviewEx.IsEnabled != enabled)
            await webviewEx.EnableAsync(enabled);

        extension.IsEnabled = enabled;
        ExtensionsModified?.Invoke(instance.Name);
    }

    public enum UpdateResult
    {
        Updated,
        UpToDate,
        /// <summary>Installed unpacked (or from an unknown store), so there is nowhere to update from.</summary>
        NotFromStore,
    }

    /// <summary>Which store an extension came from, from its manifest's update_url.</summary>
    public static ExtensionSource? GetStoreSource(Extension extension) => extension.Manifest.UpdateUrl switch
    {
        { } url when url.Contains("edge.microsoft.com", StringComparison.OrdinalIgnoreCase) => ExtensionSource.Microsoft,
        { } url when url.Contains("google.com", StringComparison.OrdinalIgnoreCase) => ExtensionSource.Chrome,
        _ => null,
    };

    /// <summary>
    /// Downloads the current version from the extension's store and reinstalls it if it is newer.
    /// Throws if the download or install fails.
    /// </summary>
    public static async Task<(UpdateResult result, string? version)> UpdateExtension(this Instance instance, WebView2 webview, Extension extension)
    {
        if (GetStoreSource(extension) is not { } source)
            return (UpdateResult.NotFromStore, extension.Manifest.Version);

        var id = GetFolderKey(extension);
        var crxBytes = await DownloadCrx(id, source);

        // unpack to a scratch folder first so an up-to-date extension is never touched
        var scratch = Path.Combine(FoxyFileManager.BuildFolderPath(FoxyFileManager.FolderType.Cache, instance.Name), "ExtensionUpdate-" + Guid.NewGuid().ToString("N"));
        string? newVersion;
        try
        {
            ExtractCrx(crxBytes, scratch);
            newVersion = (await GetFolderExtension(scratch))?.Manifest.Version;
        }
        finally
        {
            FoxyFileManager.DeleteFolder(scratch);
        }

        if (CompareVersions(newVersion, extension.Manifest.Version) <= 0)
            return (UpdateResult.UpToDate, extension.Manifest.Version);

        await ProcessCrxFile(instance, webview, id, crxBytes, isUpdate: true);
        return (UpdateResult.Updated, newVersion);
    }

    /// <summary>
    /// Copies an unpacked extension folder (one with a manifest.json, e.g. one you are developing) into this
    /// instance's extensions and loads it. Re-run it after changing the source folder.
    /// </summary>
    public static async Task<Extension> InstallUnpacked(this Instance instance, WebView2 webview, string sourceFolder)
    {
        if (!File.Exists(Path.Combine(sourceFolder, "manifest.json")))
            throw new Exception("That folder has no manifest.json, so it is not an unpacked extension.");

        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(Path.GetFileName(sourceFolder.TrimEnd('\\', '/')).Where(c => !invalid.Contains(c)).ToArray());
        var target = Path.Combine(FoxyFileManager.BuildFolderPath(FoxyFileManager.FolderType.Extension, instance.Name), $"unpacked-{name}");

        // replacing a previous copy: unload it first
        if (instance.GetSavedExtensions().FirstOrDefault(e => string.Equals(e.FolderPath, target, StringComparison.OrdinalIgnoreCase)) is { } previous)
            await instance.RemoveExtension(webview, previous.Id);
        FoxyFileManager.DeleteFolder(target);

        CopyDirectory(sourceFolder, target);

        var extension = await GetFolderExtension(target);
        if (extension is null)
        {
            FoxyFileManager.DeleteFolder(target);
            throw new Exception("The extension's manifest.json could not be read.");
        }

        var browserExtension = await webview.CoreWebView2.Profile.AddBrowserExtensionAsync(target);
        var installed = new Extension
        {
            FolderPath = extension.FolderPath,
            Manifest = extension.Manifest,
            WebviewName = browserExtension.Name,
            Id = browserExtension.Id,
            IsEnabled = true,
        };
        _extensions.GetOrAdd(instance.Name, _ => []).Add(installed);
        ExtensionsModified?.Invoke(instance.Name);
        return installed;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        foreach (var directory in Directory.GetDirectories(source))
        {
            // version control and dependency folders are not part of a loadable extension
            var dirName = Path.GetFileName(directory);
            if (dirName is ".git" or "node_modules") continue;
            CopyDirectory(directory, Path.Combine(target, dirName));
        }
    }

    /// <summary>Compares dotted extension versions ("1.10.2" &gt; "1.9"). Unparsable parts count as 0.</summary>
    public static int CompareVersions(string? a, string? b)
    {
        var left = (a ?? "0").Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var right = (b ?? "0").Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var l = i < left.Length ? left[i] : 0;
            var r = i < right.Length ? right[i] : 0;
            if (l != r) return l.CompareTo(r);
        }
        return 0;
    }

    /// <summary>Localized name, falling back to what WebView2 reports.</summary>
    public static string GetDisplayName(Extension extension) =>
        extension.Manifest.GetLocalizedName() is { Length: > 0 } name && !name.StartsWith("__MSG_")
            ? name
            : extension.WebviewName ?? extension.Manifest.Name ?? GetFolderKey(extension);

    public static string? GetDescription(Extension extension) =>
        extension.Manifest.GetLocalizedDescription() is { Length: > 0 } description && !description.StartsWith("__MSG_")
            ? description
            : null;

    /// <summary>The page the toolbar button opens, relative to the extension root, if it has one.</summary>
    public static string? GetPopupPage(ExtensionManifestBase manifest) => manifest switch
    {
        ExtensionManifestV3 v3 => v3.Action?.DefaultPopup,
        ExtensionManifestV2 v2 => v2.BrowserAction?.DefaultPopup ?? v2.PageAction?.DefaultPopup,
        _ => null,
    } is { Length: > 0 } page ? page.TrimStart('/') : null;

    /// <summary>chrome-extension:// URL of the extension's options page, if it has one.</summary>
    public static string? GetOptionsUrl(Extension extension)
    {
        var page = extension.Manifest switch
        {
            ExtensionManifestV3 v3 => v3.OptionsUI?.Page ?? v3.OptionsPage,
            ExtensionManifestV2 v2 => v2.OptionsUI?.Page ?? v2.GetExtraValue<string>("options_page"),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(page) ? null : $"chrome-extension://{extension.Id}/{page.TrimStart('/')}";
    }

    /// <summary>Full path of the extension's largest icon, if it declares one that exists.</summary>
    public static string? GetIconPath(Extension extension)
    {
        Icons? icons = extension.Manifest switch
        {
            ExtensionManifestV3 v3 => v3.Icons is { Count: > 0 } ? v3.Icons : v3.Action?.DefaultIcon,
            ExtensionManifestV2 v2 => v2.Icons is { Count: > 0 } ? v2.Icons : v2.BrowserAction?.DefaultIcon ?? v2.PageAction?.DefaultIcon,
            _ => null,
        };

        var relative = icons?
            .OrderByDescending(kv => int.TryParse(kv.Key, out var size) ? size : 0)
            .Select(kv => kv.Value)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (relative is null) return null;

        var path = Path.Combine(extension.FolderPath, relative.TrimStart('/', '\\'));
        return File.Exists(path) ? path : null;
    }

    /// <summary>The extension's icon as an element, or a puzzle piece if it has none.</summary>
    public static UIElement CreateIconElement(Extension extension, double size)
    {
        if (GetIconPath(extension) is { } path)
        {
            try
            {
                return new Image
                {
                    Source = path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                        ? new SvgImageSource(new Uri(path))
                        : new BitmapImage(new Uri(path)),
                    Width = size,
                    Height = size,
                    Stretch = Stretch.Uniform,
                };
            }
            catch (Exception e)
            {
                FoxyLogger.AddError(e);
            }
        }
        return new MaterialIcon { Kind = MaterialIconKind.Puzzle, Width = size, Height = size };
    }

    /// <summary>Shows a toast in the window that owns <paramref name="webview"/> (or the instance's current window).</summary>
    private static void Notify(Instance instance, WebView2? webview, string title, string? message, bool isError = false)
    {
        try
        {
            var window = instance.Windows.FirstOrDefault(w => webview is not null && w.Content?.XamlRoot == webview.XamlRoot)
                         ?? instance.CurrentWindow;
            window?.ShowToast(title, message, isError ? MaterialIconKind.AlertCircle : MaterialIconKind.Puzzle, isError);
        }
        catch (Exception e)
        {
            FoxyLogger.AddError(e);
        }
    }
    
	private static async Task AddExtension(this Instance instance, WebView2 webview, CoreWebView2DownloadStartingEventArgs e)
	{
		if (ExtractExtensionIdFromUrl(e.DownloadOperation.Uri) is not { } id) return;
		await instance.AddExtension(webview, id, ExtensionSource.Chrome);
	}

    public enum ExtensionSource
    {
        Chrome,
        Microsoft,
    }
    
	private static async Task AddExtension(this Instance instance, WebView2 webview, string id, ExtensionSource source)
    {
        Debug.WriteLine($"Adding extension {id}");
        await ProcessCrxFile(instance, webview, id, await DownloadCrx(id, source), isUpdate: false);
    }

    private static async Task<byte[]> DownloadCrx(string id, ExtensionSource source)
    {
        byte[] crxBytes = null;
        Exception lastException = null;

        if (source == ExtensionSource.Chrome)
        {
            //var url = $"https://clients2.google.com/service/update2/crx?response=redirect&prodversion=99&x=id%3D{id}%26uc";
            
            // from:
            // https://github.com/Rob--W/crxviewer/blob/master/src/cws_pattern.js
            
            const string product_channel = "unknown";
            const string product_version = "9999.0.9999.0";
            const string product_id = "chromiumcrx";
            const string platform = "win";
            var arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "x86-32";
            
            var url = "https://clients2.google.com/service/update2/crx?response=redirect";
            url += $"&os={platform}";
            url += $"&arch={arch}";
            url += $"&os_arch={arch}";
            url += $"&nacl_arch={arch}";
            url += $"&prod={product_id}";
            url += $"&prodchannel={product_channel}";
            url += $"&prodversion={product_version}";
            url += $"&acceptformat=crx2,crx3";
            url += $"&x=id%3D{id}";
            url += $"%26uc";
            try
            {
                crxBytes = await DownloadFromUrl(url);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }
        else if (source == ExtensionSource.Microsoft)
        {
            var url = $"https://edge.microsoft.com/extensionwebstorebase/v1/crx?response=redirect&x=id%3D{id}%26installsource%3Dondemand%26uc;";
            try
            {
                crxBytes = await DownloadFromUrl(url);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }
        
        if (crxBytes == null || crxBytes.Length <= 250)
        {
            throw new Exception($"Failed to download extension {id}. All methods failed. Last error: {lastException?.Message}");
        }
        
        return crxBytes;
    }

    private static async Task<byte[]> DownloadFromUrl(string url, int i = 10)
    {
        if (i <= 0) throw new Exception("Failed to download extension. Maximum number of redirects reached.");
        
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        
        // Use a more recent Chrome user agent
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        
        // Add additional headers that Chrome typically sends
        http.DefaultRequestHeaders.Add("Accept", "*/*");
        http.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        http.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
        http.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
        http.DefaultRequestHeaders.Add("Pragma", "no-cache");
        http.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
        http.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
        http.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-site");
        
        var response = await http.GetAsync(url);
    
        if (response.StatusCode is HttpStatusCode.Found)
        {
            var location = response.Headers.Location?.ToString();
            if (!string.IsNullOrEmpty(location))
            {
                return await DownloadFromUrl(location, --i);
            }
        }
        
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>Strips the CRX2/CRX3 header and unzips the extension into <paramref name="outFolder"/>.</summary>
    private static void ExtractCrx(byte[] crxBytes, string outFolder)
    {
        using var crxStream = new MemoryStream(crxBytes);
        using var br = new BinaryReader(crxStream, Encoding.UTF8, leaveOpen: true);
        
        var signature = new string(br.ReadChars(4));
        if (signature != "Cr24")
        {
            throw new InvalidDataException("Invalid CRX header signature. Expected 'Cr24'.");
        }
        
        var version = br.ReadUInt32();
        
        switch (version)
        {
            case 2:
            {
                // CRX2 format:
                var publicKeySize = br.ReadUInt32();
                var signatureSize = br.ReadUInt32();
                
                br.ReadBytes((int)publicKeySize);  // Skip public key
                br.ReadBytes((int)signatureSize);  // Skip signature
                break;
            }
            case 3:
            {
                // CRX3 format:
                var headerSize = br.ReadUInt32();
                br.ReadBytes((int)headerSize);  // Skip entire header
                break;
            }
            default:
                throw new InvalidDataException($"Unsupported CRX version: {version}");
        }
        
        var zipDataSize = crxStream.Length - crxStream.Position;
        var zipData = new byte[zipDataSize];
        crxStream.ReadExactly(zipData, 0, (int)zipDataSize);
        
        using var zipStream = new MemoryStream(zipData);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        
        if (!Directory.Exists(outFolder))
        {
            Directory.CreateDirectory(outFolder);
        }
        
        // Extract with error handling
        try
        {
            archive.ExtractToDirectory(outFolder, overwriteFiles: true);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to extract extension: {ex.Message}", ex);
        }
    }

    private static async Task ProcessCrxFile(Instance instance, WebView2 webview, string id, byte[] crxBytes, bool isUpdate)
    {
        var outFolder = Path.Combine(FoxyFileManager.BuildFolderPath(FoxyFileManager.FolderType.Extension, instance.Name), id);
        
        if (!_extensions.TryGetValue(instance.Name, out var extensions))
        {
            throw new Exception($"Extensions for instance name '{instance.Name}' not found");
        }

        // unload the installed copy (if any) before its files are replaced
        var currentExtensions = await webview.CoreWebView2.Profile.GetBrowserExtensionsAsync();
        currentExtensions = currentExtensions.Where(e => !_whitelist.Contains(e.Name)).ToList();

        var oldExtension = extensions.FirstOrDefault(e => string.Equals(e.FolderPath, outFolder, StringComparison.OrdinalIgnoreCase));
        if (oldExtension is not null)
        {
            if (currentExtensions.FirstOrDefault(e => e.Id == oldExtension.Id) is { } loadedOld)
                await loadedOld.RemoveAsync();
            extensions.Remove(oldExtension);
        }

        // start from an empty folder so files dropped by the new version do not linger
        FoxyFileManager.DeleteFolder(outFolder);
        ExtractCrx(crxBytes, outFolder);
        
        var manifestPath = Path.Combine(outFolder, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new Exception($"No manifest.json found in extracted extension {id}! It will not be loaded.");
        }
        
        var extension1 = await GetFolderExtension(outFolder);
        if (extension1 is null)
        {
            throw new Exception($"Failed to load extension from {outFolder}");
        }
        
        // an older copy loaded under a different folder (matched by name) is replaced too
        var webviewEx = currentExtensions.FirstOrDefault(e => e.Id != oldExtension?.Id && IsNamesEqual(e.Name, extension1.Manifest));
        if (webviewEx != null)
        {
            await webviewEx.RemoveAsync();
            extensions.RemoveAll(e => e.Id == webviewEx.Id);
        }
        
        // Add the new extension
        try
        {
            var browserExtension = await webview.CoreWebView2.Profile.AddBrowserExtensionAsync(outFolder);
            
            var added = new Extension
            {
                FolderPath = extension1.FolderPath,
                Manifest = extension1.Manifest,
                WebviewName = browserExtension.Name,
                Id = browserExtension.Id,
                IsEnabled = true,
            };

            // an update keeps the extension off if the user had turned it off
            if (IsDisabled(instance, added))
            {
                await browserExtension.EnableAsync(false);
                added.IsEnabled = false;
            }

            extensions.Add(added);
            
            Debug.WriteLine($"Successfully added extension {id} with WebView ID {browserExtension.Id}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to add extension {id} to WebView: {ex.Message}", ex);
        }
        
        ExtensionsModified?.Invoke(instance.Name);

        var name = extension1.Manifest.GetLocalizedName() ?? extension1.Manifest.Name ?? id;
        Notify(instance, webview,
            isUpdate ? "Extension updated" : "Extension installed",
            isUpdate ? $"{name} is now version {extension1.Manifest.Version}." : $"{name} was added. Manage it in Settings > Extensions.");
    }

	private static string? ExtractExtensionIdFromUrl(string url)
	{
		try
		{
			if (string.IsNullOrEmpty(url)) return null;
			var uri = new Uri(url);

			var q = HttpUtility.ParseQueryString(uri.Query);
			var x = q["x"];
			if (!string.IsNullOrEmpty(x))
			{
				var decoded = HttpUtility.UrlDecode(x);
				var parts = decoded.Split(['&'], StringSplitOptions.RemoveEmptyEntries);
				foreach (var p in parts)
				{
					if (p.StartsWith("id=", StringComparison.OrdinalIgnoreCase))
						return p[3..];
				}
			}

			var idq = q["id"];
			if (!string.IsNullOrEmpty(idq)) return idq;

			var segs = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
			if (segs.Length >= 3)
			{
				var candidate = segs.Last();
				if (System.Text.RegularExpressions.Regex.IsMatch(candidate, @"^[a-p0-9]{32}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
					return candidate;
			}

			var m = System.Text.RegularExpressions.Regex.Match(url, @"([a-p0-9]{32})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
			if (m.Success) return m.Groups[1].Value;
		}
		catch { }

		return null;
	}
	
	public static bool IsNamesEqual(string name, ExtensionManifestBase manifest, string language = "en")
	{
		var localizedName = manifest.GetLocalizedName(language);
		var localizedShortName = manifest.GetLocalizedShortName(language);
    
		if (manifest is ExtensionManifestV2 v2)
		{
			var localizedTitle = manifest.GetLocalizedValue(v2.BrowserAction?.DefaultTitle, language);
			return name == localizedTitle || name == localizedName || name == localizedShortName;
		}
		else if (manifest is ExtensionManifestV3 v3)
		{
			var localizedTitle = manifest.GetLocalizedValue(v3.Action?.DefaultTitle, language);
			return name == localizedTitle || name == localizedName || name == localizedShortName;
		}
		else
			return false;
	}


	private static async IAsyncEnumerable<Extension> GetFolderExtensions(string extensionFolder)
	{
		var folders = await FoxyFileManager.GetChildrenOfFolderAsync(extensionFolder, FoxyFileManager.ItemType.Folder);

		foreach (var item in folders.items??[])
		{
			var manifestFile = Directory.GetFiles(item.path, "manifest.json", SearchOption.TopDirectoryOnly)
				.FirstOrDefault();

			if (manifestFile is null || await FoxyFileManager.ReadFromFileAsync(manifestFile) is not 
				    { code: FoxyFileManager.ReturnCode.Success, content: not null } result) continue;
			
			var manifest = ExtensionManifestParser.Parse(result.content, item.path);
			yield return new Extension { FolderPath = item.path, Manifest = manifest};
		}
	}

	public static async Task<Extension?> GetFolderExtension(string extensionFolder)
	{
		var manifestFile = Directory.GetFiles(extensionFolder, "manifest.json", SearchOption.TopDirectoryOnly)
			.FirstOrDefault();
		
		if (manifestFile is null || await FoxyFileManager.ReadFromFileAsync(manifestFile) is not 
			    { code: FoxyFileManager.ReturnCode.Success, content: not null } result) return null;
		
		var manifest = ExtensionManifestParser.Parse(result.content, extensionFolder);
		return new Extension { FolderPath = extensionFolder, Manifest = manifest};
	}

	public static List<Extension> GetSavedExtensions(this Instance instance)
	{
		return _extensions.TryGetValue(instance.Name, out var extensions) ? extensions : [];
	}

    private const string MicrosoftStoreScript = //TODO a few lines under this in config of the JS:
        """
        (function(){
          if (window.__wv2_ext_helper_installed) return;
          window.__wv2_ext_helper_installed = true;
        
          // ===== CONFIG =====
          const DEFAULT_COLOR = 'rgb(0,116,204)'; // Microsoft blue
          const ALLOWED_HOST_PATTERNS = [/(\.|^)microsoftedge\.microsoft\.com$/i, /(\.|^)microsoft\.com$/i];
          // ====================
        
          let currentColor = DEFAULT_COLOR;
        
          function postButtonClick(msg){
            try {
              if (window.chrome?.webview?.postMessage) {
                chrome.webview.postMessage(JSON.stringify(msg));
              }
            } catch(e){
              console.warn('WebView2 postMessage failed:', e);
            }
          }
        
          function isAllowedHost(){
            try {
              const host = location.hostname || '';
              return ALLOWED_HOST_PATTERNS.some(re => re.test(host));
            } catch(e){ 
              return false; 
            }
          }
        
          function styleButton(btn){
            if (!btn) return;
            try{
              Object.assign(btn.style, {
                backgroundImage: 'none',
                backgroundColor: currentColor,
                borderColor: currentColor,
                color: '#ffffff',
                opacity: '1',
                pointerEvents: 'auto',
                cursor: 'pointer',
                borderRadius: btn.style.borderRadius || '4px'
              });
            } catch(e){
              console.warn('Button styling failed:', e);
            }
          }
        
          function removeIncompatibleNotices(){
            let removed = 0;
            try{
              // Remove elements with incompatible class
              document.querySelectorAll('.incompatible').forEach(el => {
                try{ 
                  el.remove(); 
                  removed++; 
                } catch(e){}
              });
        
              // Remove aria-live incompatible messages
              document.querySelectorAll('[aria-live]').forEach(el => {
                try {
                  if (el?.textContent?.includes('incompatible with your browser')){
                    el.remove(); 
                    removed++;
                  }
                } catch(e){}
              });
        
              // Remove other incompatible text
              const textElements = document.querySelectorAll('p, div, span');
              textElements.forEach(el => {
                try {
                  if (el?.textContent?.toLowerCase().includes('incompatible with your browser')){
                    el.remove(); 
                    removed++;
                  }
                } catch(e){}
              });
            } catch(e){
              console.warn('Remove incompatible notices failed:', e);
            }
            return removed;
          }
        
          function findInstallButtons(){
            const results = [];
            try {
              // Find buttons with install ID pattern
              const installButtons = document.querySelectorAll('button[id*="install"], button[id*="Install"]');
              installButtons.forEach(b => results.push(b));
        
              // Find "Get" buttons
              const buttons = document.querySelectorAll('button');
              buttons.forEach(b => {
                try {
                  const text = b?.textContent?.trim().toLowerCase();
                  if ((text === 'get' || text === 'install') && !results.includes(b)) {
                    results.push(b);
                  }
                } catch(e){}
              });
        
              // Find add-to-browser buttons
              const addButtons = document.querySelectorAll('button[aria-label*="Add"], button[title*="Add"]');
              addButtons.forEach(b => {
                if (!results.includes(b)) results.push(b);
              });
        
            } catch(e){
              console.warn('Find install buttons failed:', e);
            }
            return results;
          }
        
          function enableButtons(){
            const buttons = findInstallButtons();
            let changed = 0;
        
            buttons.forEach((btn, idx) => {
              if (!btn) return;
              
              try{
                // Enable the button
                btn.removeAttribute('disabled');
                btn.disabled = false;
                btn.removeAttribute('aria-disabled');
                
                // Remove disabled classes
                const disabledClasses = ['disabled', 'is-disabled', 'btn--disabled', 'fui-Button--disabled'];
                disabledClasses.forEach(cls => btn.classList.remove(cls));
                
                // Style the button
                styleButton(btn);
                
                // Add click handler once
                if (!btn.__wv2_click_hooked) {
                  btn.__wv2_click_hooked = true;
                  btn.addEventListener('click', function(ev){
                    postButtonClick({ 
                      type: 'installButtonClicked', 
                      href: location.href, 
                      buttonId: btn.id || `button-${idx}`,
                      buttonText: btn.textContent?.trim() || 'Unknown'
                    });
                  }, { capture: true, passive: true });
                }
                
                changed++;
              } catch(e){
                console.warn('Button enable failed:', e);
              }
            });
        
            return { count: buttons.length, changed };
          }
        
          function processPage(){
            if (!isAllowedHost()) {
              return { href: location.href, ignoredHost: true };
            }
        
            try {
              removeIncompatibleNotices();
              const buttonResult = enableButtons();
              
              return { 
                href: location.href, 
                buttonsFound: buttonResult.count, 
                buttonsChanged: buttonResult.changed
              };
            } catch(e) {
              console.error('Process page failed:', e);
              return { href: location.href, error: e.message };
            }
          }
        
          function startWatcher(){
            if (!isAllowedHost()) return;
            
            let attempts = 0;
            const maxAttempts = 30;
            const observer = new MutationObserver(() => {
              attempts++;
              try {
                const result = processPage();
                if (result.buttonsFound > 0 || attempts >= maxAttempts) {
                  observer.disconnect();
                }
              } catch(e) {
                console.warn('Watcher iteration failed:', e);
              }
            });
        
            try {
              const target = document.documentElement || document.body || document;
              observer.observe(target, { 
                childList: true, 
                subtree: true, 
                attributes: true 
              });
              
              // Initial run
              processPage();
              
              // Timeout fallback
              setTimeout(() => {
                try {
                  observer.disconnect();
                } catch(e){}
              }, 10000);
              
            } catch(e) {
              console.error('Watcher setup failed:', e);
              processPage(); // Fallback to single run
            }
          }
        
          // Navigation handling for SPAs
          function setupNavigationHandler() {
            const originalPushState = history.pushState;
            const originalReplaceState = history.replaceState;
            
            history.pushState = function() {
              const result = originalPushState.apply(this, arguments);
              setTimeout(() => startWatcher(), 100);
              return result;
            };
            
            history.replaceState = function() {
              const result = originalReplaceState.apply(this, arguments);
              setTimeout(() => startWatcher(), 100);
              return result;
            };
            
            window.addEventListener('popstate', () => {
              setTimeout(() => startWatcher(), 100);
            });
          }
        
          // Message handler for host commands
          function setupMessageHandler() {
            try {
              if (window.chrome?.webview?.addEventListener) {
                chrome.webview.addEventListener('message', function(e){
                  try {
                    const data = typeof e.data === 'string' ? JSON.parse(e.data) : e.data;
                    if (!data) return;
        
                    switch(data.cmd) {
                      case 'setColor':
                        if (typeof data.color === 'string') {
                          currentColor = data.color.startsWith('rgb') ? data.color : `rgb(${data.color})`;
                          findInstallButtons().forEach(styleButton);
                        }
                        break;
                        
                      case 'runNow':
                        processPage();
                        break;
                    }
                  } catch(e) {
                    console.warn('Message handler failed:', e);
                  }
                });
              }
            } catch(e) {
              console.warn('Message handler setup failed:', e);
            }
          }
        
          // Initialize
          function init() {
            setupNavigationHandler();
            setupMessageHandler();
            
            // Export helper functions
            window.__wv2_ext_helper = { 
              runNow: processPage, 
              setColor: (color) => {
                currentColor = color;
                findInstallButtons().forEach(styleButton);
              }
            };
        
            // Start processing
            if (document.readyState === 'complete' || document.readyState === 'interactive') {
              startWatcher();
            } else {
              window.addEventListener('DOMContentLoaded', startWatcher, { once: true });
              window.addEventListener('load', startWatcher, { once: true });
            }
          }
        
          init();
        })();
        """;
}
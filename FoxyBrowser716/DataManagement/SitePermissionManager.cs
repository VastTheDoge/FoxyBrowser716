using FoxyBrowser716.DataObjects.Complex;
using FoxyBrowser716.DataObjects.Settings;
using Microsoft.Web.WebView2.Core;

namespace FoxyBrowser716.DataManagement;

/// <summary>
/// Remembered per-site permission decisions for one instance (SitePermissions.json), plus the static
/// per-kind text/icons the prompt and settings UI share.
///
/// Resolution order for a request: remembered decision for the origin, then the per-kind default in
/// <see cref="BrowserSettings"/>, and only if that is <see cref="PermissionDefault.Ask"/> is the user prompted.
/// WebView2's own persistence is bypassed (<c>SavesInProfile = false</c>) so this list stays the single source of truth.
/// </summary>
public sealed class SitePermissionManager
{
	private readonly FoxyAutoSaverLockedList<SitePermission> _store;

	public event Action? Changed;

	internal IFoxyAutoSaverItem SaverItem => _store;

	public SitePermissionManager(string instanceName)
	{
		_store = new FoxyAutoSaverLockedList<SitePermission>("SitePermissions.json", FoxyFileManager.FolderType.Data, instanceName);
	}

	/// <summary>scheme://host[:port] for a page URI, which is what permissions are keyed on.</summary>
	public static string GetOrigin(string uri) =>
		Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && !string.IsNullOrEmpty(parsed.Host)
			? parsed.GetLeftPart(UriPartial.Authority)
			: uri;

	/// <summary>Host part of an origin for display ("maps.google.com").</summary>
	public static string GetDisplayHost(string originOrUri) =>
		Uri.TryCreate(originOrUri, UriKind.Absolute, out var parsed) && !string.IsNullOrEmpty(parsed.Host)
			? parsed.Host
			: originOrUri;

	public PermissionDecision? GetDecision(string origin, CoreWebView2PermissionKind kind) =>
		_store.Read(items => items.FirstOrDefault(p => p.Kind == kind && string.Equals(p.Origin, origin, StringComparison.OrdinalIgnoreCase))?.Decision);

	public void SetDecision(string origin, CoreWebView2PermissionKind kind, PermissionDecision decision)
	{
		_store.Mutate(items =>
		{
			items.RemoveAll(p => p.Kind == kind && string.Equals(p.Origin, origin, StringComparison.OrdinalIgnoreCase));
			items.Add(new SitePermission { Origin = origin, Kind = kind, Decision = decision, DecidedAt = DateTime.Now });
		});
		Changed?.Invoke();
	}

	public void Forget(string origin, CoreWebView2PermissionKind kind)
	{
		_store.Mutate(items => items.RemoveAll(p => p.Kind == kind && string.Equals(p.Origin, origin, StringComparison.OrdinalIgnoreCase)));
		Changed?.Invoke();
	}

	public void ForgetSite(string origin)
	{
		_store.Mutate(items => items.RemoveAll(p => string.Equals(p.Origin, origin, StringComparison.OrdinalIgnoreCase)));
		Changed?.Invoke();
	}

	public void ForgetAll()
	{
		_store.Mutate(items => items.Clear());
		Changed?.Invoke();
	}

	public List<SitePermission> GetAll() => _store.Read(items => items
		.OrderBy(p => GetDisplayHost(p.Origin), StringComparer.OrdinalIgnoreCase)
		.ThenBy(p => p.Kind)
		.ToList());

	/// <summary>
	/// What to do with a request right now: a remembered decision, else the setting's default.
	/// Null means ask the user.
	/// </summary>
	public PermissionDecision? Resolve(string origin, CoreWebView2PermissionKind kind, BrowserSettings settings)
	{
		if (GetDecision(origin, kind) is { } remembered)
			return remembered;

		return GetDefault(settings, kind) switch
		{
			PermissionDefault.Allow => PermissionDecision.Allow,
			PermissionDefault.Block => PermissionDecision.Block,
			_ => null,
		};
	}

	public static PermissionDefault GetDefault(BrowserSettings settings, CoreWebView2PermissionKind kind) => kind switch
	{
		CoreWebView2PermissionKind.Geolocation => settings.LocationPermission,
		CoreWebView2PermissionKind.Camera => settings.CameraPermission,
		CoreWebView2PermissionKind.Microphone => settings.MicrophonePermission,
		CoreWebView2PermissionKind.Notifications => settings.NotificationsPermission,
		CoreWebView2PermissionKind.ClipboardRead => settings.ClipboardReadPermission,
		CoreWebView2PermissionKind.OtherSensors => settings.SensorsPermission,
		CoreWebView2PermissionKind.MultipleAutomaticDownloads => settings.AutomaticDownloadsPermission,
		CoreWebView2PermissionKind.FileReadWrite => settings.FileEditingPermission,
		CoreWebView2PermissionKind.Autoplay => settings.AutoplayPermission,
		CoreWebView2PermissionKind.LocalFonts => settings.LocalFontsPermission,
		CoreWebView2PermissionKind.MidiSystemExclusiveMessages => settings.MidiPermission,
		CoreWebView2PermissionKind.WindowManagement => settings.WindowManagementPermission,
		_ => PermissionDefault.Ask,
	};

	public static string GetDisplayName(CoreWebView2PermissionKind kind) => kind switch
	{
		CoreWebView2PermissionKind.Geolocation => "Location",
		CoreWebView2PermissionKind.Camera => "Camera",
		CoreWebView2PermissionKind.Microphone => "Microphone",
		CoreWebView2PermissionKind.Notifications => "Notifications",
		CoreWebView2PermissionKind.ClipboardRead => "Clipboard",
		CoreWebView2PermissionKind.OtherSensors => "Motion sensors",
		CoreWebView2PermissionKind.MultipleAutomaticDownloads => "Automatic downloads",
		CoreWebView2PermissionKind.FileReadWrite => "File editing",
		CoreWebView2PermissionKind.Autoplay => "Autoplay",
		CoreWebView2PermissionKind.LocalFonts => "Local fonts",
		CoreWebView2PermissionKind.MidiSystemExclusiveMessages => "MIDI devices",
		CoreWebView2PermissionKind.WindowManagement => "Window management",
		_ => "Unknown permission",
	};

	/// <summary>Completes "{site} wants to ..." in the prompt.</summary>
	public static string GetRequestText(CoreWebView2PermissionKind kind) => kind switch
	{
		CoreWebView2PermissionKind.Geolocation => "Know your location",
		CoreWebView2PermissionKind.Camera => "Use your camera",
		CoreWebView2PermissionKind.Microphone => "Use your microphone",
		CoreWebView2PermissionKind.Notifications => "Show notifications",
		CoreWebView2PermissionKind.ClipboardRead => "See text and images copied to the clipboard",
		CoreWebView2PermissionKind.OtherSensors => "Use your motion and light sensors",
		CoreWebView2PermissionKind.MultipleAutomaticDownloads => "Download multiple files automatically",
		CoreWebView2PermissionKind.FileReadWrite => "Edit files and folders on your device",
		CoreWebView2PermissionKind.Autoplay => "Play media with sound automatically",
		CoreWebView2PermissionKind.LocalFonts => "Use the fonts installed on your computer",
		CoreWebView2PermissionKind.MidiSystemExclusiveMessages => "Get full control of your MIDI devices",
		CoreWebView2PermissionKind.WindowManagement => "Manage windows on all your displays",
		_ => "Use a permission FoxyBrowser does not recognize",
	};

	public static MaterialIconKind GetIcon(CoreWebView2PermissionKind kind) => kind switch
	{
		CoreWebView2PermissionKind.Geolocation => MaterialIconKind.MapMarker,
		CoreWebView2PermissionKind.Camera => MaterialIconKind.Camera,
		CoreWebView2PermissionKind.Microphone => MaterialIconKind.Microphone,
		CoreWebView2PermissionKind.Notifications => MaterialIconKind.Bell,
		CoreWebView2PermissionKind.ClipboardRead => MaterialIconKind.ContentPaste,
		CoreWebView2PermissionKind.OtherSensors => MaterialIconKind.Motion,
		CoreWebView2PermissionKind.MultipleAutomaticDownloads => MaterialIconKind.DownloadMultiple,
		CoreWebView2PermissionKind.FileReadWrite => MaterialIconKind.FileEdit,
		CoreWebView2PermissionKind.Autoplay => MaterialIconKind.PlayCircle,
		CoreWebView2PermissionKind.LocalFonts => MaterialIconKind.FormatFont,
		CoreWebView2PermissionKind.MidiSystemExclusiveMessages => MaterialIconKind.Piano,
		CoreWebView2PermissionKind.WindowManagement => MaterialIconKind.MonitorMultiple,
		_ => MaterialIconKind.HelpCircle,
	};
}

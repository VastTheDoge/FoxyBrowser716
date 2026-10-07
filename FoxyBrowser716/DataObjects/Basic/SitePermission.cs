using Microsoft.Web.WebView2.Core;

namespace FoxyBrowser716.DataObjects.Basic;

/// <summary>What a site gets for a permission kind when nothing was remembered for it.</summary>
public enum PermissionDefault
{
	Ask,
	Allow,
	Block,
}

public enum PermissionDecision
{
	Allow,
	Block,
}

/// <summary>A permission decision the user chose to remember for one origin.</summary>
public class SitePermission
{
	/// <summary>scheme://host[:port]</summary>
	public string Origin { get; set; } = string.Empty;
	public CoreWebView2PermissionKind Kind { get; set; }
	public PermissionDecision Decision { get; set; }
	public DateTime DecidedAt { get; set; }
}

using Microsoft.Web.WebView2.Core;

namespace FoxyBrowser716.DataObjects.Basic;

// Claude Doc
/// <summary>What a site gets for a permission kind when nothing was remembered for it.</summary>
public enum PermissionDefault
{
	Ask,
	Allow,
	Block,
}

// Claude Doc
/// <summary>What happens after you allow or block a permission prompt.</summary>
public enum PermissionRememberMode
{
	// Claude Doc
	/// <summary>Follow up with "Remember this?".</summary>
	Ask,
	Always,
	Never,
}

public enum PermissionDecision
{
	Allow,
	Block,
}

// Claude Doc
/// <summary>A permission decision the user chose to remember for one origin.</summary>
public class SitePermission
{
	// Claude Doc
	/// <summary>scheme://host[:port]</summary>
	public string Origin { get; set; } = string.Empty;
	public CoreWebView2PermissionKind Kind { get; set; }
	public PermissionDecision Decision { get; set; }
	public DateTime DecidedAt { get; set; }
}

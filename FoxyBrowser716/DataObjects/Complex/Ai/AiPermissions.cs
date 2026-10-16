using System.ComponentModel;
using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.DataObjects.Complex.Ai;

// Claude Doc
/// <summary>What the assistant may do without asking. Default in the panel's settings; the mode button changes it per chat.</summary>
public enum AiPermissionMode
{
	// Claude Doc
	/// <summary>Reads run on their own; every action asks.</summary>
	[Description("Read-only")] ReadOnly,
	// Claude Doc
	/// <summary>Everything asks, reads included.</summary>
	Ask,
	// Claude Doc
	/// <summary>Nothing asks, scripts included.</summary>
	Brave,
	// Claude Doc
	/// <summary>Per tool, from <see cref="BrowserSettings.AiToolPermissions"/>.</summary>
	Custom,
}

public enum AiToolPermission { Allow, Ask, Block }

public static class AiPermissions
{
	public static AiToolPermission Resolve(AiPermissionMode mode, AiTool tool, BrowserSettings settings) => mode switch
	{
		AiPermissionMode.Ask => AiToolPermission.Ask,
		AiPermissionMode.Brave => AiToolPermission.Allow,
		AiPermissionMode.Custom => settings.AiToolPermissions.TryGetValue(tool.Name, out var choice) ? choice : Default(tool),
		_ => Default(tool),
	};

	// Claude Doc
	/// <summary>Read-only mode's choice, also the starting point for Custom.</summary>
	public static AiToolPermission Default(AiTool tool) => tool.Kind == AiToolKind.Read ? AiToolPermission.Allow : AiToolPermission.Ask;

	public static string Name(AiPermissionMode mode) => mode == AiPermissionMode.ReadOnly ? "Read-only" : mode.ToString();

	public static MaterialIconKind Icon(AiPermissionMode mode) => mode switch
	{
		AiPermissionMode.Ask => MaterialIconKind.HandBackRight,
		AiPermissionMode.Brave => MaterialIconKind.LightningBolt,
		AiPermissionMode.Custom => MaterialIconKind.Tune,
		_ => MaterialIconKind.Eye,
	};

	public static string Explain(AiPermissionMode mode) => mode switch
	{
		AiPermissionMode.Ask => "Asks before everything, reading included.",
		AiPermissionMode.Brave => "Acts without asking, running scripts included.",
		AiPermissionMode.Custom => "Per tool, as set in the assistant's settings (gear icon).",
		_ => "Reads tabs and pages on its own; asks before every action.",
	};
}

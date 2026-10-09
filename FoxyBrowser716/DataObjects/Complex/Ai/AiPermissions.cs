using System.ComponentModel;
using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.DataObjects.Complex.Ai;

/// <summary>What the assistant may do without asking. Default in settings; the panel's lightbulb changes it per chat.</summary>
public enum AiPermissionMode
{
	/// <summary>Reads run on their own; every action asks.</summary>
	[Description("Read-only")] ReadOnly,
	/// <summary>Everything asks, reads included.</summary>
	Ask,
	/// <summary>Nothing asks, except running scripts.</summary>
	Brave,
	/// <summary>Per tool, from <see cref="BrowserSettings.AiToolPermissions"/>.</summary>
	Custom,
}

/// <summary>One tool's choice in Custom mode.</summary>
public enum AiToolPermission { Allow, Ask, Block }

public static class AiPermissions
{
	public static AiToolPermission Resolve(AiPermissionMode mode, AiTool tool, BrowserSettings settings) => mode switch
	{
		AiPermissionMode.Ask => AiToolPermission.Ask,
		// scripts can do anything the user can on that site (read mail, send forms), so they keep asking
		AiPermissionMode.Brave => tool.Kind == AiToolKind.Script ? AiToolPermission.Ask : AiToolPermission.Allow,
		AiPermissionMode.Custom => settings.AiToolPermissions.TryGetValue(tool.Name, out var choice) ? choice : Default(tool),
		_ => Default(tool),
	};

	/// <summary>Read-only mode's choice, also the starting point for Custom.</summary>
	public static AiToolPermission Default(AiTool tool) => tool.Kind == AiToolKind.Read ? AiToolPermission.Allow : AiToolPermission.Ask;

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
		AiPermissionMode.Brave => "Acts without asking. Running scripts still asks.",
		AiPermissionMode.Custom => "Per tool, as set in Settings → AI Assistant.",
		_ => "Reads tabs and pages on its own; asks before every action.",
	};
}

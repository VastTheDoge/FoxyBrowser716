namespace FoxyBrowser716.DataObjects.Basic;

// Claude Doc
/// <summary>
/// A theme as written to disk and to exported .foxytheme files: a name and the ten colors as "#RRGGBB" strings
/// ("#AARRGGBB" when not opaque). The same shape is read back, so a hand-edited file only needs valid hex.
/// </summary>
public sealed record SavedTheme
{
	public string Name { get; init; } = "";
	public string PrimaryBackground { get; init; } = "#000000";
	public string SecondaryBackground { get; init; } = "#000000";
	public string PrimaryForeground { get; init; } = "#FFFFFF";
	public string SecondaryForeground { get; init; } = "#FFFFFF";
	public string PrimaryAccent { get; init; } = "#000000";
	public string SecondaryAccent { get; init; } = "#000000";
	public string PrimaryHighlight { get; init; } = "#000000";
	public string SecondaryHighlight { get; init; } = "#000000";
	public string Yes { get; init; } = "#00E100";
	public string No { get; init; } = "#E10000";

	public static SavedTheme From(string name, Theme theme) => new()
	{
		Name = name,
		PrimaryBackground = Hex(theme.PrimaryBackgroundColor),
		SecondaryBackground = Hex(theme.SecondaryBackgroundColor),
		PrimaryForeground = Hex(theme.PrimaryForegroundColor),
		SecondaryForeground = Hex(theme.SecondaryForegroundColor),
		PrimaryAccent = Hex(theme.PrimaryAccentColor),
		SecondaryAccent = Hex(theme.SecondaryAccentColor),
		PrimaryHighlight = Hex(theme.PrimaryHighlightColor),
		SecondaryHighlight = Hex(theme.SecondaryHighlightColor),
		Yes = Hex(theme.YesColor),
		No = Hex(theme.NoColor),
	};

	// Claude Doc
	/// <summary>Null when any color isn't valid hex, so a broken file is skipped rather than shown as black.</summary>
	public Theme? ToTheme()
	{
		if (!TryParseHex(PrimaryBackground, out var primaryBackground) || !TryParseHex(SecondaryBackground, out var secondaryBackground)
		    || !TryParseHex(PrimaryForeground, out var primaryForeground) || !TryParseHex(SecondaryForeground, out var secondaryForeground)
		    || !TryParseHex(PrimaryAccent, out var primaryAccent) || !TryParseHex(SecondaryAccent, out var secondaryAccent)
		    || !TryParseHex(PrimaryHighlight, out var primaryHighlight) || !TryParseHex(SecondaryHighlight, out var secondaryHighlight)
		    || !TryParseHex(Yes, out var yes) || !TryParseHex(No, out var no))
			return null;

		return new Theme
		{
			PrimaryBackgroundColor = primaryBackground,
			SecondaryBackgroundColor = secondaryBackground,
			PrimaryForegroundColor = primaryForeground,
			SecondaryForegroundColor = secondaryForeground,
			PrimaryAccentColor = primaryAccent,
			SecondaryAccentColor = secondaryAccent,
			PrimaryHighlightColor = primaryHighlight,
			SecondaryHighlightColor = secondaryHighlight,
			YesColor = yes,
			NoColor = no,
		};
	}

	public static string Hex(Color color) => color.A == 255
		? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
		: $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

	// Claude Doc
	/// <summary>Accepts RGB, RRGGBB and AARRGGBB, with or without a leading '#'.</summary>
	public static bool TryParseHex(string? text, out Color color)
	{
		color = default;
		var hex = text?.Trim().TrimStart('#') ?? "";
		if (hex.Length == 3) hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
		if (hex.Length is not (6 or 8) || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
			return false;
		if (hex.Length == 6) value |= 0xFF000000;
		color = Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
		return true;
	}
}

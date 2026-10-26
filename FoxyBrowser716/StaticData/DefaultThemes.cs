using System.Drawing;
using FoxyBrowser716.DataObjects.Basic;

namespace FoxyBrowser716.StaticData;

public static class DefaultThemes
{

	public static readonly Theme DarkMode = new()
	{
		PrimaryBackgroundColor = Color.FromArgb(255, 54, 56, 64),
		SecondaryBackgroundColor = Color.FromArgb(255, 43, 44, 51),
		PrimaryForegroundColor = Color.FromArgb(255, 255, 255, 255),
		SecondaryForegroundColor = Color.FromArgb(255, 200, 200, 200),
		PrimaryAccentColor = Color.FromArgb(255, 102, 104, 112),
		SecondaryAccentColor = Color.FromArgb(255, 66, 69, 79),
		PrimaryHighlightColor = Color.FromArgb(255, 255, 150, 0),
		SecondaryHighlightColor = Color.FromArgb(255, 200, 100, 0),
		YesColor = Color.FromArgb(255, 0, 225, 0),
		NoColor = Color.FromArgb(255, 225, 0, 0),
	};
	public static readonly Theme LightMode = new()
	{
		PrimaryBackgroundColor = Color.FromArgb(255, 255, 255, 255),
		SecondaryBackgroundColor = Color.FromArgb(255, 225, 225, 225),
		PrimaryForegroundColor = Color.FromArgb(255, 0,0,0),
		SecondaryForegroundColor = Color.FromArgb(255, 75,75,75),
		PrimaryAccentColor = Color.FromArgb(255, 175, 175, 175),
		SecondaryAccentColor = Color.FromArgb(255, 125, 125, 125),
		PrimaryHighlightColor = Color.FromArgb(255, 255, 150, 0),
		SecondaryHighlightColor = Color.FromArgb(255, 255, 175, 0),
		YesColor = Color.FromArgb(255, 0, 225, 0),
		NoColor = Color.FromArgb(255, 225, 0, 0),
	};
	public static readonly Theme DaybreakFoxy = new()
	{
		PrimaryBackgroundColor = Color.FromArgb(255, 255, 181, 92),
		SecondaryBackgroundColor = Color.FromArgb(255, 255, 170, 75),
		PrimaryForegroundColor = Color.FromArgb(255, 255, 255, 255),
		SecondaryForegroundColor = Color.FromArgb(255, 200, 200, 200),
		PrimaryAccentColor = Color.FromArgb(255, 149, 69, 255),
		SecondaryAccentColor = Color.FromArgb(255, 114, 43, 209),
		PrimaryHighlightColor = Color.FromArgb(255, 149, 69, 255),
		SecondaryHighlightColor = Color.FromArgb(255, 114, 43, 209),
		YesColor = Color.FromArgb(255, 0, 225, 0),
		NoColor = Color.FromArgb(255, 225, 0, 0),
	};
	public static readonly Theme NightfallFoxy = new()
	{
		PrimaryBackgroundColor = Color.FromArgb(255, 30, 0, 73),
		SecondaryBackgroundColor = Color.FromArgb(255, 0, 6, 38),
		PrimaryForegroundColor = Color.FromArgb(255, 255, 255, 255),
		SecondaryForegroundColor = Color.FromArgb(255, 200, 200, 200),
		PrimaryAccentColor = Color.FromArgb(255, 149, 69, 255),
		SecondaryAccentColor = Color.FromArgb(255, 114, 43, 209),
		PrimaryHighlightColor = Color.FromArgb(255, 94, 35, 173),
		SecondaryHighlightColor = Color.FromArgb(255, 76, 22, 148),
		YesColor = Color.FromArgb(255, 0, 225, 0),
		NoColor = Color.FromArgb(255, 225, 0, 0),
	};
	public static readonly Theme VastSea = new()
	{
		PrimaryBackgroundColor = Color.FromArgb(255, 40, 60, 80),
		SecondaryBackgroundColor = Color.FromArgb(255, 20, 40, 60),
		PrimaryForegroundColor = Color.FromArgb(255, 255, 255, 255),
		SecondaryForegroundColor = Color.FromArgb(255, 200, 200, 200),
		PrimaryAccentColor = Color.FromArgb(255, 105, 135, 165),
		SecondaryAccentColor = Color.FromArgb(255, 85, 115, 145),
		PrimaryHighlightColor = Color.FromArgb(255, 11, 190, 255),
		SecondaryHighlightColor = Color.FromArgb(255, 94, 168, 248),
		YesColor = Color.FromArgb(255, 0, 225, 0),
		NoColor = Color.FromArgb(255, 225, 0, 0),
	};
	public static readonly Theme VastSky = new()
	{
		PrimaryBackgroundColor = Color.FromArgb(255, 205, 230, 255),
		SecondaryBackgroundColor = Color.FromArgb(255, 185, 210, 235),
		PrimaryForegroundColor = Color.FromArgb(255, 0,0,0),
		SecondaryForegroundColor = Color.FromArgb(255, 50,50,50),
		PrimaryAccentColor = Color.FromArgb(255, 105, 135, 165),
		SecondaryAccentColor = Color.FromArgb(255, 85, 115, 145),
		PrimaryHighlightColor = Color.FromArgb(255, 11, 190, 255),
		SecondaryHighlightColor = Color.FromArgb(255, 94, 168, 248),
		YesColor = Color.FromArgb(255, 0, 225, 0),
		NoColor = Color.FromArgb(255, 225, 0, 0),
	};

	// Claude Doc
	/// <summary>In gallery order. The names are also the ids stored in settings, so renaming one orphans saved choices.</summary>
	public static readonly (string name, Theme theme)[] BuiltIn =
	[
		("Dark Mode", DarkMode),
		("Light Mode", LightMode),
		("Daybreak Foxy", DaybreakFoxy),
		("Nightfall Foxy", NightfallFoxy),
		("Vast Seas", VastSea),
		("Vast Skies", VastSky),
	];
}

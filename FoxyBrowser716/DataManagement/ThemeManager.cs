using FoxyBrowser716.ErrorHandeler;

namespace FoxyBrowser716.DataManagement;

// Claude Doc
/// <summary>
/// Every theme an instance can use: the built-ins plus custom ones kept as one .foxytheme file each in the data
/// folder's Themes folder, so they are shared by all instances and move with the data folder. Use on the UI
/// thread; <see cref="Changed"/> is raised on the thread that saved, deleted or imported.
/// </summary>
public static class ThemeManager
{
	public const string FileExtension = ".foxytheme";

	// Claude Doc
	/// <summary>A built-in's id is its name; a custom theme's id is the file name, which never changes when it is renamed.</summary>
	public sealed record Entry(string Id, string Name, Theme Theme, bool IsBuiltIn);

	public static IReadOnlyList<Entry> BuiltIn { get; } =
		DefaultThemes.BuiltIn.Select(p => new Entry(p.name, p.name, p.theme, true)).ToList();

	private static readonly Dictionary<string, Entry> CustomById = new(StringComparer.OrdinalIgnoreCase);
	private static bool _loaded;

	public static event Action? Changed;

	public static string Folder => Path.Combine(FoxyFileManager.BuildFolderPath(FoxyFileManager.FolderType.Data), "Themes");

	public static IReadOnlyList<Entry> All
	{
		get
		{
			EnsureLoaded();
			return [..BuiltIn, ..CustomById.Values.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)];
		}
	}

	public static Entry? Find(string? id)
	{
		if (string.IsNullOrWhiteSpace(id)) return null;
		EnsureLoaded();
		return BuiltIn.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
		       ?? CustomById.GetValueOrDefault(id);
	}

	// Claude Doc
	/// <summary>The theme for a settings value; a missing custom theme (deleted, or not synced to this PC) gives Dark Mode.</summary>
	public static Theme Resolve(string? id) => Find(id)?.Theme ?? DefaultThemes.DarkMode;

	// Claude Doc
	/// <summary>Writes a custom theme; <paramref name="id"/> null (or a built-in's id) makes a new one. Returns null when the file could not be written.</summary>
	public static Entry? Save(string? id, string name, Theme theme)
	{
		EnsureLoaded();
		if (id is null || !CustomById.ContainsKey(id)) id = Guid.NewGuid().ToString("N");
		name = UniqueName(name, id);

		var entry = new Entry(id, name, theme, false);
		if (!Write(entry)) return null;
		CustomById[id] = entry;
		Changed?.Invoke();
		return entry;
	}

	public static bool Delete(string id)
	{
		EnsureLoaded();
		if (!CustomById.Remove(id)) return false;
		try
		{
			File.Delete(PathFor(id));
		}
		catch (Exception e)
		{
			FoxyLogger.AddError(e);
		}
		Changed?.Invoke();
		return true;
	}

	// Claude Doc
	/// <summary>Copies a .foxytheme file in as a new custom theme; null when it can't be read or isn't a theme.</summary>
	public static Entry? Import(string path)
	{
		var (code, saved) = FoxyFileManager.ReadFromFile<SavedTheme>(path);
		if (code != FoxyFileManager.ReturnCode.Success || saved?.ToTheme() is not { } theme) return null;
		var name = string.IsNullOrWhiteSpace(saved.Name) ? Path.GetFileNameWithoutExtension(path) : saved.Name.Trim();
		return Save(null, name, theme);
	}

	public static bool Export(Entry entry, string path) =>
		FoxyFileManager.SaveToFile(path, SavedTheme.From(entry.Name, entry.Theme)) == FoxyFileManager.ReturnCode.Success;

	// Claude Doc
	/// <summary>"Name", else "Name (2)", "Name (3)"… so two themes never show the same name; <paramref name="exceptId"/> may keep its own.</summary>
	public static string UniqueName(string name, string? exceptId = null)
	{
		EnsureLoaded();
		name = string.IsNullOrWhiteSpace(name) ? "Custom theme" : name.Trim();
		bool Taken(string candidate) => All.Any(e => e.Id != exceptId && string.Equals(e.Name, candidate, StringComparison.CurrentCultureIgnoreCase));
		if (!Taken(name)) return name;
		for (var i = 2; ; i++)
			if (!Taken($"{name} ({i})")) return $"{name} ({i})";
	}

	private static string PathFor(string id) => Path.Combine(Folder, id + FileExtension);

	private static bool Write(Entry entry)
	{
		try
		{
			Directory.CreateDirectory(Folder);
		}
		catch (Exception e)
		{
			FoxyLogger.AddError(e);
			return false;
		}
		return FoxyFileManager.SaveToFile(PathFor(entry.Id), SavedTheme.From(entry.Name, entry.Theme)) == FoxyFileManager.ReturnCode.Success;
	}

	private static void EnsureLoaded()
	{
		if (_loaded) return;
		_loaded = true;
		if (!Directory.Exists(Folder)) return;

		foreach (var file in Directory.EnumerateFiles(Folder, "*" + FileExtension))
		{
			var (code, saved) = FoxyFileManager.ReadFromFile<SavedTheme>(file);
			if (code != FoxyFileManager.ReturnCode.Success || saved?.ToTheme() is not { } theme)
			{
				FoxyLogger.AddWarning($"Skipped theme file {file}: not a valid theme.", details: null);
				continue;
			}
			var id = Path.GetFileNameWithoutExtension(file);
			CustomById[id] = new Entry(id, string.IsNullOrWhiteSpace(saved.Name) ? id : saved.Name.Trim(), theme, false);
		}
	}
}

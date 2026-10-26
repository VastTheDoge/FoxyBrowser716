namespace FoxyBrowser716.DataObjects.Settings;

// Claude Doc
/// <summary>
/// Ranks settings for the settings page search box. Every word typed has to hit somewhere (name, description or
/// category); a hit on the name counts most. Words match whole, as a prefix, inside a word, or with a typo or two,
/// so "downlod folder" still finds "Download folder".
/// </summary>
public static class SettingsSearch
{
	public sealed record Entry<T>(T Item, string Name, string Description, string Category);

	private const double NameWeight = 3;
	private const double CategoryWeight = 1.5;
	private const double DescriptionWeight = 1;

	public static List<T> Rank<T>(string query, IEnumerable<Entry<T>> entries, int take)
	{
		var tokens = Words(query);
		if (tokens.Length == 0) return [];
		var normalizedQuery = string.Join(' ', tokens);

		var scored = new List<(T item, double score, int nameLength)>();
		foreach (var entry in entries)
		{
			var name = Words(entry.Name);
			var description = Words(entry.Description);
			var category = Words(entry.Category);

			var total = 0.0;
			var allHit = true;
			foreach (var token in tokens)
			{
				var best = Math.Max(Best(token, name) * NameWeight, Math.Max(Best(token, category) * CategoryWeight, Best(token, description) * DescriptionWeight));
				if (best <= 0)
				{
					allHit = false;
					break;
				}
				total += best;
			}
			if (!allHit) continue;

			//C the whole phrase inside the name beats the same words scattered over the description
			if (string.Join(' ', name).Contains(normalizedQuery, StringComparison.Ordinal)) total += 2;
			scored.Add((entry.Item, total, entry.Name.Length));
		}

		return scored
			.OrderByDescending(s => s.score)
			.ThenBy(s => s.nameLength)
			.Take(take)
			.Select(s => s.item)
			.ToList();
	}

	private static double Best(string token, string[] words)
	{
		var best = 0.0;
		foreach (var word in words)
		{
			best = Math.Max(best, WordScore(token, word));
			if (best >= 1) break;
		}
		return best;
	}

	private static double WordScore(string token, string word)
	{
		if (word == token) return 1;
		if (word.StartsWith(token, StringComparison.Ordinal)) return 0.9;
		if (token.Length >= 3 && word.Contains(token, StringComparison.Ordinal)) return 0.7;
		if (token.Length >= 4)
		{
			var allowed = token.Length >= 7 ? 2 : 1;
			if (Distance(token, word, allowed) <= allowed) return 0.6;
			//C a typo in a prefix: "permisi" should still find "permissions"
			if (word.Length > token.Length && Distance(token, word[..token.Length], allowed) <= allowed) return 0.5;
		}
		return 0;
	}

	private static string[] Words(string text) =>
		text.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	private static readonly char[] Separators = [' ', '\t', '\n', '\r', ',', '.', ';', ':', '(', ')', '"', '\'', '/', '\\', '-', '_', '+', '&', '?', '!'];

	// Claude Doc
	/// <summary>Damerau-Levenshtein distance, stopping early (returns <paramref name="limit"/> + 1) once no row can get under the limit.</summary>
	private static int Distance(string a, string b, int limit)
	{
		if (Math.Abs(a.Length - b.Length) > limit) return limit + 1;
		var previous2 = new int[b.Length + 1];
		var previous = new int[b.Length + 1];
		var current = new int[b.Length + 1];
		for (var j = 0; j <= b.Length; j++) previous[j] = j;

		for (var i = 1; i <= a.Length; i++)
		{
			current[0] = i;
			var rowMin = current[0];
			for (var j = 1; j <= b.Length; j++)
			{
				var cost = a[i - 1] == b[j - 1] ? 0 : 1;
				current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
				if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
					current[j] = Math.Min(current[j], previous2[j - 2] + 1);
				rowMin = Math.Min(rowMin, current[j]);
			}
			if (rowMin > limit) return limit + 1;
			(previous2, previous, current) = (previous, current, previous2);
		}
		return previous[b.Length];
	}
}

using System.Threading;
using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.DataObjects.Complex.Ai;

// Claude Doc
/// <summary>
/// The models each provider offers, fetched from its API (Claude's Models API, an OpenAI-compatible server's
/// <c>/models</c>) and kept for the session. Refetched when the key or server changes, or on request.
/// </summary>
public static class AiModels
{
	private static readonly Dictionary<AiProviderKind, (string Source, List<(string Id, string Name)> Models)> Cache = [];

	// Claude Doc
	/// <summary>The provider's models as (id, display name). Throws when the provider isn't set up or can't be reached.</summary>
	public static async Task<List<(string Id, string Name)>> GetAsync(AiProviderKind provider, BrowserSettings settings, bool refresh = false, CancellationToken cancellationToken = default)
	{
		var source = provider == AiProviderKind.Claude
			? AiCredentials.Get(AiProviderKind.Claude) ?? ""
			: $"{settings.OpenAiBaseUrl}|{AiCredentials.Get(AiProviderKind.OpenAiCompatible)}";

		if (!refresh && Cache.TryGetValue(provider, out var cached) && cached.Source == source)
			return cached.Models;

		var models = provider == AiProviderKind.Claude
			? await ClaudeConversation.ListModelsAsync(cancellationToken)
			: await OpenAiCompatibleConversation.ListModelsAsync(settings, cancellationToken);
		Cache[provider] = (source, models);
		return models;
	}

	// Claude Doc
	/// <summary>The display name for <paramref name="model"/> if its list was fetched, else its id.</summary>
	public static string DisplayName(AiModelChoice model)
	{
		if (!Cache.TryGetValue(model.Provider, out var cached)) return model.Model;
		var name = cached.Models.FirstOrDefault(m => m.Id == model.Model).Name;
		return string.IsNullOrEmpty(name) ? model.Model : name;
	}
}

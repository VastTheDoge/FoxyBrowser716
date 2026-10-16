using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Threading;
using FoxyBrowser716.ErrorHandeler;
using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.DataObjects.Complex.Ai;

// Claude Doc
/// <summary>Which service a model runs on. Each has its own settings in the panel's settings view.</summary>
public enum AiProviderKind
{
	[Description("Claude")] Claude,
	[Description("OpenAI-compatible")] OpenAiCompatible,
}

public sealed record AiModelChoice(AiProviderKind Provider, string Model)
{
	public static AiModelChoice Default => new(AiProviderKind.Claude, ClaudeConversation.DefaultModel);

	public static AiModelChoice DefaultQuick => new(AiProviderKind.Claude, ClaudeConversation.QuickModel);
}

// Claude Doc
/// <summary>How hard the model thinks before answering (Claude's <c>output_config.effort</c>).</summary>
public enum AiEffort
{
	Low,
	Medium,
	High,
	[Description("Extra high")] XHigh,
	Max,
}

public enum AiRole { User, Assistant }

// Claude Doc
/// <summary>What a tool does, which decides whether it needs the user's approval (see <see cref="AiPermissions"/>).</summary>
public enum AiToolKind
{
	// Claude Doc
	/// <summary>Only looks: tab list, page text, scrolling.</summary>
	Read,
	// Claude Doc
	/// <summary>Changes something: clicks, typing, navigating, opening or closing tabs.</summary>
	Action,
	// Claude Doc
	/// <summary>Runs arbitrary JavaScript in a page.</summary>
	Script,
}

// Claude Doc
/// <summary>What a tool hands back to the model: text, plus an image for tools that look at the screen.</summary>
public sealed record AiToolOutput(string Text, byte[]? Image = null, string ImageMediaType = "image/jpeg")
{
	public static implicit operator AiToolOutput(string text) => new(text);
}

// Claude Doc
/// <summary>A tool the assistant can call. The schema is plain JSON Schema so every provider can send it as-is.</summary>
/// <param name="Title">Short human name, for settings and approval cards.</param>
/// <param name="Execute">Runs on the UI thread. Throw <see cref="AiToolException"/> to hand the model an error result.</param>
public sealed record AiTool(
	string Name,
	string Title,
	AiToolKind Kind,
	string Description,
	IReadOnlyDictionary<string, JsonElement> Properties,
	IReadOnlyList<string> Required,
	Func<IReadOnlyDictionary<string, JsonElement>, Task<AiToolOutput>> Execute)
{
	// Claude Doc
	/// <summary>Returns an image, so it's only offered to providers that can send images back to the model (Claude).</summary>
	public bool ReturnsImage { get; init; }

	// Claude Doc
	/// <summary>
	/// Plain-language summary of one call for the approval card ("Click button "Buy" on example.com"). May throw
	/// <see cref="AiToolException"/> (e.g. a stale element ref) to fail the call before the user is asked.
	/// Null: the card shows the title and raw arguments.
	/// </summary>
	public Func<IReadOnlyDictionary<string, JsonElement>, Task<string>>? Describe { get; init; }
}

public sealed record AiApprovalRequest(string Title, string Description);

// Claude Doc
/// <summary>Token counts for one or more responses. <see cref="CostUsd"/> is null when the model's price is unknown.</summary>
public sealed record AiUsage(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, decimal? CostUsd)
{
	public static readonly AiUsage Zero = new(0, 0, 0, 0, 0m);

	public long TotalInputTokens => InputTokens + CacheReadTokens + CacheWriteTokens;

	public AiUsage Add(AiUsage other) => new(
		InputTokens + other.InputTokens,
		OutputTokens + other.OutputTokens,
		CacheReadTokens + other.CacheReadTokens,
		CacheWriteTokens + other.CacheWriteTokens,
		CostUsd is { } a && other.CostUsd is { } b ? a + b : null);
}

// Claude Doc
/// <summary>What a turn reports while it runs. All callbacks arrive on the caller's (UI) thread.</summary>
public sealed class AiTurnCallbacks
{
	public Action<string>? Text { get; init; }
	public Action<string>? ToolCall { get; init; }
	// Claude Doc
	/// <summary>Once per model response; a turn with tool calls has several.</summary>
	public Action<AiUsage>? Usage { get; init; }
	// Claude Doc
	/// <summary>Asks the user to allow a tool call; resolves to their answer. Null: anything needing approval is denied.</summary>
	public Func<AiApprovalRequest, Task<bool>>? Approve { get; init; }
}

// Claude Doc
/// <summary>The provider isn't set up yet (missing key or model); the message tells the user what to fill in.</summary>
public sealed class AiNotConfiguredException(string message) : Exception(message);

// Claude Doc
/// <summary>Thrown by a tool to return an error result to the model instead of failing the turn.</summary>
public sealed class AiToolException(string message) : Exception(message);

// Claude Doc
/// <summary>
/// One chat's history in the provider's own wire format, so nothing is lost converting between providers
/// (Claude's thinking blocks must be sent back unchanged). History is append-only: a turn that fails or is
/// refused is rolled back whole, so the next request always extends what was sent before.
/// The model and effort come with each turn (the chat can switch models within its provider), and connection
/// settings (key, URL) are read fresh every turn, so fixing a key takes effect without starting a new chat.
/// </summary>
public abstract class AiConversation
{
	// Claude Doc
	/// <summary>Model calls per user message. Page tasks (read, click, read again, type...) take several.</summary>
	protected const int MaxToolRounds = 25;

	protected readonly Func<BrowserSettings> Settings;
	protected readonly string SystemPrompt;
	protected readonly IReadOnlyList<AiTool> Tools;

	protected AiConversation(Func<BrowserSettings> settings, string systemPrompt, IReadOnlyList<AiTool> tools)
	{
		Settings = settings;
		SystemPrompt = systemPrompt;
		Tools = tools;
	}

	public abstract AiProviderKind Kind { get; }

	//C the provider-format history, for saving a chat; images are left out so files stay small
	public abstract JsonArray ExportHistory();
	public abstract void ImportHistory(JsonArray history);

	//C tool results can hold screenshots; a saved chat keeps a note in their place
	protected static void StripImages(JsonNode? node)
	{
		switch (node)
		{
			case JsonArray array:
				for (var i = 0; i < array.Count; i++)
				{
					if (array[i] is JsonObject o && o["type"]?.GetValueKind() == JsonValueKind.String && o["type"]!.GetValue<string>() == "image")
						array[i] = new JsonObject { ["type"] = "text", ["text"] = "(screenshot not kept in the saved chat)" };
					else
						StripImages(array[i]);
				}
				break;
			case JsonObject obj:
				foreach (var (_, value) in obj.ToList())
					StripImages(value);
				break;
		}
	}

	// Claude Doc
	/// <summary>Sends <paramref name="userMessage"/>, runs any tool calls, and streams the reply through <paramref name="callbacks"/>.</summary>
	/// <param name="effort">Ignored by providers without an effort setting.</param>
	public abstract Task SendAsync(string userMessage, string model, AiEffort effort, AiTurnCallbacks callbacks, CancellationToken cancellationToken = default);

	public static AiConversation Create(AiProviderKind kind, Func<BrowserSettings> settings, string systemPrompt, IReadOnlyList<AiTool> tools) => kind switch
	{
		AiProviderKind.Claude => new ClaudeConversation(settings, systemPrompt, tools),
		AiProviderKind.OpenAiCompatible => new OpenAiCompatibleConversation(settings, systemPrompt, tools.Where(t => !t.ReturnsImage).ToList()),
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
	};

	// Claude Doc
	/// <summary>
	/// One question, one short answer, no tools or history: for small jobs like naming a tab group. Returns the
	/// reply text and what it cost.
	/// </summary>
	public static Task<(string Text, AiUsage Usage)> CompleteAsync(AiModelChoice model, BrowserSettings settings, string system, string prompt, CancellationToken cancellationToken = default) =>
		model.Provider switch
		{
			AiProviderKind.Claude => ClaudeConversation.CompleteAsync(model.Model, system, prompt, cancellationToken),
			AiProviderKind.OpenAiCompatible => OpenAiCompatibleConversation.CompleteAsync(settings, model.Model, system, prompt, cancellationToken),
			_ => throw new ArgumentOutOfRangeException(nameof(model), model.Provider, null),
		};

	// Claude Doc
	/// <summary>Runs a tool by name; failures become an error result for the model rather than an exception.</summary>
	protected async Task<(AiToolOutput Output, bool IsError)> RunToolAsync(string name, IReadOnlyDictionary<string, JsonElement> input)
	{
		if (Tools.FirstOrDefault(t => t.Name == name) is not { } tool)
			return ($"Unknown tool '{name}'.", true);

		try
		{
			return (await tool.Execute(input), false);
		}
		catch (AiToolException e)
		{
			return (e.Message, true);
		}
		catch (Exception e)
		{
			FoxyLogger.AddError(e);
			return ($"The tool failed: {e.Message}", true);
		}
	}
}

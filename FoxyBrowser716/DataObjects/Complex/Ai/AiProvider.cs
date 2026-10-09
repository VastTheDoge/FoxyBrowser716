using System.ComponentModel;
using System.Threading;
using FoxyBrowser716.ErrorHandeler;
using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.DataObjects.Complex.Ai;

/// <summary>Which service the assistant panel talks to. Each has its own settings in the AI Assistant category.</summary>
public enum AiProviderKind
{
	[Description("Claude")] Claude,
	[Description("OpenAI-compatible")] OpenAiCompatible,
}

/// <summary>How hard the model thinks before answering (Claude's <c>output_config.effort</c>).</summary>
public enum AiEffort { Low, Medium, High, Max }

public enum AiRole { User, Assistant }

/// <summary>What a tool does, which decides whether it needs the user's approval (see <see cref="AiPermissions"/>).</summary>
public enum AiToolKind
{
	/// <summary>Only looks: tab list, page text, scrolling.</summary>
	Read,
	/// <summary>Changes something: clicks, typing, navigating, opening or closing tabs.</summary>
	Action,
	/// <summary>Runs arbitrary JavaScript in a page.</summary>
	Script,
}

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
	Func<IReadOnlyDictionary<string, JsonElement>, Task<string>> Execute)
{
	/// <summary>
	/// Plain-language summary of one call for the approval card ("Click button "Buy" on example.com"). May throw
	/// <see cref="AiToolException"/> (e.g. a stale element ref) to fail the call before the user is asked.
	/// Null: the card shows the title and raw arguments.
	/// </summary>
	public Func<IReadOnlyDictionary<string, JsonElement>, Task<string>>? Describe { get; init; }
}

/// <summary>A tool call waiting for the user's Allow / Deny.</summary>
public sealed record AiApprovalRequest(string Title, string Description);

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

/// <summary>What a turn reports while it runs. All callbacks arrive on the caller's (UI) thread.</summary>
public sealed class AiTurnCallbacks
{
	public Action<string>? Text { get; init; }
	public Action<string>? ToolCall { get; init; }
	/// <summary>Once per model response; a turn with tool calls has several.</summary>
	public Action<AiUsage>? Usage { get; init; }
	/// <summary>Asks the user to allow a tool call; resolves to their answer. Null: anything needing approval is denied.</summary>
	public Func<AiApprovalRequest, Task<bool>>? Approve { get; init; }
}

/// <summary>The provider isn't set up yet (missing key or model); the message tells the user what to fill in.</summary>
public sealed class AiNotConfiguredException(string message) : Exception(message);

/// <summary>Thrown by a tool to return an error result to the model instead of failing the turn.</summary>
public sealed class AiToolException(string message) : Exception(message);

/// <summary>
/// One chat's history in the provider's own wire format, so nothing is lost converting between providers
/// (Claude's thinking blocks must be sent back unchanged). History is append-only: a turn that fails or is
/// refused is rolled back whole, so the next request always extends what was sent before.
/// Connection settings (key, model, effort, URL) are read fresh on every turn, so fixing a key in
/// settings takes effect without starting a new chat.
/// </summary>
public abstract class AiConversation
{
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

	/// <summary>The model the next turn will use, for display.</summary>
	public abstract string CurrentModel { get; }

	/// <summary>Sends <paramref name="userMessage"/>, runs any tool calls, and streams the reply through <paramref name="callbacks"/>.</summary>
	public abstract Task SendAsync(string userMessage, AiTurnCallbacks callbacks, CancellationToken cancellationToken = default);

	public static AiConversation Create(AiProviderKind kind, Func<BrowserSettings> settings, string systemPrompt, IReadOnlyList<AiTool> tools) => kind switch
	{
		AiProviderKind.Claude => new ClaudeConversation(settings, systemPrompt, tools),
		AiProviderKind.OpenAiCompatible => new OpenAiCompatibleConversation(settings, systemPrompt, tools),
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
	};

	/// <summary>Runs a tool by name; failures become an error result for the model rather than an exception.</summary>
	protected async Task<(string Content, bool IsError)> RunToolAsync(string name, IReadOnlyDictionary<string, JsonElement> input)
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

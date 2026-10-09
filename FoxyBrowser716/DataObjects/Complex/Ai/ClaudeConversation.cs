using System.Threading;
using Anthropic;
using Anthropic.Helpers;
using Anthropic.Models.Beta.Messages;
using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.DataObjects.Complex.Ai;

/// <summary>
/// Claude through the official Anthropic SDK (beta endpoint, for refusal fallbacks): streamed replies, adaptive
/// thinking, a client-side tool loop, and automatic prompt caching of the growing conversation.
/// </summary>
internal sealed class ClaudeConversation(Func<BrowserSettings> settings, string systemPrompt, IReadOnlyList<AiTool> tools)
	: AiConversation(settings, systemPrompt, tools)
{
	public const string DefaultModel = "claude-opus-5-5";

	/// <summary>
	/// Models that take <c>fallbacks: "default"</c>: when a safety classifier declines a request, the API re-serves it
	/// on a suitable fallback model in the same call instead of stopping.
	/// </summary>
	private static readonly HashSet<string> FallbackModels = ["claude-fable-5-1", "claude-opus-5-5", "claude-opus-5", "claude-sonnet-5-5"];
	private const string FallbackBeta = "server-side-fallback-2026-07-01";

	/// <summary>
	/// List prices in $ per million tokens (input, output, cache read), as of 2026-10. Cache writes bill at 1.25x
	/// input. Only used for the cost estimate shown in the chat panel.
	/// </summary>
	private static readonly Dictionary<string, (decimal Input, decimal Output, decimal CacheRead)> Prices = new()
	{
		["claude-fable-5-1"] = (10m, 50m, 0.25m),
		["claude-fable-5"] = (10m, 50m, 1m),
		["claude-opus-5-5"] = (4m, 20m, 0.20m),
		["claude-opus-5"] = (5m, 25m, 0.50m),
		["claude-opus-4-8"] = (5m, 25m, 0.50m),
		["claude-opus-4-7"] = (5m, 25m, 0.50m),
		["claude-opus-4-6"] = (5m, 25m, 0.50m),
		["claude-sonnet-5-5"] = (2m, 10m, 0.20m),
		["claude-sonnet-5"] = (2m, 10m, 0.20m),
		["claude-sonnet-4-6"] = (3m, 15m, 0.30m),
		["claude-haiku-5-5"] = (0.10m, 0.50m, 0.01m),
		["claude-haiku-4-5"] = (1m, 5m, 0.10m),
	};

	private readonly List<BetaMessageParam> _messages = [];
	private AnthropicClient? _client;
	private string? _clientKey;

	public override AiProviderKind Kind => AiProviderKind.Claude;

	public override string CurrentModel => Settings().ClaudeModel is { } model && !string.IsNullOrWhiteSpace(model) ? model.Trim() : DefaultModel;

	public override async Task SendAsync(string userMessage, AiTurnCallbacks callbacks, CancellationToken cancellationToken = default)
	{
		var client = GetClient();
		var model = CurrentModel;
		var effort = Settings().ClaudeEffort;

		var checkpoint = _messages.Count;
		_messages.Add(new BetaMessageParam { Role = Role.User, Content = userMessage });
		try
		{
			for (var round = 0; round < MaxToolRounds; round++)
			{
				var aggregator = new BetaMessageContentAggregator();
				var stream = client.Beta.Messages.CreateStreaming(BuildParams(model, effort), cancellationToken);
				await foreach (var e in aggregator.CollectAsync(stream).WithCancellation(cancellationToken))
				{
					if (e.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text))
						callbacks.Text?.Invoke(text.Text);
				}

				var response = aggregator.Message();
				callbacks.Usage?.Invoke(ToUsage(response));

				var stopReason = response.StopReason?.Raw();
				if (stopReason == "refusal")
				{
					// the whole fallback chain declined; any partial output is discarded with the rolled-back turn
					var category = response.StopDetails?.Category is { } c ? $" ({c})" : "";
					throw new InvalidOperationException($"Claude declined this request{category}. Try rephrasing it.");
				}

				var (content, toolUses) = ToParams(response.Content, keepToolUse: stopReason == "tool_use");
				if (content.Count > 0)
					_messages.Add(new BetaMessageParam { Role = Role.Assistant, Content = content });

				if (stopReason == "max_tokens")
					callbacks.Text?.Invoke("\n\n*(Reply cut off: it hit the length limit.)*");
				if (toolUses.Count == 0) return;

				List<BetaContentBlockParam> results = [];
				foreach (var toolUse in toolUses)
				{
					callbacks.ToolCall?.Invoke(toolUse.Name);
					var (result, isError) = await RunToolAsync(toolUse.Name, toolUse.Input);
					results.Add(new BetaToolResultBlockParam { ToolUseID = toolUse.ID, Content = result, IsError = isError });
				}
				_messages.Add(new BetaMessageParam { Role = Role.User, Content = results });
			}

			callbacks.Text?.Invoke($"\n\n*(Stopped after {MaxToolRounds} rounds of tool calls.)*");
		}
		catch
		{
			_messages.RemoveRange(checkpoint, _messages.Count - checkpoint);
			throw;
		}
	}

	private AnthropicClient GetClient()
	{
		var key = AiCredentials.Get(AiProviderKind.Claude)
			?? throw new AiNotConfiguredException("Add your Claude API key in **Settings → AI Assistant**. You can create one in the Claude Console (platform.claude.com).");

		if (_client is null || _clientKey != key)
		{
			_client = new AnthropicClient { ApiKey = key };
			_clientKey = key;
		}
		return _client;
	}

	private MessageCreateParams BuildParams(string model, AiEffort effort)
	{
		var parameters = new MessageCreateParams
		{
			Model = model,
			MaxTokens = 64000,
			System = SystemPrompt,
			Messages = _messages.ToList(),
			Tools = Tools.Select(t => (BetaToolUnion)new BetaTool
			{
				Name = t.Name,
				Description = t.Description,
				InputSchema = new() { Properties = t.Properties.ToDictionary(), Required = t.Required.ToList() },
				// tool input arrives unvalidated with this on; the tools check their own arguments
				EagerInputStreaming = true,
			}).ToList(),
			Thinking = new BetaThinkingConfigAdaptive(),
			OutputConfig = new BetaOutputConfig
			{
				Effort = effort switch
				{
					AiEffort.Low => Effort.Low,
					AiEffort.High => Effort.High,
					AiEffort.Max => Effort.Max,
					_ => Effort.Medium,
				},
			},
			// caches the whole prefix up to the latest message, so each turn only pays full price for what's new
			CacheControl = new BetaCacheControlEphemeral(),
		};

		return FallbackModels.Contains(model)
			? parameters with { Betas = [FallbackBeta], Fallbacks = new Default() }
			: parameters;
	}

	/// <summary>
	/// Converts a response into the assistant message to append (the SDK has no ToParam). Thinking blocks are kept
	/// byte-for-byte with their signatures. After a mid-output fallback, only text from before the last fallback
	/// marker may be sent back; the tools to run are the ones after it.
	/// </summary>
	private static (List<BetaContentBlockParam> Content, List<BetaToolUseBlock> ToolUses) ToParams(IReadOnlyList<BetaContentBlock> blocks, bool keepToolUse)
	{
		var boundary = -1;
		for (var i = 0; i < blocks.Count; i++)
			if (blocks[i].TryPickFallback(out _)) boundary = i;

		List<BetaContentBlockParam> content = [];
		List<BetaToolUseBlock> toolUses = [];
		for (var i = 0; i < blocks.Count; i++)
		{
			var block = blocks[i];
			if (block.TryPickText(out var text))
			{
				if (!string.IsNullOrEmpty(text.Text)) content.Add(new BetaTextBlockParam { Text = text.Text });
			}
			else if (i < boundary)
			{
				// thinking and tool calls from a model that then declined
			}
			else if (block.TryPickThinking(out var thinking))
				content.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
			else if (block.TryPickRedactedThinking(out var redacted))
				content.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
			else if (block.TryPickToolUse(out var toolUse) && keepToolUse)
			{
				content.Add(new BetaToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
				toolUses.Add(toolUse);
			}
			// fallback markers are audit-only; this chat declares no server tools
		}

		return (content, toolUses);
	}

	private static AiUsage ToUsage(BetaMessage response)
	{
		var usage = response.Usage;
		var input = usage.InputTokens;
		var output = usage.OutputTokens;
		var cacheRead = usage.CacheReadInputTokens ?? 0;
		var cacheWrite = usage.CacheCreationInputTokens ?? 0;

		decimal? cost = null;
		if (response.Model?.Raw() is { } served && Prices.TryGetValue(served, out var price))
			cost = (input * price.Input + output * price.Output + cacheRead * price.CacheRead + cacheWrite * price.Input * 1.25m) / 1_000_000m;

		return new AiUsage(input, output, cacheRead, cacheWrite, cost);
	}
}

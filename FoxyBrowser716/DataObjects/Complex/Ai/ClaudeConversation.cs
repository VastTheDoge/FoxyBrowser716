using System.Net;
using System.Text.Json.Nodes;
using System.Threading;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Helpers;
using Anthropic.Models.Beta.Messages;
using Anthropic.Models.Models;
using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.DataObjects.Complex.Ai;

// Claude Doc
/// <summary>
/// Claude through the official Anthropic SDK (beta endpoint, for refusal fallbacks): streamed replies, adaptive
/// thinking, a client-side tool loop, and automatic prompt caching of the growing conversation.
/// </summary>
internal sealed class ClaudeConversation(Func<BrowserSettings> settings, string systemPrompt, IReadOnlyList<AiTool> tools)
	: AiConversation(settings, systemPrompt, tools)
{
	public const string DefaultModel = "claude-opus-5-5";

	// Claude Doc
	/// <summary>
	/// Models that take <c>fallbacks: "default"</c>: when a safety classifier declines a request, the API re-serves it
	/// on a suitable fallback model in the same call instead of stopping.
	/// </summary>
	private static readonly HashSet<string> FallbackModels = ["claude-fable-5-1", "claude-opus-5-5", "claude-opus-5", "claude-sonnet-5-5"];
	private const string FallbackBeta = "server-side-fallback-2026-07-01";

	// Claude Doc
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

	public const string QuickModel = "claude-haiku-5-5";

	// Claude Doc
	/// <summary>
	/// Set explicitly: the SDK would otherwise take <c>ANTHROPIC_BASE_URL</c> from the environment, which a browser
	/// launched from a developer shell may inherit (pointing it at a proxy that rejects the key).
	/// </summary>
	private const string BaseUrl = "https://api.anthropic.com";

	private const string KeyMissing = "Add your Claude API key in the assistant's settings (the gear at the top of this panel). You can create one in the Claude Console (platform.claude.com).";

	private readonly List<BetaMessageParam> _messages = [];

	private static AnthropicClient? _client;
	private static string? _clientKey;

	public override AiProviderKind Kind => AiProviderKind.Claude;

	public override JsonArray ExportHistory()
	{
		var history = new JsonArray(_messages.Select(m => JsonSerializer.SerializeToNode(m.RawData)).ToArray());
		StripImages(history);
		return history;
	}

	public override void ImportHistory(JsonArray history)
	{
		_messages.Clear();
		foreach (var node in history)
			if (node.Deserialize<Dictionary<string, JsonElement>>() is { } raw)
				_messages.Add(BetaMessageParam.FromRawUnchecked(raw));
	}

	public override async Task SendAsync(string userMessage, string model, AiEffort effort, AiTurnCallbacks callbacks, CancellationToken cancellationToken = default)
	{
		var client = GetClient();
		model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

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
					//C the whole fallback chain declined; any partial output is discarded with the rolled-back turn
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
					var (output, isError) = await RunToolAsync(toolUse.Name, toolUse.Input);
					results.Add(new BetaToolResultBlockParam { ToolUseID = toolUse.ID, Content = ToResultContent(output), IsError = isError });
				}
				_messages.Add(new BetaMessageParam { Role = Role.User, Content = results });
			}

			callbacks.Text?.Invoke($"\n\n*(Stopped after {MaxToolRounds} rounds of tool calls.)*");
		}
		catch (AnthropicApiException e) when (e.StatusCode == HttpStatusCode.Unauthorized)
		{
			_messages.RemoveRange(checkpoint, _messages.Count - checkpoint);
			throw KeyRejected();
		}
		catch
		{
			_messages.RemoveRange(checkpoint, _messages.Count - checkpoint);
			throw;
		}
	}

	private static BetaToolResultBlockParamContent ToResultContent(AiToolOutput output)
	{
		if (output.Image is not { } image) return output.Text;

		List<Block> blocks = [];
		if (output.Text.Length > 0) blocks.Add(new BetaTextBlockParam { Text = output.Text });
		blocks.Add(new BetaImageBlockParam
		{
			Source = new BetaBase64ImageSource
			{
				Data = Convert.ToBase64String(image),
				MediaType = output.ImageMediaType == "image/png" ? MediaType.ImagePng : MediaType.ImageJpeg,
			},
		});
		return blocks;
	}

	// Claude Doc
	/// <summary>The client for the saved key, shared by every chat; rebuilt when the key changes.</summary>
	private static AnthropicClient GetClient()
	{
		var key = AiCredentials.Get(AiProviderKind.Claude) ?? throw new AiNotConfiguredException(KeyMissing);

		if (_client is null || _clientKey != key)
		{
			_client = new AnthropicClient { ApiKey = key, BaseUrl = BaseUrl };
			_clientKey = key;
		}
		return _client;
	}

	// Claude Doc
	/// <summary>A 401: says which key was sent (start, end and length only) so a wrong or cut-off paste is easy to spot.</summary>
	private static AiNotConfiguredException KeyRejected() =>
		new($"Claude rejected the saved API key ({AiCredentials.Mask(AiCredentials.Get(AiProviderKind.Claude))}). " +
			"Check it in the assistant's settings (the gear at the top of this panel): it should be a Console API key starting with `sk-ant-api`.");

	public static async Task<string> TestKeyAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			await GetClient().Models.List(new ModelListParams { Limit = 1 }, cancellationToken);
			return "Key works.";
		}
		catch (AiNotConfiguredException)
		{
			return "No key saved.";
		}
		catch (AnthropicApiException e) when (e.StatusCode == HttpStatusCode.Unauthorized)
		{
			return $"Rejected: {AiCredentials.Mask(AiCredentials.Get(AiProviderKind.Claude))} is not a valid API key.";
		}
		catch (Exception e)
		{
			return $"Couldn't check: {e.Message}";
		}
	}

	public static async Task<List<(string Id, string Name)>> ListModelsAsync(CancellationToken cancellationToken = default)
	{
		List<(string, string)> models = [];
		try
		{
			var page = await GetClient().Models.List(new ModelListParams { Limit = 100 }, cancellationToken);
			while (true)
			{
				models.AddRange(page.Items.Select(m => (m.ID, string.IsNullOrWhiteSpace(m.DisplayName) ? m.ID : m.DisplayName)));
				if (!page.HasNext()) break;
				page = await page.Next(cancellationToken);
			}
		}
		catch (AnthropicApiException e) when (e.StatusCode == HttpStatusCode.Unauthorized)
		{
			throw KeyRejected();
		}
		return models;
	}

	internal static async Task<(string Text, AiUsage Usage)> CompleteAsync(string model, string system, string prompt, CancellationToken cancellationToken)
	{
		BetaMessage response;
		try
		{
			response = await GetClient().Beta.Messages.Create(new MessageCreateParams
			{
				Model = model,
				MaxTokens = 2000,
				System = system,
				Messages = [new BetaMessageParam { Role = Role.User, Content = prompt }],
				Thinking = new BetaThinkingConfigAdaptive(),
				OutputConfig = new BetaOutputConfig { Effort = Effort.Low },
			}, cancellationToken);
		}
		catch (AnthropicApiException e) when (e.StatusCode == HttpStatusCode.Unauthorized)
		{
			throw KeyRejected();
		}

		if (response.StopReason?.Raw() == "refusal")
			throw new InvalidOperationException("Claude declined this request.");

		var text = new StringBuilder();
		foreach (var block in response.Content)
			if (block.TryPickText(out var t)) text.Append(t.Text);
		return (text.ToString().Trim(), ToUsage(response));
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
				//C tool input arrives unvalidated with this on; the tools check their own arguments
				EagerInputStreaming = true,
			}).ToList(),
			Thinking = new BetaThinkingConfigAdaptive(),
			OutputConfig = new BetaOutputConfig
			{
				Effort = effort switch
				{
					AiEffort.Low => Effort.Low,
					AiEffort.High => Effort.High,
					AiEffort.XHigh => Effort.Xhigh,
					AiEffort.Max => Effort.Max,
					_ => Effort.Medium,
				},
			},
			//C caches the whole prefix up to the latest message, so each turn only pays full price for what's new
			CacheControl = new BetaCacheControlEphemeral(),
		};

		return FallbackModels.Contains(model)
			? parameters with { Betas = [FallbackBeta], Fallbacks = new Default() }
			: parameters;
	}

	// Claude Doc
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
				//C thinking and tool calls from a model that then declined
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
			//C fallback markers are audit-only; this chat declares no server tools
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

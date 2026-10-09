using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading;
using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.DataObjects.Complex.Ai;

/// <summary>
/// Any server that speaks OpenAI's <c>/chat/completions</c> (OpenAI, OpenRouter, Mistral, Groq, Ollama, LM Studio...),
/// over plain HTTP + SSE so server-specific SDK quirks don't matter. Usage is reported as tokens only: prices
/// differ per server, so there's no cost estimate.
/// </summary>
internal sealed class OpenAiCompatibleConversation : AiConversation
{
	private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan }; // replies stream for as long as they take

	private readonly List<JsonObject> _messages;

	public OpenAiCompatibleConversation(Func<BrowserSettings> settings, string systemPrompt, IReadOnlyList<AiTool> tools)
		: base(settings, systemPrompt, tools)
	{
		_messages = [new JsonObject { ["role"] = "system", ["content"] = systemPrompt }];
	}

	public override AiProviderKind Kind => AiProviderKind.OpenAiCompatible;

	public override string CurrentModel => Settings().OpenAiModel?.Trim() ?? string.Empty;

	public override async Task SendAsync(string userMessage, AiTurnCallbacks callbacks, CancellationToken cancellationToken = default)
	{
		var baseUrl = Settings().OpenAiBaseUrl?.Trim().TrimEnd('/');
		if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(baseUrl + "/chat/completions", UriKind.Absolute, out var endpoint))
			throw new AiNotConfiguredException("Set the OpenAI-compatible base URL in **Settings → AI Assistant**, for example `https://api.openai.com/v1`.");

		var model = CurrentModel;
		if (model.Length == 0)
			throw new AiNotConfiguredException("Set the OpenAI-compatible model in **Settings → AI Assistant**.");

		var key = AiCredentials.Get(AiProviderKind.OpenAiCompatible); // optional: local servers usually don't need one

		var checkpoint = _messages.Count;
		_messages.Add(new JsonObject { ["role"] = "user", ["content"] = userMessage });
		try
		{
			for (var round = 0; round < MaxToolRounds; round++)
			{
				var (message, toolCalls) = await StreamCompletionAsync(endpoint, key, model, callbacks, cancellationToken);
				_messages.Add(message);
				if (toolCalls.Count == 0) return;

				foreach (var call in toolCalls)
				{
					callbacks.ToolCall?.Invoke(call.Name);
					var (result, isError) = ParseArguments(call.Arguments) is { } input
						? await RunToolAsync(call.Name, input)
						: ("The arguments were not valid JSON.", true);
					_messages.Add(new JsonObject
					{
						["role"] = "tool",
						["tool_call_id"] = call.Id,
						["content"] = isError ? $"Error: {result}" : result,
					});
				}
			}

			callbacks.Text?.Invoke($"\n\n*(Stopped after {MaxToolRounds} rounds of tool calls.)*");
		}
		catch
		{
			_messages.RemoveRange(checkpoint, _messages.Count - checkpoint);
			throw;
		}
	}

	private sealed class ToolCallBuilder
	{
		public string Id = string.Empty;
		public string Name = string.Empty;
		public readonly StringBuilder Arguments = new();
	}

	private async Task<(JsonObject Message, List<(string Id, string Name, string Arguments)> ToolCalls)> StreamCompletionAsync(
		Uri endpoint, string? key, string model, AiTurnCallbacks callbacks, CancellationToken cancellationToken)
	{
		var body = new JsonObject
		{
			["model"] = model,
			["messages"] = new JsonArray(_messages.Select(m => m.DeepClone()).ToArray()),
			["stream"] = true,
			["stream_options"] = new JsonObject { ["include_usage"] = true },
		};
		if (Tools.Count > 0) body["tools"] = BuildTools();

		using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
		request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
		if (!string.IsNullOrEmpty(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

		using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			var error = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {ErrorMessage(error)}");
		}

		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var reader = new StreamReader(stream);

		var text = new StringBuilder();
		var calls = new SortedDictionary<int, ToolCallBuilder>();
		string? finishReason = null;

		while (await reader.ReadLineAsync(cancellationToken) is { } line)
		{
			if (!line.StartsWith("data:")) continue;
			var data = line[5..].Trim();
			if (data == "[DONE]") break;

			var chunk = JsonNode.Parse(data);
			if (chunk?["error"] is { } streamError)
				throw new HttpRequestException(streamError["message"]?.ToString() ?? streamError.ToJsonString());

			if (chunk?["usage"] is JsonObject usage)
			{
				var prompt = usage["prompt_tokens"]?.GetValue<long>() ?? 0;
				var cached = usage["prompt_tokens_details"]?["cached_tokens"]?.GetValue<long>() ?? 0;
				var completion = usage["completion_tokens"]?.GetValue<long>() ?? 0;
				callbacks.Usage?.Invoke(new AiUsage(prompt - cached, completion, cached, 0, null));
			}

			if (chunk?["choices"] is not JsonArray { Count: > 0 } choices) continue;
			var choice = choices[0];
			if (choice?["finish_reason"] is JsonValue finish) finishReason = finish.ToString();

			var delta = choice?["delta"];
			if (delta?["content"] is JsonValue contentValue && contentValue.ToString() is { Length: > 0 } content)
			{
				text.Append(content);
				callbacks.Text?.Invoke(content);
			}

			if (delta?["tool_calls"] is JsonArray toolCallDeltas)
			{
				foreach (var toolCallDelta in toolCallDeltas)
				{
					var index = toolCallDelta?["index"]?.GetValue<int>() ?? 0;
					if (!calls.TryGetValue(index, out var call)) calls[index] = call = new ToolCallBuilder();
					if (toolCallDelta?["id"]?.ToString() is { Length: > 0 } id) call.Id = id;
					if (toolCallDelta?["function"]?["name"]?.ToString() is { Length: > 0 } name && call.Name.Length == 0) call.Name = name;
					call.Arguments.Append(toolCallDelta?["function"]?["arguments"]?.ToString());
				}
			}
		}

		// a reply cut off by the length limit may hold half a tool call; running it would only confuse things
		if (finishReason == "length")
		{
			calls.Clear();
			callbacks.Text?.Invoke("\n\n*(Reply cut off: it hit the length limit.)*");
		}

		var toolCalls = calls
			.Select(p => (Id: p.Value.Id.Length > 0 ? p.Value.Id : $"call_{p.Key}", p.Value.Name, Arguments: p.Value.Arguments.Length > 0 ? p.Value.Arguments.ToString() : "{}"))
			.ToList();

		var message = new JsonObject
		{
			["role"] = "assistant",
			["content"] = text.Length > 0 || toolCalls.Count == 0 ? text.ToString() : null,
		};
		if (toolCalls.Count > 0)
		{
			message["tool_calls"] = new JsonArray(toolCalls.Select(c => (JsonNode)new JsonObject
			{
				["id"] = c.Id,
				["type"] = "function",
				["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments },
			}).ToArray());
		}

		return (message, toolCalls);
	}

	private JsonArray BuildTools() => new(Tools.Select(t => (JsonNode)new JsonObject
	{
		["type"] = "function",
		["function"] = new JsonObject
		{
			["name"] = t.Name,
			["description"] = t.Description,
			["parameters"] = new JsonObject
			{
				["type"] = "object",
				["properties"] = new JsonObject(t.Properties.Select(p => KeyValuePair.Create(p.Key, JsonNode.Parse(p.Value.GetRawText())))),
				["required"] = new JsonArray(t.Required.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
			},
		},
	}).ToArray());

	private static Dictionary<string, JsonElement>? ParseArguments(string arguments)
	{
		try
		{
			return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>Pulls <c>error.message</c> out of an OpenAI-style error body, or returns the start of the raw body.</summary>
	private static string ErrorMessage(string body)
	{
		try
		{
			if (JsonNode.Parse(body)?["error"] is { } error)
				return error is JsonObject ? error["message"]?.ToString() ?? error.ToJsonString() : error.ToString();
		}
		catch (JsonException)
		{
			// not JSON
		}
		return body.Length > 500 ? body[..500] + "..." : body;
	}
}

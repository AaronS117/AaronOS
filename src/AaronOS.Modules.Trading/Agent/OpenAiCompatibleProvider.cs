using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AaronOS.Modules.Trading.Brokerage;

namespace AaronOS.Modules.Trading.Agent;

public class OpenAiCompatibleException(string message) : Exception(message);

/// <summary>
/// Any endpoint that speaks the OpenAI chat-completions format with tools, selected by base URL.
///
/// One adapter rather than one integration per vendor, because the format is the de facto standard:
/// Ollama and LM Studio serve it locally, and Groq, Gemini and OpenRouter all expose it. That makes
/// running this at no cost a base-URL change rather than new code, which is the whole point of it
/// existing.
/// </summary>
public class OpenAiCompatibleProvider(TradingCredentialStore credentialStore) : IAgentProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public string Name => "openai-compatible";

    /// <summary>
    /// A base URL is enough. Local servers accept any key or none, so demanding one would rule out
    /// exactly the free case this exists for.
    /// </summary>
    public bool IsConfigured => credentialStore.Load()?.OpenAiBaseUrl is { Length: > 0 };

    public IAgentConversation Start(
        string model, string systemPrompt, string firstUserMessage, IReadOnlyList<AgentTool> tools)
    {
        var credentials = credentialStore.Load()
            ?? throw new OpenAiCompatibleException("No endpoint saved. Add a base URL in Settings.");

        if (string.IsNullOrWhiteSpace(credentials.OpenAiBaseUrl))
        {
            throw new OpenAiCompatibleException("No endpoint saved. Add a base URL in Settings.");
        }

        return new OpenAiConversation(
            Http, credentials.OpenAiBaseUrl.TrimEnd('/'), credentials.OpenAiApiKey,
            model, systemPrompt, firstUserMessage, tools);
    }
}

file sealed class OpenAiConversation : IAgentConversation
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly JsonArray _messages;
    private readonly JsonArray _tools;

    public OpenAiConversation(
        HttpClient http,
        string baseUrl,
        string apiKey,
        string model,
        string systemPrompt,
        string firstUserMessage,
        IReadOnlyList<AgentTool> tools)
    {
        _http = http;
        _baseUrl = baseUrl;
        _apiKey = apiKey;
        _model = model;

        // This format carries the system prompt as the first message rather than a separate field.
        _messages =
        [
            new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
            new JsonObject { ["role"] = "user", ["content"] = firstUserMessage },
        ];

        _tools = new JsonArray();
        foreach (var tool in tools)
        {
            _tools.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = tool.ParametersSchema.DeepClone(),
                },
            });
        }
    }

    public async Task<AgentTurn> NextAsync(CancellationToken token = default)
    {
        var body = new JsonObject
        {
            ["model"] = _model,
            ["messages"] = _messages.DeepClone(),
            ["tools"] = _tools.DeepClone(),
            ["tool_choice"] = "auto",
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions")
        {
            Content = JsonContent.Create(body),
        };

        // Local servers ignore the header entirely; hosted ones require it. Sending it only when
        // present keeps one code path for both.
        if (_apiKey.Length > 0)
        {
            request.Headers.Add("Authorization", "Bearer " + _apiKey);
        }

        using var response = await _http.SendAsync(request, token);
        var raw = await response.Content.ReadAsStringAsync(token);

        if (!response.IsSuccessStatusCode)
        {
            throw new OpenAiCompatibleException(
                $"{_baseUrl} returned {(int)response.StatusCode}: {Truncate(raw, 400)}");
        }

        var parsed = JsonNode.Parse(raw)
                     ?? throw new OpenAiCompatibleException($"{_baseUrl} returned an empty body.");

        var message = parsed["choices"]?.AsArray().FirstOrDefault()?["message"]
                      ?? throw new OpenAiCompatibleException(
                          $"{_baseUrl} returned no message: {Truncate(raw, 300)}");

        var text = (string?)message["content"] ?? "";
        var calls = new List<AgentToolCall>();

        foreach (var call in message["tool_calls"]?.AsArray() ?? [])
        {
            var function = call?["function"];
            calls.Add(new AgentToolCall(
                (string?)call?["id"] ?? "",
                (string?)function?["name"] ?? "",
                ToolArguments.Parse((string?)function?["arguments"])));
        }

        if (calls.Count > 0)
        {
            _messages.Add(message.DeepClone());
        }

        return new AgentTurn(
            text.Trim(),
            calls,
            (int?)parsed["usage"]?["prompt_tokens"] ?? 0,
            (int?)parsed["usage"]?["completion_tokens"] ?? 0);
    }

    public void AddToolResults(IEnumerable<(string ToolCallId, string Content)> results)
    {
        // One message per result in this format, rather than one message holding every result.
        foreach (var (id, content) in results)
        {
            _messages.Add(new JsonObject
            {
                ["role"] = "tool",
                ["tool_call_id"] = id,
                ["content"] = content,
            });
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}

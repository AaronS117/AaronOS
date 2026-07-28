using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AaronOS.Modules.Trading.Brokerage;

namespace AaronOS.Modules.Trading.Agent;

public class AnthropicApiException(string message) : Exception(message);

/// <summary>
/// A thin client for the Messages API, kept deliberately untyped.
///
/// Content blocks are polymorphic and the tool-use loop has to echo assistant messages back
/// verbatim, so modelling them as records means writing a custom converter and then keeping it in
/// step with a format that gains fields. Passing <see cref="JsonNode"/> through hands that job to
/// the caller, which needs to read only three block types.
/// </summary>
public class AnthropicClient(TradingCredentialStore credentialStore)
{
    private const string Url = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public bool IsConfigured => credentialStore.Load()?.AnthropicApiKey is { Length: > 0 };

    public async Task<JsonNode> SendAsync(
        string model,
        string system,
        JsonArray messages,
        JsonArray tools,
        int maxTokens = 2048,
        CancellationToken token = default)
    {
        var apiKey = credentialStore.Load()?.AnthropicApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AnthropicApiException("No Anthropic API key saved. Add one in Settings.");
        }

        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["system"] = system,
            ["messages"] = messages.DeepClone(),
            ["tools"] = tools.DeepClone(),
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, Url)
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.Add("x-api-key", apiKey);
        message.Headers.Add("anthropic-version", ApiVersion);

        using var response = await Http.SendAsync(message, token);
        var text = await response.Content.ReadAsStringAsync(token);

        if (!response.IsSuccessStatusCode)
        {
            throw new AnthropicApiException(
                $"Anthropic returned {(int)response.StatusCode}: {Truncate(text, 400)}");
        }

        return JsonNode.Parse(text)
               ?? throw new AnthropicApiException("Anthropic returned an empty body.");
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}

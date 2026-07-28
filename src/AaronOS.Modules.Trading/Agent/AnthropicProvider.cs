using System.Text.Json.Nodes;

namespace AaronOS.Modules.Trading.Agent;

/// <summary>Anthropic's Messages API as an <see cref="IAgentProvider"/>.</summary>
public class AnthropicProvider(AnthropicClient client) : IAgentProvider
{
    public string Name => "anthropic";

    public bool IsConfigured => client.IsConfigured;

    public IAgentConversation Start(
        string model, string systemPrompt, string firstUserMessage, IReadOnlyList<AgentTool> tools) =>
        new AnthropicConversation(client, model, systemPrompt, firstUserMessage, tools);
}

file sealed class AnthropicConversation(
    AnthropicClient client,
    string model,
    string systemPrompt,
    string firstUserMessage,
    IReadOnlyList<AgentTool> tools) : IAgentConversation
{
    private readonly JsonArray _messages =
        [new JsonObject { ["role"] = "user", ["content"] = firstUserMessage }];

    private readonly JsonArray _tools = ToAnthropicTools(tools);

    public async Task<AgentTurn> NextAsync(CancellationToken token = default)
    {
        var response = await client.SendAsync(model, systemPrompt, _messages, _tools, token: token);
        var content = response["content"]?.AsArray() ?? [];

        var text = string.Join(
            "\n",
            content
                .Where(b => (string?)b?["type"] == "text")
                .Select(b => ((string?)b!["text"] ?? "").Trim())
                .Where(t => t.Length > 0));

        var calls = content
            .Where(b => (string?)b?["type"] == "tool_use")
            .Select(b => new AgentToolCall(
                (string?)b!["id"] ?? "",
                (string?)b["name"] ?? "",
                b["input"]?.DeepClone()))
            .ToList();

        // The assistant turn is echoed back exactly as received. Reconstructing it from the parsed
        // pieces would drop fields and make the next request invalid.
        if (calls.Count > 0)
        {
            _messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = content.DeepClone() });
        }

        return new AgentTurn(
            text,
            calls,
            (int?)response["usage"]?["input_tokens"] ?? 0,
            (int?)response["usage"]?["output_tokens"] ?? 0);
    }

    public void AddToolResults(IEnumerable<(string ToolCallId, string Content)> results)
    {
        var blocks = new JsonArray();
        foreach (var (id, content) in results)
        {
            blocks.Add(new JsonObject
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = id,
                ["content"] = content,
            });
        }

        _messages.Add(new JsonObject { ["role"] = "user", ["content"] = blocks });
    }

    private static JsonArray ToAnthropicTools(IReadOnlyList<AgentTool> tools)
    {
        var array = new JsonArray();
        foreach (var tool in tools)
        {
            array.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = tool.ParametersSchema.DeepClone(),
            });
        }

        return array;
    }
}

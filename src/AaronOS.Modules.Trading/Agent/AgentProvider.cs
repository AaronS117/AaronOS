using System.Text.Json.Nodes;

namespace AaronOS.Modules.Trading.Agent;

/// <summary>A tool the model may call, described once and translated per provider.</summary>
public readonly record struct AgentTool(string Name, string Description, JsonObject ParametersSchema);

/// <summary>
/// One tool call the model asked for. <see cref="Arguments"/> is null when the model produced
/// something that would not parse as JSON, which weaker local models do often enough that it has to
/// be an expected outcome rather than an exception.
/// </summary>
public readonly record struct AgentToolCall(string Id, string Name, JsonNode? Arguments);

/// <summary>One reply from the model: what it said, what it wants to call, what it cost.</summary>
public readonly record struct AgentTurn(
    string Text,
    IReadOnlyList<AgentToolCall> ToolCalls,
    int InputTokens,
    int OutputTokens);

/// <summary>
/// A model that can hold a tool-using conversation.
///
/// The abstraction exists because the two wire formats genuinely differ: Anthropic returns
/// polymorphic content blocks and expects the assistant turn echoed back verbatim, while the
/// OpenAI-compatible format returns a message with a tool_calls array and takes tool results as
/// separate messages. Both have to be reproduced exactly or the next request is malformed, so each
/// implementation keeps its own history in whatever shape its provider wants.
/// </summary>
public interface IAgentProvider
{
    /// <summary>Shown in the decision log, so a run can be traced back to what produced it.</summary>
    string Name { get; }

    bool IsConfigured { get; }

    IAgentConversation Start(string model, string systemPrompt, string firstUserMessage, IReadOnlyList<AgentTool> tools);
}

public interface IAgentConversation
{
    /// <summary>Sends the conversation so far and records the reply in its own history.</summary>
    Task<AgentTurn> NextAsync(CancellationToken token = default);

    /// <summary>Supplies the outcome of each tool call before the next <see cref="NextAsync"/>.</summary>
    void AddToolResults(IEnumerable<(string ToolCallId, string Content)> results);
}

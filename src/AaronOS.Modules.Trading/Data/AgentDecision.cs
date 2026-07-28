namespace AaronOS.Modules.Trading.Data;

/// <summary>
/// One run of the agent, recorded whether or not it traded.
///
/// Cycles that decided to do nothing are kept as well as cycles that acted. Logging only the trades
/// would make the record look decisive in hindsight and would hide how often the answer was "wait",
/// which is the more common and usually the better call.
/// </summary>
public class AgentDecision
{
    public int Id { get; set; }
    public DateTime RanAtUtc { get; set; }
    public string Model { get; set; } = "";

    /// <summary>The agent's own explanation, kept verbatim so it can be read back later.</summary>
    public string Reasoning { get; set; } = "";

    /// <summary>One line naming what happened, e.g. "Bought 4 MSFT" or "No action".</summary>
    public string ActionSummary { get; set; } = "No action";

    /// <summary>Anything the guardrails refused, so a blocked order is visible rather than silent.</summary>
    public string? BlockedActions { get; set; }

    public string? Error { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

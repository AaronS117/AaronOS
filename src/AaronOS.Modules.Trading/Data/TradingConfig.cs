namespace AaronOS.Modules.Trading.Data;

/// <summary>
/// The single row of trading settings and, more importantly, limits.
///
/// The limits exist as data the application enforces rather than as instructions in a prompt. A
/// model asked politely to stay under a position size will usually comply and occasionally will
/// not, and "usually" is not a risk control. See <see cref="Trading.TradingGuardrails"/>.
/// </summary>
public class TradingConfig
{
    public int Id { get; set; }

    /// <summary>Master switch. When false the scheduler runs no cycles at all.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Comma-separated symbols the agent may touch. Anything else is refused.</summary>
    public string Watchlist { get; set; } = "SPY,QQQ,AAPL,MSFT,NVDA,AMZN,GOOGL";

    /// <summary>Ceiling on any one position as a percent of account equity.</summary>
    public decimal MaxPositionPercent { get; set; } = 10m;

    /// <summary>
    /// Symbols exempt from the per-position cap, though never from the total exposure cap.
    ///
    /// That cap exists to limit how much rides on one company. A broad index fund is not one company,
    /// so applying the same 10% to SPY is a category error — and a consequential one: the brief tells
    /// the agent to hold the index when it has no view, and a 10% ceiling would leave ninety percent in
    /// cash, which is the failure the brief was rewritten to prevent.
    /// </summary>
    public string BroadIndexSymbols { get; set; } = "SPY,QQQ,VTI,VOO,IVV";

    /// <summary>
    /// Ceiling on total invested value as a percent of equity.
    ///
    /// A hundred, so the agent is allowed to be as invested as the benchmark it is judged against. This
    /// was 80, and a replay showed the cost precisely: buy-and-hold through the harness returned 9.01%
    /// where SPY returned 11.27%, and 80% of 11.27 is 9.02. The whole gap was the cash the cap forced,
    /// which meant the experiment asked whether the agent could beat an index while forbidding it from
    /// matching one. That is a handicap, not a risk control.
    ///
    /// The genuine protections are elsewhere and unchanged: no borrowing, no short selling, and no more
    /// than <see cref="MaxPositionPercent"/> in any single company. Available cash is what binds now,
    /// which is exactly the constraint a long-only account actually has.
    /// </summary>
    public decimal MaxInvestedPercent { get; set; } = 100m;

    /// <summary>
    /// Trailing stop, as a percent below a position's peak. Zero disables it.
    ///
    /// Seven by choice, on evidence rather than instinct: over six years of SPY a 7% trailing stop cut
    /// the worst peak-to-trough fall from 24% to 15% and cost about 21 points of return. Wider stops
    /// tested here reduced return without reducing the fall, because they sold near the bottom and bought
    /// back higher. This is a deliberate trade of return for a smoother ride, not an edge.
    /// </summary>
    public decimal StopLossPercent { get; set; } = 7m;

    /// <summary>
    /// Ceiling on everything that is not a broad index fund, combined, as a percent of equity.
    ///
    /// The per-company cap limits any single name; this limits the whole risky sleeve. Without it, ten
    /// names at ten percent each is a hundred percent in individual companies while every individual cap
    /// is satisfied — technically compliant and the opposite of an index core.
    /// </summary>
    public decimal MaxIndividualStocksPercent { get; set; } = 30m;

    public int MaxTradesPerDay { get; set; } = 6;
    public int CycleIntervalMinutes { get; set; } = 30;

    public string Model { get; set; } = "claude-sonnet-5";

    /// <summary>
    /// Which model service to call: "anthropic", or "openai-compatible" for anything speaking the
    /// OpenAI chat-completions format — a local Ollama server, or a hosted free tier.
    ///
    /// Stored as the provider's own <c>Name</c> rather than an enum so adding a third provider does
    /// not need a schema change, and an unrecognised value falls back rather than crashing.
    /// </summary>
    public string Provider { get; set; } = "anthropic";

    /// <summary>
    /// Whether recent headlines are included in the brief.
    ///
    /// Exists as a switch so the question can be asked properly. Reading text and forming a view from it
    /// is the one thing a language model is plausibly better at than a rule, and everything it could
    /// derive from prices alone a rule computes better and identically every time. Running the same agent
    /// over the same window with this on and off is what separates a news contribution from noise.
    /// </summary>
    public bool IncludeNews { get; set; }

    /// <summary>Free text appended to the agent's brief — the strategy in your own words.</summary>
    public string StrategyNotes { get; set; } =
        "Favour a small number of high-conviction positions. Do nothing when nothing looks compelling; " +
        "holding cash is a valid decision and churning is not.";

    /// <summary>
    /// First day the experiment ran, set once and never rewritten. Performance is always measured
    /// from here, so a bad run cannot be quietly restarted and counted from the recovery.
    /// </summary>
    public DateOnly? StartedOn { get; set; }

    /// <summary>
    /// Closed trades needed before a win rate is worth showing. Below this the UI reports the count
    /// and refuses the percentage, because a run of eight winners is noise that reads like skill.
    /// </summary>
    public int MinTradesForStats { get; set; } = 30;
}

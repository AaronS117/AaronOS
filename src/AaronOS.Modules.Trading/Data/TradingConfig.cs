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

    /// <summary>Ceiling on total invested value as a percent of equity, so cash is always held back.</summary>
    public decimal MaxInvestedPercent { get; set; } = 80m;

    public int MaxTradesPerDay { get; set; } = 6;
    public int CycleIntervalMinutes { get; set; } = 30;

    public string Model { get; set; } = "claude-sonnet-5";

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

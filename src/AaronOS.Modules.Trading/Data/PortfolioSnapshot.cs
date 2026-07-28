namespace AaronOS.Modules.Trading.Data;

/// <summary>
/// One day's closing state, written once per day.
///
/// It records the benchmark's close alongside the account's equity on purpose. Reconstructing what
/// the index did afterwards invites choosing a flattering window, whereas a value stamped on the day
/// makes the comparison fixed. The benchmark is the whole point: a strategy that made money in a
/// rising market has demonstrated nothing until it is set against simply having held the index.
/// </summary>
public class PortfolioSnapshot
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public decimal Equity { get; set; }
    public decimal Cash { get; set; }

    /// <summary>Closing price of the benchmark on this date, null when the fetch failed.</summary>
    public decimal? BenchmarkClose { get; set; }

    public const string BenchmarkSymbol = "SPY";
}

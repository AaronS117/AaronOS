using AaronOS.Modules.Trading.Data;

namespace AaronOS.Modules.Trading.Trading;

/// <summary>
/// The verdict on the experiment. Deliberately shaped so the flattering numbers cannot be read
/// without the deflating ones beside them.
/// </summary>
public readonly record struct PerformanceSummary(
    int DayCount,
    int ClosedTradeCount,
    bool HasMeaningfulSample,
    decimal StartEquity,
    decimal CurrentEquity,
    decimal StrategyReturnPercent,
    decimal? BenchmarkReturnPercent,
    decimal? AlphaPercent,
    decimal MaxDrawdownPercent,
    decimal? WinRatePercent)
{
    /// <summary>True only when the benchmark is known and the strategy is behind it.</summary>
    public bool IsBehindBenchmark => AlphaPercent is { } alpha && alpha < 0;

    /// <summary>
    /// The sentence the dashboard leads with. It names the benchmark comparison first because that
    /// is the only figure that distinguishes a strategy from a rising market.
    /// </summary>
    public string Verdict => (BenchmarkReturnPercent, AlphaPercent) switch
    {
        (null, _) => "No benchmark data yet, so there is nothing to compare against.",
        // The count is not repeated here; it sits directly beneath this line on the dashboard.
        (_, { } alpha) when !HasMeaningfulSample =>
            $"{alpha:+0.0;-0.0} points against SPY, on too few trades to mean anything yet.",
        (_, { } alpha) when alpha >= 0 => $"Ahead of SPY by {alpha:0.0} points.",
        (_, { } alpha) => $"Behind SPY by {Math.Abs(alpha):0.0} points.",
    };
}

/// <summary>
/// Turns the daily snapshots into an honest verdict.
///
/// Three decisions here are about resisting self-deception rather than about arithmetic. Return is
/// always reported next to the benchmark over the identical window, because a paper account that
/// gained twelve percent while the index gained fifteen has lost. Drawdown is reported because an
/// equity curve's worst moment says more about whether a strategy is survivable than its endpoint
/// does. And the win rate is withheld entirely below a usable sample size, since a run of eight
/// winners reads like skill and is noise.
/// </summary>
public static class PerformanceCalculator
{
    public static PerformanceSummary Summarise(
        IReadOnlyList<PortfolioSnapshot> snapshots,
        int closedTradeCount,
        int winningTradeCount,
        int minTradesForStats)
    {
        if (snapshots.Count == 0)
        {
            return new PerformanceSummary(0, closedTradeCount, false, 0, 0, 0, null, null, 0, null);
        }

        var ordered = snapshots.OrderBy(s => s.Date).ToList();
        var first = ordered[0];
        var last = ordered[^1];

        var strategyReturn = PercentChange(first.Equity, last.Equity) ?? 0m;

        // Both ends must be present. Substituting a nearby day's close would quietly shift the
        // comparison window in whichever direction happened to help.
        var benchmarkReturn = PercentChange(first.BenchmarkClose, last.BenchmarkClose);
        var alpha = benchmarkReturn is { } benchmark ? strategyReturn - benchmark : (decimal?)null;

        var hasSample = closedTradeCount >= minTradesForStats;
        var winRate = hasSample && closedTradeCount > 0
            ? Math.Round(100m * winningTradeCount / closedTradeCount, 1)
            : (decimal?)null;

        return new PerformanceSummary(
            DayCount: ordered.Count,
            ClosedTradeCount: closedTradeCount,
            HasMeaningfulSample: hasSample,
            StartEquity: first.Equity,
            CurrentEquity: last.Equity,
            StrategyReturnPercent: Math.Round(strategyReturn, 2),
            BenchmarkReturnPercent: benchmarkReturn is { } b ? Math.Round(b, 2) : null,
            AlphaPercent: alpha is { } a ? Math.Round(a, 2) : null,
            MaxDrawdownPercent: Math.Round(MaxDrawdownPercent(ordered), 2),
            WinRatePercent: winRate);
    }

    /// <summary>
    /// The deepest fall from any previous peak, as a positive percent. Zero when the curve never
    /// dropped below a high-water mark.
    /// </summary>
    public static decimal MaxDrawdownPercent(IReadOnlyList<PortfolioSnapshot> orderedSnapshots)
    {
        var peak = 0m;
        var worst = 0m;

        foreach (var snapshot in orderedSnapshots)
        {
            if (snapshot.Equity > peak)
            {
                peak = snapshot.Equity;
            }

            if (peak <= 0)
            {
                continue;
            }

            var fall = (peak - snapshot.Equity) / peak * 100m;
            if (fall > worst)
            {
                worst = fall;
            }
        }

        return worst;
    }

    private static decimal? PercentChange(decimal? from, decimal? to) =>
        from is > 0 && to is not null ? (to.Value - from.Value) / from.Value * 100m : null;
}

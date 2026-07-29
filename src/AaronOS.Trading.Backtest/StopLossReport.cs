using AaronOS.Modules.Trading.Backtest;
using AaronOS.Modules.Trading.Brokerage;
using AaronOS.Modules.Trading.Data;

namespace AaronOS.Trading.Backtest;

/// <summary>
/// Answers one question with data instead of opinion: does selling after a fall protect you?
///
/// It is the most commonly requested rule in retail investing and the hardest to reason about from the
/// armchair, because the thing it protects against — watching a position fall — is vivid, and the thing
/// it costs — being out of the market when it turns — is invisible. Running it over a real bear market
/// and a real recovery makes both visible at once.
///
/// Every variant is measured over identical sessions with identical costs, against holding through.
/// </summary>
public static class StopLossReport
{
    public static async Task<int> RunAsync(AlpacaClient alpaca, TradingConfig live)
    {
        var windows = new (string Label, DateOnly From, DateOnly To)[]
        {
            ("2022 bear market      ", new DateOnly(2022, 1, 3), new DateOnly(2022, 12, 30)),
            ("2022 bear + recovery  ", new DateOnly(2022, 1, 3), new DateOnly(2024, 12, 31)),
            ("2025 selloff + rebound", new DateOnly(2025, 1, 2), new DateOnly(2025, 12, 31)),
            ("full history          ", new DateOnly(2019, 1, 2), new DateOnly(2026, 7, 24)),
        };

        Console.WriteLine("fetching history…");
        var raw = await alpaca.GetDailyBarsAsync(
            ["SPY"], new DateOnly(2018, 11, 1), new DateOnly(2026, 7, 24));

        if (!raw.TryGetValue("SPY", out var spy) || spy.Count == 0)
        {
            Console.WriteLine("no SPY bars returned");
            return 1;
        }

        var market = new ReplayMarket(
            [new("SPY", spy.Select(b => new DailyBar(b.Date, b.Open, b.Close)).ToList())],
            "SPY");

        Console.WriteLine($"  {spy.Count} sessions {spy[0].Date} .. {spy[^1].Date}\n");

        // The live caps, but a single-symbol universe so the comparison is purely about the exit rule.
        var config = new TradingConfig
        {
            IsEnabled = true,
            Watchlist = "SPY",
            BroadIndexSymbols = "SPY",
            MaxPositionPercent = live.MaxPositionPercent,
            MaxInvestedPercent = live.MaxInvestedPercent,
            MaxTradesPerDay = 8,
            MinTradesForStats = live.MinTradesForStats,
        };

        foreach (var (label, from, to) in windows)
        {
            var sessions = market.DaysBetween(from, to);
            if (sessions.Count < 30)
            {
                Console.WriteLine($"=== {label} — only {sessions.Count} sessions, skipped ===\n");
                continue;
            }

            Console.WriteLine($"=== {label}  {sessions[0]} .. {sessions[^1]}  ({sessions.Count} sessions) ===");

            var hold = new BaselineRunner(market)
                .Run(new BuyAndHoldIndexBaseline(), config, from, to);
            Console.WriteLine($"  {"hold through",-34} {hold.Performance.StrategyReturnPercent,8:+0.00;-0.00}%  "
                              + $"worst drawdown −{hold.Performance.MaxDrawdownPercent,5:0.0}%   1 trade");

            foreach (var (stop, wait) in new[] { (10m, 20), (10m, 5), (15m, 20), (7m, 20) })
            {
                var strategy = new StopLossBaseline("SPY", stop, wait);
                var run = new BaselineRunner(market).Run(strategy, config, from, to);
                var delta = run.Performance.StrategyReturnPercent - hold.Performance.StrategyReturnPercent;

                Console.WriteLine(
                    $"  {$"stop {stop:0}% / back in after {wait}d",-34} "
                    + $"{run.Performance.StrategyReturnPercent,8:+0.00;-0.00}%  "
                    + $"worst drawdown −{run.Performance.MaxDrawdownPercent,5:0.0}%   "
                    + $"{run.OrdersFilled,2} trades   vs holding {delta,7:+0.0;-0.0} pts");
            }

            Console.WriteLine();
        }

        Console.WriteLine("Reading this: the drawdown column is what the rule buys you, and the return");
        Console.WriteLine("column is what it costs. Both matter — a smaller fall is worth something real");
        Console.WriteLine("if you would otherwise sell at the bottom in a panic.");
        return 0;
    }
}

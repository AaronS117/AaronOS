using System.IO;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Trading.Agent;
using AaronOS.Modules.Trading.Data;
using AaronOS.Modules.Trading.Trading;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Trading.Backtest;

public readonly record struct BacktestResult(
    string Label,
    DateOnly From,
    DateOnly To,
    int Sessions,
    int DecisionsMade,
    int OrdersFilled,
    int OrdersRefused,
    PerformanceSummary Performance)
{
    public string Headline =>
        $"{Label}: {Performance.StrategyReturnPercent:+0.00;-0.00}% vs SPY " +
        $"{Performance.BenchmarkReturnPercent?.ToString("+0.00;-0.00") ?? "—"}%, " +
        $"alpha {Performance.AlphaPercent?.ToString("+0.00;-0.00") ?? "—"} pts, " +
        $"worst drawdown −{Performance.MaxDrawdownPercent:0.00}%, " +
        $"{OrdersFilled} fills / {Performance.ClosedTradeCount} closed";
}

/// <summary>
/// Steps the real agent through historical sessions, one decision per day.
///
/// Everything downstream of the decision is production code: the same guardrails, the same order
/// recording, the same performance maths that the live run uses. Only the clock and the broker are
/// substituted, which is what makes a result here comparable to a result there.
///
/// Each run gets its own database file. Writing replay orders into the live one would corrupt the
/// record the six-month experiment is being judged on, and that record cannot be reconstructed.
/// </summary>
public sealed class BacktestRunner(ReplayMarket market, IAgentProvider provider)
{
    public async Task<BacktestResult> RunAsync(
        string label,
        TradingConfig config,
        DateOnly from,
        DateOnly to,
        string dbPath,
        decimal startingCash = 100_000m,
        ReplayCosts? costs = null,
        Action<string>? log = null,
        CancellationToken token = default)
    {
        var sessions = market.DaysBetween(from, to);
        if (sessions.Count == 0)
        {
            throw new ArgumentException($"No trading sessions between {from} and {to}.", nameof(from));
        }

        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            File.Delete(dbPath + suffix);
        }

        var broker = new ReplayBroker(market, startingCash, costs ?? new ReplayCosts());
        var clock = new ReplayTimeProvider(sessions[0]);
        var factory = new BacktestContextFactory(dbPath);

        await using (var db = factory.CreateDbContext())
        {
            await SchemaBootstrapper.EnsureSchemaAsync(db);

            // A copy, so a caller cannot accidentally hand the live configuration row to a replay and
            // have it mutated by the run.
            db.Add(Clone(config));
            await db.SaveChangesAsync(token);
        }

        var agent = new TradingAgent(factory, broker, new AgentProviderRegistry([provider]), clock);
        var recorder = new SnapshotRecorder(factory, broker, clock);

        var decisions = 0;
        foreach (var session in sessions)
        {
            broker.Today = session;
            clock.SetDate(session);

            var result = await agent.RunCycleAsync(token);
            if (result.Ran)
            {
                decisions++;
            }

            await recorder.ReconcileOpenOrdersAsync(token);
            await recorder.RecordTodayAsync(token);

            if (decisions % 20 == 0 && result.Ran)
            {
                log?.Invoke($"  {session}  equity {broker.Equity:C0}  ({decisions} decisions)");
            }
        }

        await using var final = factory.CreateDbContext();
        var snapshots = await final.Set<PortfolioSnapshot>().OrderBy(s => s.Date).ToListAsync(token);
        var orders = await final.Set<TradeOrder>().ToListAsync(token);
        var refused = await final.Set<AgentDecision>()
            .CountAsync(d => d.BlockedActions != null, token);

        var (closed, wins) = RoundTripCounter.Count(orders);
        var performance = PerformanceCalculator.Summarise(snapshots, closed, wins, config.MinTradesForStats);

        return new BacktestResult(
            label, sessions[0], sessions[^1], sessions.Count, decisions, orders.Count, refused, performance);
    }

    private static TradingConfig Clone(TradingConfig source) => new()
    {
        IsEnabled = true,
        Watchlist = source.Watchlist,
        MaxPositionPercent = source.MaxPositionPercent,
        MaxInvestedPercent = source.MaxInvestedPercent,
        MaxTradesPerDay = source.MaxTradesPerDay,
        CycleIntervalMinutes = source.CycleIntervalMinutes,
        Model = source.Model,
        Provider = source.Provider,
        StrategyNotes = source.StrategyNotes,
        MinTradesForStats = source.MinTradesForStats,
    };

    private sealed class BacktestContextFactory(string path) : IDbContextFactory<AaronOsDbContext>
    {
        public AaronOsDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AaronOsDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
            IAppModule[] modules = [new TradingModule()];
            return new AaronOsDbContext(options, modules);
        }
    }
}

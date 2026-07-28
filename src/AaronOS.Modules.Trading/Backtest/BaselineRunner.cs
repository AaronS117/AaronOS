using AaronOS.Modules.Trading.Data;
using AaronOS.Modules.Trading.Trading;

namespace AaronOS.Modules.Trading.Backtest;

/// <summary>
/// Replays a mechanical strategy over the same sessions, fills, costs and guardrails as the agent.
///
/// Every one of those has to match or the comparison measures the harness rather than the decisions. In
/// particular the guardrails apply: a rule allowed to hold 20% of one name while the agent is capped at
/// 10% would win on permission, not on judgement.
///
/// No database, because there is nothing here worth auditing later — a rule is fully described by its
/// source. Snapshots are held in memory and handed to the same performance maths the live run uses.
/// </summary>
public sealed class BaselineRunner(ReplayMarket market)
{
    /// <summary>The first order the guardrails refused, or null if none were. Reported, never swallowed.</summary>
    public string? FirstRefusal { get; private set; }


    public BacktestResult Run(
        IBaselineStrategy strategy,
        TradingConfig config,
        DateOnly from,
        DateOnly to,
        decimal startingCash = 100_000m,
        ReplayCosts? costs = null)
    {
        var sessions = market.DaysBetween(from, to);
        if (sessions.Count == 0)
        {
            throw new ArgumentException($"No trading sessions between {from} and {to}.", nameof(from));
        }

        var broker = new ReplayBroker(market, startingCash, costs ?? new ReplayCosts());
        var watchlist = TradingGuardrails.ParseWatchlist(config.Watchlist);
        var snapshots = new List<PortfolioSnapshot>(sessions.Count);
        var orders = new List<TradeOrder>();
        var refusedSessions = 0;

        foreach (var session in sessions)
        {
            broker.Today = session;

            var quotes = broker.GetQuotesAsync(watchlist).GetAwaiter().GetResult();
            var positions = broker.GetPositionsAsync().GetAwaiter().GetResult();

            // Counted the same way the agent's cap is: orders already placed on this session.
            var placedToday = 0;
            var refusedHere = false;

            var context = new BaselineContext(
                market,
                session,
                new AccountState(broker.Equity, broker.Cash, true, positions, placedToday),
                quotes,
                config);

            foreach (var request in strategy.Decide(context))
            {
                var state = new AccountState(
                    broker.Equity,
                    broker.Cash,
                    true,
                    broker.GetPositionsAsync().GetAwaiter().GetResult(),
                    placedToday);

                var verdict = TradingGuardrails.Check(request, state, config);
                if (!verdict.Allowed)
                {
                    refusedHere = true;

                    // Kept so a strategy that placed nothing can be told apart from one that chose
                    // nothing. A silent zero has now been mistaken for a decision three times.
                    FirstRefusal ??= $"{session:yyyy-MM-dd} {request.Symbol}: {verdict.Reason}";
                    continue;
                }

                var submitted = broker
                    .PlaceMarketOrderAsync(request.Symbol, request.Side, request.Quantity)
                    .GetAwaiter().GetResult();
                placedToday++;

                var fill = broker.Fills[^1];
                orders.Add(new TradeOrder
                {
                    BrokerOrderId = submitted.BrokerOrderId,
                    Symbol = request.Symbol,
                    Side = request.Side,
                    Quantity = request.Quantity,
                    SubmittedAtUtc = session.ToDateTime(new TimeOnly(14, 30), DateTimeKind.Utc),
                    EstimatedPrice = request.EstimatedPrice,
                    FilledPrice = fill.Price,
                    FilledAtUtc = session.ToDateTime(new TimeOnly(14, 31), DateTimeKind.Utc),
                    Status = "filled",
                });
            }

            if (refusedHere)
            {
                refusedSessions++;
            }

            snapshots.Add(new PortfolioSnapshot
            {
                Date = session,
                Equity = broker.Equity,
                Cash = broker.Cash,
                BenchmarkClose = market.BarOn(market.BenchmarkSymbol, session)?.Close,
            });
        }

        var (closed, wins) = RoundTripCounter.Count(orders);
        var performance = PerformanceCalculator.Summarise(snapshots, closed, wins, config.MinTradesForStats);

        return new BacktestResult(
            strategy.Name, sessions[0], sessions[^1], sessions.Count,
            DecisionsMade: sessions.Count, orders.Count, refusedSessions, performance);
    }
}

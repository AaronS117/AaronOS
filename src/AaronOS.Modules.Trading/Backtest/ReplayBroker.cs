using AaronOS.Modules.Trading.Brokerage;
using AaronOS.Modules.Trading.Data;
using AaronOS.Modules.Trading.Trading;

namespace AaronOS.Modules.Trading.Backtest;

/// <summary>Costs applied to every simulated fill. Defaults are deliberately pessimistic.</summary>
public readonly record struct ReplayCosts(decimal HalfSpreadBps = 2m, decimal SlippageBps = 3m)
{
    public decimal HalfSpread => HalfSpreadBps / 10_000m;
    public decimal Slippage => SlippageBps / 10_000m;
}

/// <summary>
/// A broker backed by historical bars instead of a network.
///
/// Two choices here are the difference between a replay worth reading and one that lies.
///
/// Orders fill at the <em>next</em> session's open, never at the close the decision was made on.
/// Filling on the same bar the model just looked at is lookahead wearing a disguise: it grants the
/// trade a price that was only knowable because the day had already finished.
///
/// Every fill pays a spread and slippage. A frictionless replay is the single most common way a
/// backtest flatters a strategy, and the error compounds with turnover — precisely the behaviour a
/// trading agent is most likely to exhibit. The defaults err towards costing too much.
/// </summary>
public sealed class ReplayBroker(ReplayMarket market, decimal startingCash, ReplayCosts costs)
    : AlpacaClient(new TradingCredentialStore())
{
    private readonly Dictionary<string, int> _positions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every fill, in order, for the report and for reconciling stored orders.</summary>
    public List<ReplayFill> Fills { get; } = [];

    public decimal Cash { get; private set; } = startingCash;

    /// <summary>The session being replayed. The runner advances this.</summary>
    public DateOnly Today { get; set; }

    public override bool IsConfigured => true;

    /// <summary>Every replayed day is a session by construction — non-sessions have no bars.</summary>
    public override Task<bool> IsMarketOpenAsync(CancellationToken token = default) => Task.FromResult(true);

    public decimal Equity => Cash + _positions.Sum(p => p.Value * (CloseOf(p.Key) ?? 0m));

    public override Task<BrokerAccount> GetAccountAsync(CancellationToken token = default) =>
        Task.FromResult(new BrokerAccount(Equity, Cash, "ACTIVE"));

    public override Task<List<HeldPosition>> GetPositionsAsync(CancellationToken token = default) =>
        Task.FromResult(_positions
            .Where(p => p.Value > 0)
            .Select(p => new HeldPosition(p.Key, p.Value, p.Value * (CloseOf(p.Key) ?? 0m)))
            .ToList());

    /// <summary>
    /// A synthetic two-sided quote around the session's close. A symbol with no bar for the day is
    /// absent rather than priced at zero, so the guardrails refuse it instead of treating it as free.
    /// </summary>
    public override Task<Dictionary<string, SymbolQuote>> GetQuotesAsync(
        IEnumerable<string> symbols, CancellationToken token = default)
    {
        var quotes = new Dictionary<string, SymbolQuote>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            if (CloseOf(symbol) is { } close and > 0)
            {
                quotes[symbol] = new SymbolQuote(
                    symbol,
                    close * (1 - costs.HalfSpread),
                    close * (1 + costs.HalfSpread));
            }
        }

        return Task.FromResult(quotes);
    }

    public override Task<decimal?> GetLatestDailyCloseAsync(string symbol, CancellationToken token = default) =>
        Task.FromResult(CloseOf(symbol));

    public override Task<SubmittedOrder> PlaceMarketOrderAsync(
        string symbol, OrderSide side, int quantity, CancellationToken token = default)
    {
        // The next session's open, falling back to today's close only on the final day of the window,
        // where there is no tomorrow to fill into.
        var fillPrice = market.NextBarAfter(symbol, Today)?.Open ?? CloseOf(symbol);
        if (fillPrice is not > 0)
        {
            throw new AlpacaApiException($"No price available to fill {symbol}.");
        }

        var price = side == OrderSide.Buy
            ? fillPrice.Value * (1 + costs.Slippage)
            : fillPrice.Value * (1 - costs.Slippage);

        var held = _positions.GetValueOrDefault(symbol);

        if (side == OrderSide.Buy)
        {
            var cost = price * quantity;

            // A backstop behind the guardrails rather than a duplicate of them: if a cap were ever
            // wrong, the ledger must still refuse to invent money.
            if (cost > Cash)
            {
                throw new AlpacaApiException(
                    $"Insufficient simulated cash for {quantity} {symbol}: {cost:C2} against {Cash:C2}.");
            }

            Cash -= cost;
            _positions[symbol] = held + quantity;
        }
        else
        {
            if (quantity > held)
            {
                throw new AlpacaApiException(
                    $"Cannot sell {quantity} {symbol} with {held} held; shorting is not simulated.");
            }

            Cash += price * quantity;
            _positions[symbol] = held - quantity;
        }

        var id = $"replay-{Fills.Count + 1}";
        Fills.Add(new ReplayFill(id, Today, symbol, side, quantity, price));
        return Task.FromResult(new SubmittedOrder(id, "filled"));
    }

    public override Task<(string Status, decimal? FilledPrice, DateTime? FilledAtUtc)> GetOrderAsync(
        string brokerOrderId, CancellationToken token = default)
    {
        var fill = Fills.FirstOrDefault(f => f.OrderId == brokerOrderId);
        return Task.FromResult(fill.OrderId is null
            ? ("rejected", (decimal?)null, (DateTime?)null)
            : ("filled", fill.Price, fill.Date.ToDateTime(new TimeOnly(14, 30), DateTimeKind.Utc)));
    }

    private decimal? CloseOf(string symbol) => market.BarOn(symbol, Today)?.Close;
}

public readonly record struct ReplayFill(
    string? OrderId, DateOnly Date, string Symbol, OrderSide Side, int Quantity, decimal Price);

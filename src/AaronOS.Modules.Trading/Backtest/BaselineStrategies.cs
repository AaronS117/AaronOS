using AaronOS.Modules.Trading.Brokerage;
using AaronOS.Modules.Trading.Data;
using AaronOS.Modules.Trading.Trading;

namespace AaronOS.Modules.Trading.Backtest;

/// <summary>Everything a rule sees. Deliberately the same information the agent's brief carries.</summary>
public readonly record struct BaselineContext(
    ReplayMarket Market,
    DateOnly Today,
    AccountState Account,
    Dictionary<string, SymbolQuote> Quotes,
    TradingConfig Config)
{
    public IReadOnlyList<string> Watchlist => TradingGuardrails.ParseWatchlist(Config.Watchlist);

    public decimal InvestedCap => Account.Equity * Config.MaxInvestedPercent / 100m;

    /// <summary>
    /// Whole shares of a symbol that a target dollar amount buys at the current ask, clamped to the
    /// cash the guardrails will actually let go.
    ///
    /// The clamp is the point. Sizing to the invested cap and letting the guardrail refuse the result
    /// produced a strategy that placed no orders and reported +0.00% — indistinguishable, in the output,
    /// from a strategy that had decided to stay out. A rule should ask for what it can have.
    /// </summary>
    public int SharesFor(string symbol, decimal dollars)
    {
        if (!Quotes.TryGetValue(symbol, out var quote) || quote.Ask <= 0)
        {
            return 0;
        }

        var affordable = Math.Min(dollars, Account.Cash * SpendableCashFraction);
        return affordable <= 0 ? 0 : (int)Math.Floor(affordable / quote.Ask);
    }

    /// <summary>
    /// How much of the cash balance an order may be sized against.
    ///
    /// Below one because an order is sized at today's close and fills at tomorrow's open: an overnight
    /// gap up turns an order costing 99% of cash into one costing 101%, and the broker rejects it. Three
    /// percent absorbs an ordinary gap. It is a real cost — that cash sits idle — and the alternative is
    /// an order that is occasionally refused for a reason the strategy could not have known.
    /// </summary>
    public const decimal SpendableCashFraction = 0.97m;

    /// <summary>
    /// Dollars a symbol can still take before its own cap binds, honouring the broad-index exemption.
    ///
    /// The same lesson as the cash clamp: a rule should ask for what it can have. Equal weight without
    /// this asked for a sixth of the account in each name, was refused on every individual name by the
    /// 10% per-company cap, and reported a return that looked like a strategy decision.
    /// </summary>
    public decimal PositionHeadroom(string symbol)
    {
        var capPercent = TradingGuardrails.IsBroadIndex(symbol, Config.BroadIndexSymbols)
            ? Config.MaxInvestedPercent
            : Config.MaxPositionPercent;

        return Math.Max(0, (Account.Equity * capPercent / 100m) - HeldValue(symbol));
    }

    public decimal HeldValue(string symbol) => Account.Positions
        .FirstOrDefault(p => string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
        .MarketValue;

    public int HeldShares(string symbol) => Account.Positions
        .FirstOrDefault(p => string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
        .Quantity;
}

/// <summary>
/// A mechanical strategy, for the agent to be measured against.
///
/// These exist because "it lost to SPY" is a weak finding on its own. The useful question is whether
/// the model's judgement adds anything over arithmetic, and that needs arithmetic on the same window,
/// through the same fills, the same costs and the same guardrails. If twenty lines of rule beats it,
/// that is the answer.
/// </summary>
public interface IBaselineStrategy
{
    string Name { get; }

    /// <summary>Orders wanted this session. Every one still passes through the guardrails.</summary>
    IEnumerable<OrderRequest> Decide(BaselineContext context);
}

/// <summary>
/// Buy the index once, then never trade again.
///
/// Distinct from quoting SPY's total return: this pays the spread and slippage on entry, is bound by
/// the same exposure cap, and leaves the remainder in cash. It is the honest floor — what the account
/// would have done with no opinions at all.
/// </summary>
public sealed class BuyAndHoldIndexBaseline(string symbol = "SPY") : IBaselineStrategy
{
    public string Name => $"buy-and-hold {symbol}";

    public IEnumerable<OrderRequest> Decide(BaselineContext context)
    {
        if (context.HeldShares(symbol) > 0 || !context.Quotes.TryGetValue(symbol, out var quote))
        {
            yield break;
        }

        var shares = context.SharesFor(symbol, context.InvestedCap);
        if (shares > 0)
        {
            yield return new OrderRequest(symbol, OrderSide.Buy, shares, quote.Mid);
        }
    }
}

/// <summary>
/// Equal weight across the watchlist, rebalanced monthly.
///
/// No forecast of any kind — it is diversification and nothing else, which is why it belongs here. The
/// per-position cap binds first for individual names, so in practice this holds each name at its cap
/// and the index takes the remainder.
/// </summary>
public sealed class EqualWeightMonthlyBaseline : IBaselineStrategy
{
    public string Name => "equal weight, monthly";

    public IEnumerable<OrderRequest> Decide(BaselineContext context)
    {
        if (!context.Market.IsFirstSessionOfMonth(context.Today))
        {
            yield break;
        }

        var watchlist = context.Watchlist;
        var target = context.InvestedCap / Math.Max(1, watchlist.Count);

        foreach (var symbol in watchlist)
        {
            if (!context.Quotes.TryGetValue(symbol, out var quote))
            {
                continue;
            }

            // Only ever tops up. Trimming on the way up would add turnover, which is the cost this
            // comparison is partly meant to expose. Clamped to the name's own cap so the request is one
            // the guardrails will actually allow — an equal weight that cannot be equal is still worth
            // measuring, but only if it invests what it is permitted rather than nothing.
            var shortfall = Math.Min(target - context.HeldValue(symbol), context.PositionHeadroom(symbol));
            var shares = context.SharesFor(symbol, shortfall);
            if (shares > 0)
            {
                yield return new OrderRequest(symbol, OrderSide.Buy, shares, quote.Mid);
            }
        }
    }
}

/// <summary>
/// Time-series momentum: hold the index while its trailing return is positive, otherwise hold cash.
///
/// The method with the longest evidence base of anything active — AQR traced it to 1880 and found
/// positive average returns in every decade since. Two honest caveats. Its documented strength comes
/// from diversification across dozens of futures markets, not from one equity index, so this is a thin
/// version of it. And roughly half of published anomaly alpha decays after publication, which this has
/// had a century to do.
/// </summary>
public sealed class TrendFollowingBaseline(string symbol = "SPY", int lookbackSessions = 252)
    : IBaselineStrategy
{
    public string Name => $"trend following {symbol} ({lookbackSessions}d)";

    public IEnumerable<OrderRequest> Decide(BaselineContext context)
    {
        if (!context.Market.IsFirstSessionOfMonth(context.Today))
        {
            yield break;
        }

        var closes = context.Market.ClosesUpTo(symbol, context.Today, lookbackSessions);

        // Not enough history yet. Sitting out is the honest response to missing data rather than
        // guessing a trend from a short window.
        if (closes.Count < lookbackSessions || !context.Quotes.TryGetValue(symbol, out var quote))
        {
            yield break;
        }

        var trendIsUp = closes[^1] > closes[0];
        var held = context.HeldShares(symbol);

        if (trendIsUp)
        {
            var shares = context.SharesFor(symbol, context.InvestedCap - context.HeldValue(symbol));
            if (shares > 0)
            {
                yield return new OrderRequest(symbol, OrderSide.Buy, shares, quote.Mid);
            }
        }
        else if (held > 0)
        {
            yield return new OrderRequest(symbol, OrderSide.Sell, held, quote.Mid);
        }
    }
}

/// <summary>
/// Volatility targeting: scale index exposure inversely to recent realised volatility.
///
/// Included because it is contested rather than because it is established. The original paper reported
/// a Sharpe improvement of about 0.15, but a replication across 103 equity portfolios found the managed
/// version ahead in 53 cases against 50, with only eight differences statistically significant. Testing
/// it here is how that coin-flip gets a number on this specific window instead of an opinion.
/// </summary>
public sealed class VolatilityTargetedBaseline(
    string symbol = "SPY",
    double targetAnnualVolPercent = 15,
    int lookbackSessions = 20) : IBaselineStrategy
{
    public string Name => $"vol-targeted {symbol} ({targetAnnualVolPercent:0}% target)";

    public IEnumerable<OrderRequest> Decide(BaselineContext context)
    {
        if (!context.Market.IsFirstSessionOfMonth(context.Today))
        {
            yield break;
        }

        var closes = context.Market.ClosesUpTo(symbol, context.Today, lookbackSessions + 1);
        if (closes.Count < lookbackSessions + 1 || !context.Quotes.TryGetValue(symbol, out var quote))
        {
            yield break;
        }

        var realised = AnnualisedVolatilityPercent(closes);
        if (realised <= 0)
        {
            yield break;
        }

        // Exposure is never levered above the invested cap, whatever the volatility signal says,
        // because borrowing is banned everywhere else in this system too.
        var scale = Math.Min(1.0, targetAnnualVolPercent / realised);
        var target = context.InvestedCap * (decimal)scale;
        var held = context.HeldValue(symbol);

        if (target > held)
        {
            var shares = context.SharesFor(symbol, target - held);
            if (shares > 0)
            {
                yield return new OrderRequest(symbol, OrderSide.Buy, shares, quote.Mid);
            }
        }
        else if (held - target > context.Account.Equity * 0.02m)
        {
            // A 2% deadband, so a small drift in volatility does not generate a trade every month.
            // Turnover is the cost these baselines are meant to measure honestly, not incur blindly.
            var excessShares = context.SharesFor(symbol, held - target);
            var sellable = Math.Min(excessShares, context.HeldShares(symbol));
            if (sellable > 0)
            {
                yield return new OrderRequest(symbol, OrderSide.Sell, sellable, quote.Mid);
            }
        }
    }

    /// <summary>Annualised standard deviation of daily log returns, as a percent.</summary>
    internal static double AnnualisedVolatilityPercent(IReadOnlyList<decimal> closes)
    {
        if (closes.Count < 3)
        {
            return 0;
        }

        var returns = new List<double>(closes.Count - 1);
        for (var i = 1; i < closes.Count; i++)
        {
            if (closes[i - 1] > 0 && closes[i] > 0)
            {
                returns.Add(Math.Log((double)(closes[i] / closes[i - 1])));
            }
        }

        if (returns.Count < 2)
        {
            return 0;
        }

        var mean = returns.Average();
        var variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1);
        return Math.Sqrt(variance) * Math.Sqrt(252) * 100;
    }
}

/// <summary>
/// Hold the index, but sell everything when it falls a set percentage from its peak, then buy back in
/// after a waiting period.
///
/// This is the rule people ask for by name — "if it drops ten percent, get me out" — and it is worth
/// implementing precisely rather than arguing about, because it is testable. Two details decide whether
/// it helps or hurts, and both are usually left unstated when the idea is described.
///
/// The trigger is measured from the peak since entry, not from the purchase price, so a position that
/// has risen and then given back the gain triggers the same as one that fell immediately. That is what
/// "down ten percent" means to someone watching a chart.
///
/// The re-entry rule is the part that matters most and the part nobody specifies. A stop with no way
/// back is not risk management, it is a one-way exit to cash. The waiting period here is deliberate and
/// its length is the whole experiment: too short and the position is sold and rebought inside the same
/// dip, too long and the recovery is missed.
/// </summary>
public sealed class StopLossBaseline(
    string symbol = "SPY",
    decimal stopPercent = 10m,
    int reentryWaitSessions = 20) : IBaselineStrategy
{
    private decimal _peak;
    private int _waitRemaining;
    private int _stopsTriggered;

    public string Name => $"stop-loss {stopPercent:0}% on {symbol}, back in after {reentryWaitSessions}d";

    /// <summary>How often the stop fired, which is the cost side of the trade-off.</summary>
    public int StopsTriggered => _stopsTriggered;

    public IEnumerable<OrderRequest> Decide(BaselineContext context)
    {
        if (!context.Quotes.TryGetValue(symbol, out var quote) || quote.Mid <= 0)
        {
            yield break;
        }

        var held = context.HeldShares(symbol);
        var price = quote.Mid;

        if (held > 0)
        {
            if (price > _peak)
            {
                _peak = price;
            }

            var fallFromPeak = _peak <= 0 ? 0m : (_peak - price) / _peak * 100m;
            if (fallFromPeak >= stopPercent)
            {
                _stopsTriggered++;
                _waitRemaining = reentryWaitSessions;
                _peak = 0m;
                yield return new OrderRequest(symbol, OrderSide.Sell, held, price);
            }

            yield break;
        }

        // Out of the market: count down the wait, then buy back in with everything available.
        if (_waitRemaining > 0)
        {
            _waitRemaining--;
            yield break;
        }

        var shares = context.SharesFor(symbol, context.PositionHeadroom(symbol));
        if (shares > 0)
        {
            _peak = price;
            yield return new OrderRequest(symbol, OrderSide.Buy, shares, price);
        }
    }
}

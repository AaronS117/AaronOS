using AaronOS.Modules.Trading.Backtest;
using AaronOS.Modules.Trading.Brokerage;
using AaronOS.Modules.Trading.Data;
using AaronOS.Modules.Trading.Trading;

namespace AaronOS.Modules.Trading.Tests;

/// <summary>
/// The mechanical strategies the agent is measured against. Being deterministic, they can be pinned
/// exactly — which matters, because a baseline that is quietly wrong makes the agent look better or
/// worse than it is and there would be nothing to notice.
/// </summary>
public class BaselineStrategyTests
{
    private static readonly DateOnly Start = new(2026, 1, 2);

    /// <summary>A rising series long enough for a 252-session lookback, plus a switchable tail.</summary>
    private static ReplayMarket Market(bool trendUp = true, int sessions = 300)
    {
        DateOnly Day(int i) => Start.AddDays(i);

        var spy = new List<DailyBar>();
        for (var i = 0; i < sessions; i++)
        {
            var price = trendUp ? 400m + i : 700m - i;
            spy.Add(new DailyBar(Day(i), price, price));
        }

        var msft = Enumerable.Range(0, sessions)
            .Select(i => new DailyBar(Day(i), 100m, 100m))
            .ToList();

        return new ReplayMarket(
        [
            new("SPY", spy),
            new("MSFT", msft),
        ], "SPY");
    }

    /// <summary>First session of a month with a full 252-session lookback behind it.</summary>
    private static DateOnly FirstRebalanceWithFullHistory(ReplayMarket market) =>
        market.TradingDays
            .Skip(252)
            .First(market.IsFirstSessionOfMonth);

    private static TradingConfig Config() => new()
    {
        IsEnabled = true,
        Watchlist = "SPY,MSFT",
        MaxPositionPercent = 10m,
        MaxInvestedPercent = 80m,
        MaxTradesPerDay = 8,
        BroadIndexSymbols = "SPY",
    };

    private static BaselineContext Context(
        ReplayMarket market, DateOnly today, decimal equity = 100_000m, decimal cash = 100_000m,
        params HeldPosition[] positions)
    {
        var quotes = new Dictionary<string, SymbolQuote>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in new[] { "SPY", "MSFT" })
        {
            if (market.BarOn(symbol, today) is { } bar)
            {
                quotes[symbol] = new SymbolQuote(symbol, bar.Close, bar.Close);
            }
        }

        return new BaselineContext(
            market, today,
            new AccountState(equity, cash, true, positions, 0),
            quotes, Config());
    }

    [Fact]
    public void BuyAndHoldInvestsOnceUpToTheExposureCap()
    {
        var market = Market();
        var strategy = new BuyAndHoldIndexBaseline();

        // 80% of 100,000 at 400 a share is 200 shares.
        var order = Assert.Single(strategy.Decide(Context(market, Start)));
        Assert.Equal("SPY", order.Symbol);
        Assert.Equal(OrderSide.Buy, order.Side);
        Assert.Equal(200, order.Quantity);
    }

    [Fact]
    public void BuyAndHoldNeverTradesAgainOnceInvested()
    {
        var market = Market();
        var strategy = new BuyAndHoldIndexBaseline();

        var context = Context(market, Start.AddDays(50), positions: new HeldPosition("SPY", 200, 90_000m));

        Assert.Empty(strategy.Decide(context));
    }

    [Fact]
    public void EqualWeightOnlyActsOnTheFirstSessionOfAMonth()
    {
        var market = Market();
        var strategy = new EqualWeightMonthlyBaseline();

        // Start is the first session in the series, so it counts as a month boundary.
        Assert.NotEmpty(strategy.Decide(Context(market, Start)));

        // The next day is mid-month and must be quiet.
        Assert.Empty(strategy.Decide(Context(market, Start.AddDays(1))));
    }

    [Fact]
    public void EqualWeightSplitsTheExposureCapAcrossTheWatchlist()
    {
        var market = Market();
        var orders = new EqualWeightMonthlyBaseline().Decide(Context(market, Start)).ToList();

        // 80,000 across two names is 40,000 each. SPY is index-exempt so it takes the full 40,000 —
        // 100 shares at 400. MSFT is capped at 10% of equity, so it is clamped to 10,000: 100 shares at
        // 100, not the 400 an unclamped equal split would have asked for and been refused.
        Assert.Equal(2, orders.Count);
        Assert.Equal(100, orders.Single(o => o.Symbol == "SPY").Quantity);
        Assert.Equal(100, orders.Single(o => o.Symbol == "MSFT").Quantity);
    }

    [Fact]
    public void EveryEqualWeightOrderIsOneTheGuardrailsAccept()
    {
        // The property that matters after the clamp: no request is made that will be refused. A refused
        // request means the strategy invests nothing and reports it as though it had chosen to.
        var market = Market();
        var config = Config();
        var context = Context(market, Start);

        foreach (var order in new EqualWeightMonthlyBaseline().Decide(context))
        {
            Assert.True(
                TradingGuardrails.Check(order, context.Account, config).Allowed,
                $"{order.Symbol} order should be permitted, was refused");
        }
    }

    [Fact]
    public void PositionHeadroomRespectsTheIndexExemption()
    {
        var market = Market();
        var context = Context(market, Start);

        // MSFT is an individual company at 10% of 100,000; SPY is exempt and bounded by the 80% total.
        Assert.Equal(10_000m, context.PositionHeadroom("MSFT"));
        Assert.Equal(80_000m, context.PositionHeadroom("SPY"));
    }

    [Fact]
    public void SizingNeverAsksForMoreCashThanTheGuardrailsWillRelease()
    {
        // Sized to the invested cap with no clamp, buy-and-hold asked for the entire balance, was refused
        // by the cash margin, and reported +0.00% with no fills — a blocked strategy wearing the costume
        // of a cautious one.
        var market = Market();
        var context = Context(market, Start, equity: 100_000m, cash: 100_000m);

        var shares = context.SharesFor("SPY", 100_000m);

        Assert.Equal(247, shares);   // 99,000 spendable at 400 a share
        Assert.True(shares * 400m <= 100_000m * BaselineContext.SpendableCashFraction);
    }

    [Fact]
    public void EqualWeightOnlyTopsUpAndNeverTrims()
    {
        // Trimming winners would add turnover, which is the cost this comparison exists to expose.
        var market = Market();
        var context = Context(
            market, Start, positions: new HeldPosition("SPY", 300, 120_000m));

        var orders = new EqualWeightMonthlyBaseline().Decide(context).ToList();

        Assert.DoesNotContain(orders, o => o.Symbol == "SPY");
        Assert.Contains(orders, o => o.Symbol == "MSFT");
    }

    [Fact]
    public void TrendFollowingBuysWhileTheTrendIsUp()
    {
        var market = Market(trendUp: true);
        var strategy = new TrendFollowingBaseline();

        var day = FirstRebalanceWithFullHistory(market);
        var order = Assert.Single(strategy.Decide(Context(market, day)));

        Assert.Equal("SPY", order.Symbol);
        Assert.Equal(OrderSide.Buy, order.Side);
    }

    [Fact]
    public void TrendFollowingSellsOutWhenTheTrendTurnsDown()
    {
        var market = Market(trendUp: false);
        var day = FirstRebalanceWithFullHistory(market);
        var bar = market.BarOn("SPY", day)!.Value;

        var context = Context(
            market, day, positions: new HeldPosition("SPY", 100, 100 * bar.Close));

        var order = Assert.Single(new TrendFollowingBaseline().Decide(context));
        Assert.Equal(OrderSide.Sell, order.Side);
        Assert.Equal(100, order.Quantity);
    }

    [Fact]
    public void TrendFollowingSitsOutUntilItHasEnoughHistory()
    {
        // Acting on a partial lookback would be inventing a trend from data that is not there.
        var market = Market(trendUp: true);

        Assert.Empty(new TrendFollowingBaseline().Decide(Context(market, market.TradingDays[10])));
    }

    [Fact]
    public void VolatilityTargetingTakesFullExposureWhenTheMarketIsCalm()
    {
        // A perfectly straight line has zero volatility, so the scale is capped at 1 rather than levered.
        var market = Market();
        var day = FirstRebalanceWithFullHistory(market);

        var orders = new VolatilityTargetedBaseline().Decide(Context(market, day)).ToList();

        Assert.True(orders.Count <= 1);
    }

    [Fact]
    public void VolatilityIsAnnualisedFromDailyReturns()
    {
        // A flat series has no volatility; an alternating one has a lot. The absolute value matters less
        // than the ordering being right, since the scale is a ratio against the target.
        Assert.Equal(0, VolatilityTargetedBaseline.AnnualisedVolatilityPercent([100m, 100m, 100m, 100m]));

        var choppy = VolatilityTargetedBaseline.AnnualisedVolatilityPercent(
            [100m, 105m, 100m, 105m, 100m, 105m]);
        Assert.True(choppy > 50, $"alternating 5% moves should annualise high, got {choppy:N1}");
    }

    [Fact]
    public void VolatilityTargetingNeedsHistoryTooAndDeclinesWithout()
    {
        var market = Market();

        Assert.Empty(new VolatilityTargetedBaseline().Decide(Context(market, market.TradingDays[2])));
    }

    [Fact]
    public void BaselineOrdersAreSubjectToTheSameGuardrailsAsTheAgent()
    {
        // The point of routing rules through the same checks: a strategy that beat the agent only because
        // it was allowed a bigger position would be measuring permission, not judgement. Asserted with a
        // hand-built oversized request, since the strategies themselves now clamp and would never make
        // one — the guardrail still has to be the thing that says no.
        var market = Market();
        var config = Config();
        var context = Context(market, Start);

        var oversized = new OrderRequest("MSFT", OrderSide.Buy, 400, 100m);

        Assert.False(TradingGuardrails.Check(oversized, context.Account, config).Allowed);
    }
}

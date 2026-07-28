using AaronOS.Modules.Trading.Backtest;
using AaronOS.Modules.Trading.Brokerage;
using AaronOS.Modules.Trading.Data;

namespace AaronOS.Modules.Trading.Tests;

/// <summary>
/// The simulated fill is where a backtest lies if it is going to. Two mistakes account for most
/// overstated results — filling on the bar the decision was made from, and charging nothing to trade —
/// so both are pinned here rather than trusted.
/// </summary>
public class ReplayBrokerTests
{
    private static readonly DateOnly Day1 = new(2026, 3, 2);
    private static readonly DateOnly Day2 = new(2026, 3, 3);
    private static readonly DateOnly Day3 = new(2026, 3, 4);

    private static ReplayMarket Market() => new(
    [
        new("SPY", [
            new DailyBar(Day1, 500m, 505m),
            new DailyBar(Day2, 506m, 510m),
            new DailyBar(Day3, 511m, 520m)]),
        new("MSFT", [
            new DailyBar(Day1, 100m, 110m),
            new DailyBar(Day2, 200m, 210m),
            new DailyBar(Day3, 300m, 310m)]),
    ], "SPY");

    /// <summary>No spread and no slippage, for tests about mechanics rather than costs.</summary>
    private static ReplayCosts Free => new(0m, 0m);

    private static ReplayBroker Broker(ReplayCosts? costs = null, decimal cash = 100_000m) =>
        new(Market(), cash, costs ?? Free) { Today = Day1 };

    [Fact]
    public async Task AFillUsesTheNextSessionsOpenNotTheCloseTheDecisionSaw()
    {
        // MSFT closes at 110 on day 1 and opens at 200 on day 2. Filling at 110 would hand the trade a
        // price only knowable because the session had already ended — lookahead in disguise.
        var broker = Broker();

        await broker.PlaceMarketOrderAsync("MSFT", OrderSide.Buy, 10);

        Assert.Equal(200m, Assert.Single(broker.Fills).Price);
    }

    [Fact]
    public async Task OnTheFinalSessionThereIsNoTomorrowSoTheCloseIsUsed()
    {
        var broker = Broker();
        broker.Today = Day3;

        await broker.PlaceMarketOrderAsync("MSFT", OrderSide.Buy, 1);

        Assert.Equal(310m, Assert.Single(broker.Fills).Price);
    }

    [Fact]
    public async Task BuyingPaysSlippageAndSellingReceivesLess()
    {
        // Costs must always work against the trade, never for it.
        var costs = new ReplayCosts(HalfSpreadBps: 0m, SlippageBps: 50m);

        var buyer = new ReplayBroker(Market(), 100_000m, costs) { Today = Day1 };
        await buyer.PlaceMarketOrderAsync("MSFT", OrderSide.Buy, 10);
        Assert.Equal(201m, buyer.Fills[^1].Price);

        await buyer.PlaceMarketOrderAsync("MSFT", OrderSide.Sell, 10);
        Assert.Equal(199m, buyer.Fills[^1].Price);
    }

    [Fact]
    public async Task TradingCostsMoneyEvenWhenThePriceDoesNotMove()
    {
        // A flat market with churn must lose. This is the property a frictionless replay destroys, and
        // it matters most for an agent whose failure mode is trading too often.
        var flat = new ReplayMarket(
        [
            new("SPY", [new DailyBar(Day1, 500m, 500m), new DailyBar(Day2, 500m, 500m)]),
            new("MSFT", [new DailyBar(Day1, 100m, 100m), new DailyBar(Day2, 100m, 100m)]),
        ], "SPY");

        var broker = new ReplayBroker(flat, 100_000m, new ReplayCosts(2m, 3m)) { Today = Day1 };

        await broker.PlaceMarketOrderAsync("MSFT", OrderSide.Buy, 100);
        await broker.PlaceMarketOrderAsync("MSFT", OrderSide.Sell, 100);

        Assert.True(broker.Cash < 100_000m, $"round trip should cost money, cash is {broker.Cash}");
    }

    [Fact]
    public async Task EquityCountsHoldingsAtTheSessionsClose()
    {
        var broker = Broker();
        await broker.PlaceMarketOrderAsync("MSFT", OrderSide.Buy, 10);   // fills at 200 on day 2's open

        broker.Today = Day2;

        // 10 shares valued at day 2's close of 210, plus the cash that survived a 2,000 purchase.
        Assert.Equal(98_000m, broker.Cash);
        Assert.Equal(98_000m + 2_100m, broker.Equity);
    }

    [Fact]
    public async Task ShortingIsNotSimulatedAtAll()
    {
        var broker = Broker();

        var error = await Assert.ThrowsAsync<AlpacaApiException>(
            () => broker.PlaceMarketOrderAsync("MSFT", OrderSide.Sell, 5));

        Assert.Contains("shorting is not simulated", error.Message);
    }

    [Fact]
    public async Task SellingMoreThanIsHeldIsRefused()
    {
        var broker = Broker();
        await broker.PlaceMarketOrderAsync("MSFT", OrderSide.Buy, 5);

        await Assert.ThrowsAsync<AlpacaApiException>(
            () => broker.PlaceMarketOrderAsync("MSFT", OrderSide.Sell, 6));
    }

    [Fact]
    public async Task TheLedgerRefusesToInventCashEvenIfAGuardrailWereWrong()
    {
        // A deliberate duplicate of a guardrail. If the cap logic were ever broken, a replay must still
        // not be able to spend money it does not have and report the result as a gain.
        var broker = Broker(cash: 500m);

        var error = await Assert.ThrowsAsync<AlpacaApiException>(
            () => broker.PlaceMarketOrderAsync("MSFT", OrderSide.Buy, 10));

        Assert.Contains("Insufficient simulated cash", error.Message);
    }

    [Fact]
    public async Task QuotesStraddleTheCloseBySpread()
    {
        var broker = new ReplayBroker(Market(), 100_000m, new ReplayCosts(10m, 0m)) { Today = Day1 };

        var quotes = await broker.GetQuotesAsync(["MSFT"]);

        var quote = quotes["MSFT"];
        Assert.Equal(109.89m, Math.Round(quote.Bid, 2));
        Assert.Equal(110.11m, Math.Round(quote.Ask, 2));
        Assert.Equal(110m, Math.Round(quote.Mid, 2));
    }

    [Fact]
    public async Task ASymbolWithNoBarThatDayIsAbsentRatherThanFree()
    {
        var broker = Broker();

        var quotes = await broker.GetQuotesAsync(["MSFT", "NOSUCH"]);

        Assert.True(quotes.ContainsKey("MSFT"));
        Assert.False(quotes.ContainsKey("NOSUCH"));
    }

    [Fact]
    public void TradingDaysComeFromTheBenchmarksOwnBars()
    {
        // Derived from data rather than from a holiday calendar, so sessions are guaranteed consistent
        // with the prices being replayed.
        Assert.Equal([Day1, Day2, Day3], Market().TradingDays);
        Assert.Equal([Day2, Day3], Market().DaysBetween(Day2, Day3));
    }

    [Fact]
    public void AMarketWithoutBenchmarkBarsIsRejected()
    {
        // Without the benchmark there is no comparison, and the comparison is the entire point.
        Assert.Throws<ArgumentException>(() => new ReplayMarket(
            [new("MSFT", [new DailyBar(Day1, 100m, 110m)])], "SPY"));
    }

    [Fact]
    public void TheReplayClockReportsTheDateItWasGiven()
    {
        var clock = new ReplayTimeProvider(Day1);
        Assert.Equal(Day1, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));

        clock.SetDate(Day3);
        Assert.Equal(Day3, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));

        // Local and UTC must agree on the date whatever the machine's offset, or a snapshot written by
        // local date and an order counted by UTC date would land on different days.
        Assert.Equal(
            DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            DateOnly.FromDateTime(clock.GetLocalNow().DateTime));
    }
}

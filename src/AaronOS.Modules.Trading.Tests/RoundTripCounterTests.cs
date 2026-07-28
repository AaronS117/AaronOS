using AaronOS.Modules.Trading.Data;
using AaronOS.Modules.Trading.Trading;

namespace AaronOS.Modules.Trading.Tests;

public class RoundTripCounterTests
{
    private static TradeOrder Fill(string symbol, OrderSide side, int quantity, decimal price, int day) =>
        new()
        {
            Symbol = symbol,
            Side = side,
            Quantity = quantity,
            FilledPrice = price,
            FilledAtUtc = new DateTime(2026, 3, day, 15, 0, 0, DateTimeKind.Utc),
            Status = "filled",
        };

    [Fact]
    public void ABuyThenAProfitableSellIsOneWin()
    {
        var (closed, wins) = RoundTripCounter.Count(
        [
            Fill("AAPL", OrderSide.Buy, 10, 100m, 1),
            Fill("AAPL", OrderSide.Sell, 10, 120m, 2),
        ]);

        Assert.Equal(1, closed);
        Assert.Equal(1, wins);
    }

    [Fact]
    public void ALosingRoundTripCountsAsClosedButNotAsAWin()
    {
        var (closed, wins) = RoundTripCounter.Count(
        [
            Fill("AAPL", OrderSide.Buy, 10, 100m, 1),
            Fill("AAPL", OrderSide.Sell, 10, 80m, 2),
        ]);

        Assert.Equal(1, closed);
        Assert.Equal(0, wins);
    }

    [Fact]
    public void AnOpenPositionIsNotCounted()
    {
        // The failure this prevents: counting unrealised gains as wins while losers stay open.
        var (closed, wins) = RoundTripCounter.Count([Fill("AAPL", OrderSide.Buy, 10, 100m, 1)]);

        Assert.Equal(0, closed);
        Assert.Equal(0, wins);
    }

    [Fact]
    public void LotsAreMatchedFirstInFirstOut()
    {
        // Two buys at 100 then 200; selling ten at 150 closes the cheaper lot, which is a win.
        var (closed, wins) = RoundTripCounter.Count(
        [
            Fill("AAPL", OrderSide.Buy, 10, 100m, 1),
            Fill("AAPL", OrderSide.Buy, 10, 200m, 2),
            Fill("AAPL", OrderSide.Sell, 10, 150m, 3),
        ]);

        Assert.Equal(1, closed);
        Assert.Equal(1, wins);
    }

    [Fact]
    public void APartialSaleLeavesTheRestOfTheLotOpen()
    {
        var (closed, wins) = RoundTripCounter.Count(
        [
            Fill("AAPL", OrderSide.Buy, 10, 100m, 1),
            Fill("AAPL", OrderSide.Sell, 4, 120m, 2),
            Fill("AAPL", OrderSide.Sell, 6, 90m, 3),
        ]);

        Assert.Equal(2, closed);
        Assert.Equal(1, wins);
    }

    [Fact]
    public void SymbolsAreCountedIndependently()
    {
        var (closed, wins) = RoundTripCounter.Count(
        [
            Fill("AAPL", OrderSide.Buy, 10, 100m, 1),
            Fill("MSFT", OrderSide.Buy, 10, 300m, 1),
            Fill("AAPL", OrderSide.Sell, 10, 110m, 2),
            Fill("MSFT", OrderSide.Sell, 10, 280m, 2),
        ]);

        Assert.Equal(2, closed);
        Assert.Equal(1, wins);
    }

    [Fact]
    public void UnfilledOrdersAreIgnored()
    {
        var pending = Fill("AAPL", OrderSide.Sell, 10, 120m, 2);
        pending.Status = "new";
        pending.FilledPrice = null;

        var (closed, _) = RoundTripCounter.Count([Fill("AAPL", OrderSide.Buy, 10, 100m, 1), pending]);

        Assert.Equal(0, closed);
    }

    [Fact]
    public void ASellWithNothingHeldIsIgnoredRatherThanCounted()
    {
        // The guardrails prevent this, but a stale or manually placed order should not corrupt
        // the record if one ever appears.
        var (closed, wins) = RoundTripCounter.Count([Fill("AAPL", OrderSide.Sell, 10, 120m, 1)]);

        Assert.Equal(0, closed);
        Assert.Equal(0, wins);
    }
}

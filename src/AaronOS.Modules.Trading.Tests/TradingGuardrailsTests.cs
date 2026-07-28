using AaronOS.Modules.Trading.Data;
using AaronOS.Modules.Trading.Trading;

namespace AaronOS.Modules.Trading.Tests;

/// <summary>
/// The guardrails are the whole safety argument for letting a model trade unattended, so they are
/// tested harder than anything else here. Each test names a specific way an order could do damage
/// if the rule were missing.
/// </summary>
public class TradingGuardrailsTests
{
    private static TradingConfig Config() => new()
    {
        IsEnabled = true,
        Watchlist = "AAPL,MSFT,SPY",
        MaxPositionPercent = 10m,
        MaxInvestedPercent = 80m,
        MaxTradesPerDay = 6,
    };

    private static AccountState Account(
        decimal equity = 100_000m,
        decimal cash = 100_000m,
        bool open = true,
        IReadOnlyList<HeldPosition>? positions = null,
        int ordersToday = 0) =>
        new(equity, cash, open, positions ?? [], ordersToday);

    private static OrderRequest Buy(string symbol = "AAPL", int quantity = 10, decimal price = 100m) =>
        new(symbol, OrderSide.Buy, quantity, price);

    private static OrderRequest Sell(string symbol = "AAPL", int quantity = 10, decimal price = 100m) =>
        new(symbol, OrderSide.Sell, quantity, price);

    [Fact]
    public void AnOrdinaryBuyIsAllowed()
    {
        Assert.True(TradingGuardrails.Check(Buy(), Account(), Config()).Allowed);
    }

    [Fact]
    public void NothingPassesWhenTradingIsSwitchedOff()
    {
        var config = Config();
        config.IsEnabled = false;

        var verdict = TradingGuardrails.Check(Buy(), Account(), config);

        Assert.False(verdict.Allowed);
        Assert.Contains("switched off", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NothingPassesWhileTheMarketIsClosed()
    {
        var verdict = TradingGuardrails.Check(Buy(), Account(open: false), Config());

        Assert.False(verdict.Allowed);
        Assert.Contains("closed", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASymbolOffTheWatchlistIsRefused()
    {
        // The watchlist is the outer boundary: whatever else goes wrong, the agent cannot buy
        // something nobody chose to expose it to.
        var verdict = TradingGuardrails.Check(Buy("TSLA"), Account(), Config());

        Assert.False(verdict.Allowed);
        Assert.Contains("watchlist", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheWatchlistIgnoresCaseAndSurroundingSpace()
    {
        var config = Config();
        config.Watchlist = " aapl , msft ";

        Assert.True(TradingGuardrails.Check(Buy("AAPL"), Account(), config).Allowed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ANonPositiveQuantityIsRefused(int quantity)
    {
        Assert.False(TradingGuardrails.Check(Buy(quantity: quantity), Account(), Config()).Allowed);
    }

    [Fact]
    public void AnUnpricedOrderIsRefusedRatherThanTreatedAsFree()
    {
        Assert.False(TradingGuardrails.Check(Buy(price: 0m), Account(), Config()).Allowed);
    }

    [Fact]
    public void BuyingBeyondTheCashBalanceIsRefused()
    {
        // No margin. A paper account offers it and a habit formed on paper carries over.
        var verdict = TradingGuardrails.Check(
            Buy(quantity: 100, price: 100m), Account(equity: 100_000m, cash: 5_000m), Config());

        Assert.False(verdict.Allowed);
        Assert.Contains("Borrowing", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnOrderCannotConsumeLiterallyEveryCentOfCash()
    {
        // Sized from a mid price and filled at the next open plus slippage, an order spending the last
        // cent fails at the broker. The margin makes it one share smaller instead of an error.
        //
        // Uses the index symbol with a full invested cap so that cash is the only thing that can bind.
        // On an individual name the 10% per-company cap fires first and the test would prove nothing.
        var config = Config();
        config.MaxInvestedPercent = 100m;
        config.Watchlist = "SPY";
        var account = Account(equity: 10_000m, cash: 10_000m);

        Assert.False(TradingGuardrails.Check(Buy("SPY", 100, 100m), account, config).Allowed);
        Assert.True(TradingGuardrails.Check(Buy("SPY", 98, 100m), account, config).Allowed);
    }

    [Fact]
    public void AFullyInvestedCapLetsTheAgentMatchTheIndexItIsJudgedAgainst()
    {
        // The handicap this removes: at 80% the agent could not reach the benchmark's exposure, so a
        // 2.25-point shortfall was structural rather than a decision. Cash is the real constraint.
        var config = Config();
        config.MaxInvestedPercent = 100m;
        config.Watchlist = "SPY";

        Assert.True(TradingGuardrails.Check(Buy("SPY", 980, 100m), Account(), config).Allowed);
    }

    [Fact]
    public void ASinglePositionCannotExceedItsCap()
    {
        // 10% of 100,000 is 10,000; this order is 15,000.
        var verdict = TradingGuardrails.Check(Buy(quantity: 150, price: 100m), Account(), Config());

        Assert.False(verdict.Allowed);
        Assert.Contains("per-position cap", verdict.Reason);
    }

    [Fact]
    public void TheCapCountsWhatIsAlreadyHeld_SoItCannotBeSteppedPast()
    {
        // The failure this guards against: nine separate orders of 1,500 each pass individually
        // and together take the position to 13,500 against a 10,000 cap.
        var account = Account(positions: [new HeldPosition("AAPL", 90, 9_000m)]);

        var verdict = TradingGuardrails.Check(Buy(quantity: 15, price: 100m), account, Config());

        Assert.False(verdict.Allowed);
        Assert.Contains("per-position cap", verdict.Reason);
    }

    [Fact]
    public void TotalExposureIsCappedSoSomeCashIsAlwaysHeldBack()
    {
        var account = Account(positions:
        [
            new HeldPosition("MSFT", 400, 40_000m),
            new HeldPosition("SPY", 350, 35_000m),
        ]);

        // Each position is inside its own cap; together with this order they breach the 80% total.
        var verdict = TradingGuardrails.Check(Buy(quantity: 90, price: 100m), account, Config());

        Assert.False(verdict.Allowed);
        Assert.Contains("total exposure", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SellingWhatIsNotHeldIsRefused()
    {
        // This is the short-selling ban. A short's loss has no ceiling, which is the one outcome a
        // limit-based system cannot bound after the fact.
        var verdict = TradingGuardrails.Check(Sell(), Account(), Config());

        Assert.False(verdict.Allowed);
        Assert.Contains("short selling", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SellingMoreThanIsHeldIsRefused()
    {
        var account = Account(positions: [new HeldPosition("AAPL", 5, 500m)]);

        var verdict = TradingGuardrails.Check(Sell(quantity: 10), account, Config());

        Assert.False(verdict.Allowed);
        Assert.Contains("only 5 held", verdict.Reason);
    }

    [Fact]
    public void SellingExactlyWhatIsHeldIsAllowed()
    {
        var account = Account(positions: [new HeldPosition("AAPL", 10, 1_000m)]);

        Assert.True(TradingGuardrails.Check(Sell(quantity: 10), account, Config()).Allowed);
    }

    [Fact]
    public void SellingIsNotBlockedByThePositionOrCashCaps()
    {
        // Getting out must never be harder than getting in. A cash-poor, fully invested account is
        // exactly when an exit matters most.
        var account = Account(cash: 0m, positions: [new HeldPosition("AAPL", 500, 50_000m)]);

        Assert.True(TradingGuardrails.Check(Sell(quantity: 500, price: 100m), account, Config()).Allowed);
    }

    [Fact]
    public void TheDailyOrderLimitStopsAChurningLoop()
    {
        var verdict = TradingGuardrails.Check(Buy(), Account(ordersToday: 6), Config());

        Assert.False(verdict.Allowed);
        Assert.Contains("limit is 6", verdict.Reason);
    }

    [Fact]
    public void TheDailyLimitAppliesToSellsToo()
    {
        var account = Account(positions: [new HeldPosition("AAPL", 50, 5_000m)], ordersToday: 6);

        Assert.False(TradingGuardrails.Check(Sell(), account, Config()).Allowed);
    }

    [Fact]
    public void AZeroEquityAccountCannotBuy()
    {
        // Every cap is a percentage of equity, so zero equity means no size can be computed and the
        // percentage checks would otherwise pass trivially.
        var verdict = TradingGuardrails.Check(Buy(), Account(equity: 0m, cash: 1_000m), Config());

        Assert.False(verdict.Allowed);
    }

    [Fact]
    public void ABroadIndexIsExemptFromThePerPositionCap()
    {
        // The per-position cap limits how much rides on one company, and an index fund is not one
        // company. Applying it to SPY would leave the "hold the index when you have no view"
        // instruction able to place only ten percent, which is the cash-drag failure it was written to
        // prevent.
        var config = Config();
        config.Watchlist = "SPY,AAPL";

        // 400 shares at 100 is 40,000 — four times the 10% per-position cap.
        Assert.True(TradingGuardrails.Check(Buy("SPY", 400, 100m), Account(), config).Allowed);
    }

    [Fact]
    public void ABroadIndexIsStillCappedAtTheInvestedLimit()
    {
        // Exempt from one cap, not from all of them. The exemption raises SPY's ceiling from 10% to the
        // 80% invested limit; it does not remove it, so the account always keeps cash back.
        var config = Config();
        config.Watchlist = "SPY";

        Assert.True(TradingGuardrails.Check(Buy("SPY", 800, 100m), Account(), config).Allowed);

        var verdict = TradingGuardrails.Check(Buy("SPY", 810, 100m), Account(), config);
        Assert.False(verdict.Allowed);
        Assert.Contains("80% index-position cap", verdict.Reason);
    }

    [Fact]
    public void AnIndexPositionStillCountsTowardsTotalExposure()
    {
        // The case where the exposure cap does the work the position cap cannot: an index holding plus
        // individual names together exceeding the limit.
        // MSFT stays inside its own 10% cap (6,000 of 10,000) while the book as a whole goes to 81,000
        // against an 80,000 limit, so only the exposure check can catch it.
        var config = Config();
        config.Watchlist = "SPY,MSFT";
        var account = Account(cash: 21_000m, positions:
        [
            new HeldPosition("SPY", 750, 75_000m),
            new HeldPosition("MSFT", 40, 4_000m),
        ]);

        var verdict = TradingGuardrails.Check(Buy("MSFT", 20, 100m), account, config);

        Assert.False(verdict.Allowed);
        Assert.Contains("total exposure", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ABroadIndexIsStillBoundByAvailableCash()
    {
        var config = Config();
        config.Watchlist = "SPY";

        var verdict = TradingGuardrails.Check(
            Buy("SPY", 400, 100m), Account(equity: 100_000m, cash: 1_000m), config);

        Assert.False(verdict.Allowed);
        Assert.Contains("Borrowing", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnOrdinaryStockGetsNoIndexExemption()
    {
        var config = Config();

        var verdict = TradingGuardrails.Check(Buy("AAPL", 400, 100m), Account(), config);

        Assert.False(verdict.Allowed);
        Assert.Contains("per-position cap", verdict.Reason);
    }

    [Fact]
    public void TheIndexListIsConfigurableAndCaseInsensitive()
    {
        var config = Config();
        config.Watchlist = "AAPL";
        config.BroadIndexSymbols = "aapl";

        // Deliberately perverse: whatever is listed is treated as an index, so the exemption is a
        // setting rather than a hardcoded opinion about which tickers are diversified.
        Assert.True(TradingGuardrails.Check(Buy("AAPL", 400, 100m), Account(), config).Allowed);
    }

    [Fact]
    public void ParseWatchlistDeduplicatesAndUppercases()
    {
        Assert.Equal(["AAPL", "MSFT"], TradingGuardrails.ParseWatchlist("aapl, AAPL , msft,"));
    }
}

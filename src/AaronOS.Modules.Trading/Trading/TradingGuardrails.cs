using AaronOS.Modules.Trading.Data;

namespace AaronOS.Modules.Trading.Trading;

/// <summary>A position as the broker currently reports it.</summary>
public readonly record struct HeldPosition(string Symbol, int Quantity, decimal MarketValue);

/// <summary>An order the agent wants to place, before anything has been sent anywhere.</summary>
public readonly record struct OrderRequest(string Symbol, OrderSide Side, int Quantity, decimal EstimatedPrice)
{
    public decimal Notional => Quantity * EstimatedPrice;
}

/// <summary>Everything the limits are checked against.</summary>
public readonly record struct AccountState(
    decimal Equity,
    decimal Cash,
    bool MarketIsOpen,
    IReadOnlyList<HeldPosition> Positions,
    int OrdersPlacedToday);

public readonly record struct GuardrailVerdict(bool Allowed, string Reason)
{
    public static GuardrailVerdict Allow() => new(true, "");
    public static GuardrailVerdict Block(string reason) => new(false, reason);
}

/// <summary>
/// The limits, enforced here rather than requested in a prompt.
///
/// This is the difference between a constraint and a suggestion. A model told to keep positions
/// under ten percent will mostly do so, and the failures are exactly the cases that matter. Every
/// order goes through these checks before it reaches the broker, so the worst a confused or
/// adversarial model can do is have its orders refused and logged.
///
/// Deliberately pure and synchronous: no clock, no network, no database. That makes each rule
/// directly testable, which is the only way to know a risk control actually works before the day it
/// has to.
/// </summary>
public static class TradingGuardrails
{
    public static GuardrailVerdict Check(OrderRequest order, AccountState account, TradingConfig config)
    {
        if (!config.IsEnabled)
        {
            return GuardrailVerdict.Block("Trading is switched off.");
        }

        if (!account.MarketIsOpen)
        {
            return GuardrailVerdict.Block("The market is closed.");
        }

        if (order.Quantity <= 0)
        {
            return GuardrailVerdict.Block($"Quantity must be positive, got {order.Quantity}.");
        }

        if (order.EstimatedPrice <= 0)
        {
            return GuardrailVerdict.Block($"No usable price for {order.Symbol}.");
        }

        if (!IsOnWatchlist(order.Symbol, config.Watchlist))
        {
            return GuardrailVerdict.Block($"{order.Symbol} is not on the watchlist.");
        }

        if (account.OrdersPlacedToday >= config.MaxTradesPerDay)
        {
            return GuardrailVerdict.Block(
                $"Already placed {account.OrdersPlacedToday} orders today, the limit is {config.MaxTradesPerDay}.");
        }

        return order.Side == OrderSide.Buy
            ? CheckBuy(order, account, config)
            : CheckSell(order, account);
    }

    private static GuardrailVerdict CheckBuy(OrderRequest order, AccountState account, TradingConfig config)
    {
        if (account.Equity <= 0)
        {
            return GuardrailVerdict.Block("Account equity is zero, so no position size can be computed.");
        }

        // Measured against slightly less than the full cash balance. The order is sized from a mid
        // price and fills at the next open plus slippage, so one sized to the last cent will fail at
        // the broker. A small margin turns that into an order one share smaller rather than an error,
        // and it costs a fraction of cash rather than a fraction of equity.
        var spendable = account.Cash * 0.99m;
        if (order.Notional > spendable)
        {
            return GuardrailVerdict.Block(
                $"{order.Notional:C0} exceeds the {spendable:C0} of cash available to spend. " +
                $"Borrowing is not permitted.");
        }

        // Measured against the resulting position, not this order alone, so a cap cannot be
        // stepped past by placing the same order repeatedly.
        var existing = account.Positions.FirstOrDefault(p =>
            string.Equals(p.Symbol, order.Symbol, StringComparison.OrdinalIgnoreCase));
        var resulting = existing.MarketValue + order.Notional;

        // A broad index fund is exempt from the per-position cap because that cap limits exposure to a
        // single company, which an index fund is not. It remains bound by the total exposure cap and by
        // cash below, so the account still cannot be fully invested or borrow.
        var isBroadIndex = IsBroadIndex(order.Symbol, config.BroadIndexSymbols);
        var positionCap = account.Equity *
            (isBroadIndex ? config.MaxInvestedPercent : config.MaxPositionPercent) / 100m;

        if (resulting > positionCap)
        {
            var capPercent = isBroadIndex ? config.MaxInvestedPercent : config.MaxPositionPercent;
            var capName = isBroadIndex ? "index-position" : "per-position";
            return GuardrailVerdict.Block(
                $"Would take {order.Symbol} to {resulting:C0}, above the {capPercent:0.#}% " +
                $"{capName} cap of {positionCap:C0}.");
        }

        var invested = account.Positions.Sum(p => p.MarketValue) + order.Notional;
        var investedCap = account.Equity * config.MaxInvestedPercent / 100m;
        if (invested > investedCap)
        {
            return GuardrailVerdict.Block(
                $"Would take total exposure to {invested:C0}, above the {config.MaxInvestedPercent:0.#}% " +
                $"cap of {investedCap:C0}.");
        }

        return GuardrailVerdict.Allow();
    }

    private static GuardrailVerdict CheckSell(OrderRequest order, AccountState account)
    {
        var held = account.Positions
            .FirstOrDefault(p => string.Equals(p.Symbol, order.Symbol, StringComparison.OrdinalIgnoreCase))
            .Quantity;

        // A sell is only ever a way out of something already owned. Allowing more would open a
        // short position, whose loss has no upper bound.
        if (held <= 0)
        {
            return GuardrailVerdict.Block($"No {order.Symbol} held, and short selling is not permitted.");
        }

        return order.Quantity > held
            ? GuardrailVerdict.Block($"Cannot sell {order.Quantity} {order.Symbol}, only {held} held.")
            : GuardrailVerdict.Allow();
    }

    public static bool IsBroadIndex(string symbol, string broadIndexSymbols) =>
        ParseWatchlist(broadIndexSymbols).Contains(symbol.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool IsOnWatchlist(string symbol, string watchlist) =>
        ParseWatchlist(watchlist).Contains(symbol.Trim(), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> ParseWatchlist(string watchlist) =>
        watchlist.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .Distinct()
            .ToList();
}

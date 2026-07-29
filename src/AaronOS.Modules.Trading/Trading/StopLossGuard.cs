using AaronOS.Core.Data;
using AaronOS.Modules.Trading.Brokerage;
using AaronOS.Modules.Trading.Data;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Trading.Trading;

/// <summary>One position sold because it fell too far from its peak.</summary>
public readonly record struct StopLossSale(string Symbol, int Quantity, decimal Peak, decimal Price)
{
    public decimal FallPercent => Peak <= 0 ? 0 : (Peak - Price) / Peak * 100m;

    public override string ToString() =>
        $"{Symbol} fell {FallPercent:0.0}% from {Peak:C2} to {Price:C2} — sold {Quantity}";
}

/// <summary>
/// Sells any position that has fallen a set percentage below its peak, before the model is consulted.
///
/// Deliberately mechanical and deliberately first. The point of a stop, for someone who does not want to
/// watch a position fall, is that it does not depend on anyone — including a language model — noticing
/// and deciding correctly on the day. It runs whether the model is having a good cycle or a bad one, and
/// it runs before the model can talk itself out of it.
///
/// Measured from the peak since entry rather than from the purchase price, because what people mean by
/// "don't let it drop ten percent" is usually about protecting a gain, not only about limiting a loss.
///
/// Worth being honest about the cost, which a six-year replay put a number on: a 7% trailing stop cut
/// the worst peak-to-trough fall from 24% to 15%, and cost about 21 points of return to do it. It buys a
/// smoother ride, not more money. It is worth having anyway if the alternative is selling in a panic at
/// the bottom, because a rule that fires identically every time beats a decision made while frightened.
/// </summary>
public class StopLossGuard(IDbContextFactory<AaronOsDbContext> dbContextFactory, AlpacaClient alpaca)
{
    public async Task<IReadOnlyList<StopLossSale>> ApplyAsync(
        TradingConfig config, IReadOnlyList<HeldPosition> positions,
        Dictionary<string, SymbolQuote> quotes, CancellationToken token = default)
    {
        if (config.StopLossPercent <= 0)
        {
            return [];
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var peaks = await db.Set<PositionPeak>().ToDictionaryAsync(
            p => p.Symbol, StringComparer.OrdinalIgnoreCase, token);

        var sales = new List<StopLossSale>();
        var heldSymbols = positions.Where(p => p.Quantity > 0)
            .Select(p => p.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A peak for something no longer held would make a future re-entry trail from an old high.
        foreach (var stale in peaks.Values.Where(p => !heldSymbols.Contains(p.Symbol)).ToList())
        {
            db.Remove(stale);
            peaks.Remove(stale.Symbol);
        }

        foreach (var position in positions.Where(p => p.Quantity > 0))
        {
            if (!quotes.TryGetValue(position.Symbol, out var quote) || quote.Mid <= 0)
            {
                continue;
            }

            var price = quote.Mid;
            if (!peaks.TryGetValue(position.Symbol, out var peak))
            {
                peak = new PositionPeak { Symbol = position.Symbol, PeakPrice = price };
                db.Add(peak);
                peaks[position.Symbol] = peak;
            }

            if (price > peak.PeakPrice)
            {
                peak.PeakPrice = price;
            }

            peak.UpdatedUtc = DateTime.UtcNow;

            var fall = peak.PeakPrice <= 0 ? 0m : (peak.PeakPrice - price) / peak.PeakPrice * 100m;
            if (fall < config.StopLossPercent)
            {
                continue;
            }

            // Sold at the market, whole position, no partial exits — a stop that leaves some on is not
            // the thing that was asked for.
            await alpaca.PlaceMarketOrderAsync(position.Symbol, OrderSide.Sell, position.Quantity, token);
            sales.Add(new StopLossSale(position.Symbol, position.Quantity, peak.PeakPrice, price));

            db.Remove(peak);
            peaks.Remove(position.Symbol);
        }

        await db.SaveChangesAsync(token);
        return sales;
    }
}

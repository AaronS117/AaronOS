using AaronOS.Modules.Trading.Data;

namespace AaronOS.Modules.Trading.Trading;

/// <summary>
/// Pairs buys with the sells that closed them, first in first out, to count completed round trips
/// and how many made money.
///
/// Only filled orders count, and an open position is deliberately excluded from both totals. Marking
/// a holding to its current price and calling the paper gain a "win" is the easiest way to flatter a
/// record: the losers stay open and the winners get counted. A trade is a result once it is closed
/// and not before.
/// </summary>
public static class RoundTripCounter
{
    public static (int Closed, int Wins) Count(IEnumerable<TradeOrder> orders)
    {
        var closed = 0;
        var wins = 0;

        var bySymbol = orders
            .Where(o => o is { Status: "filled", FilledPrice: > 0 })
            .GroupBy(o => o.Symbol, StringComparer.OrdinalIgnoreCase);

        foreach (var group in bySymbol)
        {
            var lots = new LinkedList<(int Quantity, decimal Price)>();

            foreach (var order in group.OrderBy(o => o.FilledAtUtc ?? o.SubmittedAtUtc))
            {
                if (order.Side == OrderSide.Buy)
                {
                    lots.AddLast((order.Quantity, order.FilledPrice!.Value));
                    continue;
                }

                var remaining = order.Quantity;
                while (remaining > 0 && lots.First is { } node)
                {
                    var lot = node.Value;
                    var matched = Math.Min(remaining, lot.Quantity);

                    closed++;
                    if (order.FilledPrice!.Value > lot.Price)
                    {
                        wins++;
                    }

                    remaining -= matched;

                    if (lot.Quantity > matched)
                    {
                        // Part of the lot survives the sale and stays at the front of the queue.
                        node.Value = (lot.Quantity - matched, lot.Price);
                    }
                    else
                    {
                        lots.RemoveFirst();
                    }
                }
            }
        }

        return (closed, wins);
    }
}

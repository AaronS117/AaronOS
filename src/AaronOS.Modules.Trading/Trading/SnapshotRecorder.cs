using AaronOS.Core.Data;
using AaronOS.Modules.Trading.Brokerage;
using AaronOS.Modules.Trading.Data;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Trading.Trading;

/// <summary>
/// Keeps the local record in step with the broker: today's equity curve point, and the fill status
/// of anything still open.
/// </summary>
public class SnapshotRecorder(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    AlpacaClient alpaca,
    TimeProvider time)
{
    private static readonly string[] SettledStatuses = ["filled", "canceled", "cancelled", "rejected", "expired"];

    /// <summary>
    /// Writes or refreshes today's snapshot.
    ///
    /// The row is updated through the day rather than written once at the close, so the equity curve
    /// is current even though a desktop app cannot rely on being open at 4pm. The benchmark close is
    /// stamped at the same time and only when the fetch succeeds — leaving it null is honest,
    /// whereas carrying yesterday's figure forward would quietly bias the comparison.
    /// </summary>
    public async Task RecordTodayAsync(CancellationToken token = default)
    {
        if (!alpaca.IsConfigured)
        {
            return;
        }

        var account = await alpaca.GetAccountAsync(token);
        decimal? benchmark = null;
        try
        {
            benchmark = await alpaca.GetLatestDailyCloseAsync(PortfolioSnapshot.BenchmarkSymbol, token);
        }
        catch (AlpacaApiException)
        {
            // A missing benchmark must not cost us the equity point; the column stays null.
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var snapshot = await db.Set<PortfolioSnapshot>().FirstOrDefaultAsync(s => s.Date == today, token);

        if (snapshot is null)
        {
            snapshot = new PortfolioSnapshot { Date = today };
            db.Add(snapshot);
        }

        snapshot.Equity = account.Equity;
        snapshot.Cash = account.Cash;
        snapshot.BenchmarkClose = benchmark ?? snapshot.BenchmarkClose;

        await db.SaveChangesAsync(token);
    }

    /// <summary>
    /// Updates stored orders that the broker has since filled, cancelled or rejected.
    ///
    /// It also picks up orders that arrived already filled but without a price. A market order placed
    /// during trading hours frequently comes back "filled" on submission, and the earlier version
    /// treated any settled status as finished — so those rows kept a null fill price forever. Closed
    /// round trips are counted only from filled prices, which meant every instantly-filled trade was
    /// invisible to the performance figures and the thirty-trade gate could never open. A replay, where
    /// every fill is instant, made it obvious: three sells and zero closed trades.
    /// </summary>
    public async Task ReconcileOpenOrdersAsync(CancellationToken token = default)
    {
        if (!alpaca.IsConfigured)
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var open = await db.Set<TradeOrder>()
            .Where(o => !SettledStatuses.Contains(o.Status)
                        || (o.Status == "filled" && o.FilledPrice == null))
            .ToListAsync(token);

        foreach (var order in open)
        {
            try
            {
                var (status, filledPrice, filledAt) = await alpaca.GetOrderAsync(order.BrokerOrderId, token);
                order.Status = status;
                order.FilledPrice = filledPrice ?? order.FilledPrice;
                order.FilledAtUtc = filledAt ?? order.FilledAtUtc;
            }
            catch (AlpacaApiException)
            {
                // One unreadable order must not abandon the rest of the reconciliation.
            }
        }

        await db.SaveChangesAsync(token);
    }
}

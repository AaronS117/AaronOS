using AaronOS.Modules.Finance.Data;

namespace AaronOS.Modules.Finance.Sync;

/// <summary>
/// What you actually spend in a typical month, which is what sizes an emergency fund. Asking
/// someone to estimate this produces a number that is reliably too low; the transactions already
/// know the answer.
/// </summary>
public static class MonthlySpendCalculator
{
    public const int DefaultMonths = 3;

    /// <summary>
    /// Average spend per month over the most recent complete months.
    ///
    /// The current month is excluded because it is partial — averaging a month that is four days old
    /// in drags the figure down and makes an emergency fund look adequate when it is not. Months
    /// with no transactions at all are also excluded rather than counted as zero: no data and no
    /// spending look identical in an average but mean opposite things, and a bank link that only
    /// reaches back ninety days would otherwise report a spend of nearly nothing.
    /// </summary>
    public static decimal AverageMonthlyOutflow(
        IEnumerable<FinanceTransaction> transactions, DateOnly today, int months = DefaultMonths)
    {
        if (months < 1)
        {
            return 0;
        }

        var all = transactions as IReadOnlyCollection<FinanceTransaction> ?? transactions.ToList();
        var currentMonth = new DateOnly(today.Year, today.Month, 1);

        var totals = new List<decimal>(months);
        for (var back = 1; back <= months; back++)
        {
            var month = currentMonth.AddMonths(-back);
            var inMonth = all.Where(t => t.Date.Year == month.Year && t.Date.Month == month.Month).ToList();
            if (inMonth.Count == 0)
            {
                continue;
            }

            totals.Add(inMonth.Where(SpendFilter.IsSpend).Sum(t => t.Amount));
        }

        return totals.Count == 0 ? 0 : totals.Sum() / totals.Count;
    }

    /// <summary>
    /// How many months of expenses a balance covers, or null when spend is unknown. Null rather
    /// than infinity: a zero average means there is no data to divide by, not that the money lasts
    /// forever, and "—" is the honest thing to render.
    /// </summary>
    public static decimal? MonthsCovered(decimal balance, decimal averageMonthlySpend) =>
        averageMonthlySpend <= 0 ? null : balance / averageMonthlySpend;
}

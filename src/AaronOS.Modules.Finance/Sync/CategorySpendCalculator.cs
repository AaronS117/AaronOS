using AaronOS.Modules.Finance.Data;

namespace AaronOS.Modules.Finance.Sync;

public static class CategorySpendCalculator
{
    private static readonly HashSet<string> ExcludedCategories = ["TRANSFER_IN", "TRANSFER_OUT"];

    /// <summary>
    /// Sums Amount (Plaid convention: positive = money out) grouped by CategoryPrimary for the
    /// given month, excluding TRANSFER_IN/TRANSFER_OUT so moving money between the user's own
    /// linked accounts isn't double-counted as spend. This exclusion is a judgment call, not a
    /// Plaid guarantee — revisit if a future category needs the same treatment.
    /// </summary>
    public static Dictionary<string, decimal> SpendByCategory(
        IEnumerable<FinanceTransaction> transactions, int year, int month)
    {
        return transactions
            .Where(t => t.Date.Year == year && t.Date.Month == month)
            .Where(t => t.CategoryPrimary is null || !ExcludedCategories.Contains(t.CategoryPrimary))
            .Where(t => t.Amount > 0)
            .GroupBy(t => t.CategoryPrimary ?? "OTHER")
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
    }
}

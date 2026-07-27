using AaronOS.Modules.Finance.Data;

namespace AaronOS.Modules.Finance.Sync;

public static class CategorySpendCalculator
{
    /// <summary>
    /// Sums Amount (Plaid convention: positive = money out) grouped by CategoryPrimary for the
    /// given month. What counts as spend — in particular the exclusion of transfers between the
    /// user's own accounts — is defined once in <see cref="SpendFilter"/> and shared with the
    /// average-monthly-spend calculation.
    /// </summary>
    public static Dictionary<string, decimal> SpendByCategory(
        IEnumerable<FinanceTransaction> transactions, int year, int month)
    {
        return transactions
            .Where(t => t.Date.Year == year && t.Date.Month == month)
            .Where(SpendFilter.IsSpend)
            .GroupBy(t => t.CategoryPrimary ?? "OTHER")
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
    }
}

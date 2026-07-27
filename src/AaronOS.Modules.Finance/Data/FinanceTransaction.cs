using System.ComponentModel.DataAnnotations.Schema;
using AaronOS.Modules.Finance.Sync;

namespace AaronOS.Modules.Finance.Data;

/// <summary>Plaid convention: positive Amount means money out (a purchase); negative means money in.</summary>
public class FinanceTransaction
{
    public int Id { get; set; }
    public string PlaidTransactionId { get; set; } = "";
    public int FinanceAccountId { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public bool Pending { get; set; }
    public string? CategoryPrimary { get; set; }
    public string? CategoryDetailed { get; set; }
    public string IsoCurrencyCode { get; set; } = "USD";

    /// <summary>Human-readable form of CategoryPrimary for display (e.g. "Food And Drink").</summary>
    [NotMapped]
    public string CategoryDisplay => CategoryNameFormatter.Humanize(CategoryPrimary);

    [NotMapped]
    public bool IsInflow => Amount < 0;

    /// <summary>Signed for reading rather than for Plaid's convention: money in shows as +, money
    /// out as −, which is what a person expects from a statement.</summary>
    [NotMapped]
    public string AmountDisplay => IsInflow ? $"+{-Amount:N2}" : $"−{Amount:N2}";

    [NotMapped]
    public string DateDisplay => Date.ToString("MMM d");
}

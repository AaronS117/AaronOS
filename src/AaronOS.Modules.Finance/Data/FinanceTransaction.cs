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
}

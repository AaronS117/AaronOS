namespace AaronOS.Modules.Finance.Data;

/// <summary>One row per account within an item (checking, savings, credit card, etc.).</summary>
public class FinanceAccount
{
    public int Id { get; set; }
    public string PlaidAccountId { get; set; } = "";
    public int PlaidItemId { get; set; }
    public string Name { get; set; } = "";
    public string? Mask { get; set; }
    public string Type { get; set; } = "";
    public string? Subtype { get; set; }
    public decimal? CurrentBalance { get; set; }
    public decimal? AvailableBalance { get; set; }
    public string IsoCurrencyCode { get; set; } = "USD";
}

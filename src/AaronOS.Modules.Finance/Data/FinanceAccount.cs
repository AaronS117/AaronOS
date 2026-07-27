using System.ComponentModel.DataAnnotations.Schema;

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

    /// <summary>
    /// True for accounts whose balance represents money owed rather than money held. Plaid reports
    /// a credit card's or loan's `current` balance as the outstanding amount, so these must be
    /// subtracted, not added, when totalling.
    /// </summary>
    [NotMapped]
    public bool IsLiability => Type is "credit" or "loan";

    /// <summary>"Checking ····1234" — subtype reads better than Plaid's raw type for a person.</summary>
    [NotMapped]
    public string DetailDisplay
    {
        get
        {
            var kind = string.IsNullOrWhiteSpace(Subtype) ? Type : Subtype;
            var label = string.IsNullOrWhiteSpace(kind)
                ? ""
                : char.ToUpperInvariant(kind[0]) + kind[1..].ToLowerInvariant();
            return string.IsNullOrWhiteSpace(Mask) ? label : $"{label} ····{Mask}";
        }
    }

    /// <summary>Liability balances are shown as owed (negative) so the sign matches the maths.</summary>
    [NotMapped]
    public decimal SignedBalance => IsLiability ? -(CurrentBalance ?? 0) : CurrentBalance ?? 0;
}

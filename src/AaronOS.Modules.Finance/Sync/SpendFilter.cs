using AaronOS.Modules.Finance.Data;

namespace AaronOS.Modules.Finance.Sync;

/// <summary>
/// One shared definition of "money that actually left your hands".
///
/// It lives on its own because two callers need the identical rule: the category breakdown on the
/// dashboard and the average monthly spend that sets an emergency-fund target. Two copies of a
/// judgment call like the transfer exclusion would eventually disagree, and the emergency fund
/// would then be sized from a different number than the one shown on screen.
/// </summary>
public static class SpendFilter
{
    /// <summary>
    /// Moving money between your own linked accounts is not spending, so it is excluded. This is a
    /// judgment call about Plaid's categories rather than a guarantee from Plaid — revisit it if a
    /// future category needs the same treatment.
    /// </summary>
    private static readonly HashSet<string> ExcludedCategories = ["TRANSFER_IN", "TRANSFER_OUT"];

    /// <summary>True for an outflow that counts as spend. Plaid convention: positive is money out.</summary>
    public static bool IsSpend(FinanceTransaction transaction) =>
        transaction.Amount > 0 && !IsInternalTransfer(transaction);

    public static bool IsInternalTransfer(FinanceTransaction transaction) =>
        transaction.CategoryPrimary is not null && ExcludedCategories.Contains(transaction.CategoryPrimary);
}

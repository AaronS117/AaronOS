using AaronOS.Modules.Finance.Data;
using AaronOS.Modules.Finance.Plaid;

namespace AaronOS.Modules.Finance.Sync;

/// <summary>
/// Pure merge logic for a transactions/sync result — deliberately free of any DbContext/IO
/// dependency so the algorithm is testable against a plain in-memory list (see
/// AaronOS.Modules.Finance.Tests). <see cref="Apply"/> answers "what should the final set of
/// transactions be" for tests; real EF Core persistence (AaronOS.Modules.Finance's
/// FinanceDashboardViewModel) does its own upsert against tracked entities using
/// <see cref="CopyFieldsInto"/> so identity/change-tracking isn't lost — it does not feed
/// <see cref="Apply"/>'s output directly into the DbContext.
/// </summary>
public static class TransactionSyncMerger
{
    /// <param name="existing">Transactions currently known, keyed by PlaidTransactionId.</param>
    /// <param name="plaidAccountIdToFinanceAccountId">Maps a Plaid account_id to this app's
    /// internal FinanceAccount.Id — the sync response only knows Plaid's ids.</param>
    public static List<FinanceTransaction> Apply(
        IEnumerable<FinanceTransaction> existing,
        PlaidSyncResult syncResult,
        IReadOnlyDictionary<string, int> plaidAccountIdToFinanceAccountId)
    {
        var byPlaidId = existing.ToDictionary(t => t.PlaidTransactionId);

        foreach (var removedId in syncResult.RemovedIds)
        {
            byPlaidId.Remove(removedId);
        }

        foreach (var dto in syncResult.Added.Concat(syncResult.Modified))
        {
            if (!plaidAccountIdToFinanceAccountId.TryGetValue(dto.AccountId, out var financeAccountId))
            {
                // ponytail: a transaction for an account we haven't seeded via accounts/get yet —
                // skip rather than guess an account id; the next full accounts/get sync will catch up.
                continue;
            }

            if (!byPlaidId.TryGetValue(dto.TransactionId, out var target))
            {
                target = new FinanceTransaction { PlaidTransactionId = dto.TransactionId };
                byPlaidId[dto.TransactionId] = target;
            }

            CopyFieldsInto(dto, target, financeAccountId);
        }

        return byPlaidId.Values.ToList();
    }

    /// <summary>Copies a Plaid transaction DTO's fields onto an existing entity in place — used
    /// both by <see cref="Apply"/> and by real DB upserts, so a tracked EF Core entity keeps its
    /// identity/Id instead of being replaced by a fresh untracked instance.</summary>
    public static void CopyFieldsInto(PlaidTransactionDto dto, FinanceTransaction target, int financeAccountId)
    {
        target.FinanceAccountId = financeAccountId;
        target.Date = DateOnly.Parse(dto.Date);
        target.Name = dto.Name;
        target.Amount = dto.Amount;
        target.Pending = dto.Pending;
        target.CategoryPrimary = dto.PersonalFinanceCategory?.Primary;
        target.CategoryDetailed = dto.PersonalFinanceCategory?.Detailed;
        target.IsoCurrencyCode = dto.IsoCurrencyCode ?? "USD";
    }
}

using AaronOS.Modules.Finance.Data;
using AaronOS.Modules.Finance.Plaid;
using AaronOS.Modules.Finance.Sync;

namespace AaronOS.Modules.Finance.Tests;

public class TransactionSyncMergerTests
{
    private static readonly Dictionary<string, int> AccountMap = new() { ["plaid-acct-1"] = 1 };

    private static PlaidTransactionDto Dto(string id, string accountId, decimal amount, string name = "Coffee Shop") =>
        new(id, accountId, "2026-07-01", name, amount, false, "USD", new PlaidPersonalFinanceCategoryDto("FOOD_AND_DRINK", "COFFEE"));

    [Fact]
    public void Added_TransactionsAppearInResult()
    {
        var result = TransactionSyncMerger.Apply(
            existing: [],
            syncResult: new PlaidSyncResult([Dto("txn-1", "plaid-acct-1", 4.50m)], [], [], "cursor-1"),
            plaidAccountIdToFinanceAccountId: AccountMap);

        var transaction = Assert.Single(result);
        Assert.Equal("txn-1", transaction.PlaidTransactionId);
        Assert.Equal(4.50m, transaction.Amount);
        Assert.Equal(1, transaction.FinanceAccountId);
    }

    [Fact]
    public void Modified_UpdatesExistingTransactionInPlace()
    {
        var existing = new FinanceTransaction { Id = 42, PlaidTransactionId = "txn-1", FinanceAccountId = 1, Amount = 4.50m, Name = "Coffee Shop" };

        var result = TransactionSyncMerger.Apply(
            existing: [existing],
            syncResult: new PlaidSyncResult([], [Dto("txn-1", "plaid-acct-1", 5.25m, "Coffee Shop (updated)")], [], "cursor-2"),
            plaidAccountIdToFinanceAccountId: AccountMap);

        var transaction = Assert.Single(result);
        Assert.Equal(42, transaction.Id); // identity preserved — not replaced by a fresh entity
        Assert.Equal(5.25m, transaction.Amount);
        Assert.Equal("Coffee Shop (updated)", transaction.Name);
    }

    [Fact]
    public void Removed_TakesTransactionOutOfResult()
    {
        var existing = new FinanceTransaction { Id = 1, PlaidTransactionId = "txn-1", FinanceAccountId = 1 };
        var keep = new FinanceTransaction { Id = 2, PlaidTransactionId = "txn-2", FinanceAccountId = 1 };

        var result = TransactionSyncMerger.Apply(
            existing: [existing, keep],
            syncResult: new PlaidSyncResult([], [], ["txn-1"], "cursor-3"),
            plaidAccountIdToFinanceAccountId: AccountMap);

        var transaction = Assert.Single(result);
        Assert.Equal("txn-2", transaction.PlaidTransactionId);
    }

    [Fact]
    public void UnknownAccount_IsSkippedRatherThanGuessed()
    {
        var result = TransactionSyncMerger.Apply(
            existing: [],
            syncResult: new PlaidSyncResult([Dto("txn-1", "plaid-acct-unseeded", 10m)], [], [], "cursor-4"),
            plaidAccountIdToFinanceAccountId: AccountMap);

        Assert.Empty(result);
    }
}

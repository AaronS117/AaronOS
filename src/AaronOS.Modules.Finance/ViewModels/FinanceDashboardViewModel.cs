using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Finance.Data;
using AaronOS.Modules.Finance.Plaid;
using AaronOS.Modules.Finance.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Finance.ViewModels;

public partial class FinanceDashboardViewModel(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    PlaidApiClient plaidApiClient) : ViewModelBase
{
    [ObservableProperty]
    private decimal _totalBalance;

    [ObservableProperty]
    private string _errorMessage = "";

    public ObservableCollection<FinanceAccount> Accounts { get; } = [];
    public ObservableCollection<FinanceTransaction> RecentTransactions { get; } = [];
    public List<ISeries> SpendByCategorySeries { get; } = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            var accounts = await db.Set<FinanceAccount>().OrderBy(a => a.Name).ToListAsync();
            Accounts.Clear();
            foreach (var account in accounts)
            {
                Accounts.Add(account);
            }

            TotalBalance = accounts.Sum(a => a.CurrentBalance ?? 0);

            var cutoff = DateOnly.FromDateTime(DateTime.Now.AddDays(-30));
            var recent = await db.Set<FinanceTransaction>()
                .Where(t => t.Date >= cutoff)
                .OrderByDescending(t => t.Date)
                .ToListAsync();
            RecentTransactions.Clear();
            foreach (var transaction in recent)
            {
                RecentTransactions.Add(transaction);
            }

            var now = DateTime.Now;
            var allTransactions = await db.Set<FinanceTransaction>().ToListAsync();
            var spendByCategory = CategorySpendCalculator.SpendByCategory(allTransactions, now.Year, now.Month);

            SpendByCategorySeries.Clear();
            if (spendByCategory.Count > 0)
            {
                SpendByCategorySeries.Add(new PieSeries<decimal>
                {
                    Values = spendByCategory.Values.ToArray(),
                    Name = "Spend by category"
                });
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        IsBusy = true;
        ErrorMessage = "";
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var items = await db.Set<PlaidItem>().ToListAsync();

            foreach (var item in items)
            {
                var accessToken = AccessTokenProtector.Unprotect(item.AccessTokenEncrypted);

                var accountsResponse = await plaidApiClient.GetAccountsAsync(accessToken);
                foreach (var accountDto in accountsResponse.Accounts)
                {
                    var account = await db.Set<FinanceAccount>()
                        .FirstOrDefaultAsync(a => a.PlaidAccountId == accountDto.AccountId);
                    if (account is null)
                    {
                        account = new FinanceAccount { PlaidAccountId = accountDto.AccountId, PlaidItemId = item.Id };
                        db.Add(account);
                    }

                    account.Name = accountDto.Name;
                    account.Mask = accountDto.Mask;
                    account.Type = accountDto.Type;
                    account.Subtype = accountDto.Subtype;
                    account.CurrentBalance = accountDto.Balances.Current;
                    account.AvailableBalance = accountDto.Balances.Available;
                    account.IsoCurrencyCode = accountDto.Balances.IsoCurrencyCode ?? "USD";
                }

                await db.SaveChangesAsync();

                var accountIdMap = await db.Set<FinanceAccount>()
                    .Where(a => a.PlaidItemId == item.Id)
                    .ToDictionaryAsync(a => a.PlaidAccountId, a => a.Id);

                var syncResult = await plaidApiClient.SyncTransactionsAsync(accessToken, item.Cursor);

                var touchedIds = syncResult.Added.Concat(syncResult.Modified).Select(t => t.TransactionId)
                    .Concat(syncResult.RemovedIds).ToHashSet();
                var existingByPlaidId = await db.Set<FinanceTransaction>()
                    .Where(t => touchedIds.Contains(t.PlaidTransactionId))
                    .ToDictionaryAsync(t => t.PlaidTransactionId);

                // Upsert against tracked entities directly (not via TransactionSyncMerger.Apply,
                // which returns plain untracked objects for its own pure-function unit tests).
                foreach (var dto in syncResult.Added.Concat(syncResult.Modified))
                {
                    if (!accountIdMap.TryGetValue(dto.AccountId, out var financeAccountId))
                    {
                        continue;
                    }

                    if (!existingByPlaidId.TryGetValue(dto.TransactionId, out var target))
                    {
                        target = new FinanceTransaction { PlaidTransactionId = dto.TransactionId };
                        db.Add(target);
                        existingByPlaidId[dto.TransactionId] = target;
                    }

                    TransactionSyncMerger.CopyFieldsInto(dto, target, financeAccountId);
                }

                foreach (var removedId in syncResult.RemovedIds)
                {
                    if (existingByPlaidId.TryGetValue(removedId, out var toRemove))
                    {
                        db.Remove(toRemove);
                    }
                }

                item.Cursor = syncResult.NextCursor;
                await db.SaveChangesAsync();
            }

            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

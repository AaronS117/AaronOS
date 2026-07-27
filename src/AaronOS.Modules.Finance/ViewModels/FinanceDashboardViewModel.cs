using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Finance.Data;
using AaronOS.Modules.Finance.Plaid;
using AaronOS.Modules.Finance.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Finance.ViewModels;

/// <summary>One row of the ranked spend-by-category list. BarWidth is pre-computed in device
/// pixels so the view needs no value converter to size the bar.</summary>
public record CategorySpendRow(string Label, decimal Amount, double BarWidth);

/// <summary>One day of the current month in the daily-spend strip.</summary>
public record DailySpendTick(double Height, double Opacity, string Tooltip);

public partial class FinanceDashboardViewModel(
    IDbContextFactory<AaronOsDbContext> dbContextFactory,
    PlaidApiClient plaidApiClient) : ViewModelBase
{
    private const double MaxBarWidth = 300;
    private const double MaxTickHeight = 30;

    [ObservableProperty]
    private decimal _netTotal;

    [ObservableProperty]
    private decimal _assetTotal;

    [ObservableProperty]
    private decimal _liabilityTotal;

    [ObservableProperty]
    private string _accountSummary = "";

    [ObservableProperty]
    private decimal _monthSpendTotal;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _hasAccounts;

    [ObservableProperty]
    private bool _hasCategoryData;

    [ObservableProperty]
    private bool _hasRecentTransactions;

    public ObservableCollection<FinanceAccount> Accounts { get; } = [];
    public ObservableCollection<FinanceTransaction> RecentTransactions { get; } = [];
    public ObservableCollection<CategorySpendRow> CategoryRows { get; } = [];
    public ObservableCollection<DailySpendTick> DailyTicks { get; } = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            // Sorted in memory, not in SQL: IsLiability is a [NotMapped] computed property, so EF
            // cannot translate it into an ORDER BY. Materialise first, then order.
            var accounts = (await db.Set<FinanceAccount>().ToListAsync())
                .OrderBy(a => a.IsLiability)
                .ThenByDescending(a => a.CurrentBalance ?? 0)
                .ToList();
            Accounts.Clear();
            foreach (var account in accounts)
            {
                Accounts.Add(account);
            }

            // Plaid reports a credit/loan account's `current` balance as the amount OWED, so
            // summing every account together overstates what you actually have. Split assets from
            // liabilities and lead with the net figure.
            AssetTotal = accounts.Where(a => !a.IsLiability).Sum(a => a.CurrentBalance ?? 0);
            LiabilityTotal = accounts.Where(a => a.IsLiability).Sum(a => a.CurrentBalance ?? 0);
            NetTotal = AssetTotal - LiabilityTotal;
            AccountSummary = accounts.Count == 1 ? "1 linked account" : $"{accounts.Count} linked accounts";
            HasAccounts = accounts.Count > 0;

            var cutoff = DateOnly.FromDateTime(DateTime.Now.AddDays(-30));
            var recent = await db.Set<FinanceTransaction>()
                .Where(t => t.Date >= cutoff)
                .OrderByDescending(t => t.Date)
                .Take(40)
                .ToListAsync();
            RecentTransactions.Clear();
            foreach (var transaction in recent)
            {
                RecentTransactions.Add(transaction);
            }
            HasRecentTransactions = recent.Count > 0;

            var now = DateTime.Now;
            var allTransactions = await db.Set<FinanceTransaction>().ToListAsync();
            BuildCategoryRows(allTransactions, now.Year, now.Month);
            BuildDailyTicks(allTransactions, now.Year, now.Month);
            HasCategoryData = CategoryRows.Count > 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Ranked horizontal bars rather than a pie chart: the question this answers is "what is
    /// taking the most money", which is a ranking, and a bar list shows the ordering, the exact
    /// figures and the category names together without a colour-matched legend.
    /// </summary>
    private void BuildCategoryRows(List<FinanceTransaction> transactions, int year, int month)
    {
        var spend = CategorySpendCalculator.SpendByCategory(transactions, year, month);
        MonthSpendTotal = spend.Values.Sum();

        CategoryRows.Clear();
        if (spend.Count == 0)
        {
            return;
        }

        const int maxRows = 8;
        var ordered = spend.OrderByDescending(kv => kv.Value).ToList();
        var shown = ordered.Take(maxRows).ToList();
        var remainder = ordered.Skip(maxRows).Sum(kv => kv.Value);

        var max = shown[0].Value;
        foreach (var (category, amount) in shown)
        {
            var width = max <= 0 ? 0 : MaxBarWidth * (double)(amount / max);
            CategoryRows.Add(new CategorySpendRow(CategoryNameFormatter.Humanize(category), amount, width));
        }

        if (remainder > 0)
        {
            var width = max <= 0 ? 0 : MaxBarWidth * (double)(remainder / max);
            CategoryRows.Add(new CategorySpendRow($"Other ({ordered.Count - maxRows} categories)", remainder, width));
        }
    }

    /// <summary>
    /// The strip of ticks under the hero figure — one per day of the current month, height scaled
    /// to that day's spend. It doubles as the visual echo of the app's segmented reactor ring,
    /// but it carries real data rather than being ornament.
    /// </summary>
    private void BuildDailyTicks(List<FinanceTransaction> transactions, int year, int month)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var byDay = transactions
            .Where(t => t.Date.Year == year && t.Date.Month == month && t.Amount > 0)
            .GroupBy(t => t.Date.Day)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var maxDay = byDay.Count == 0 ? 0m : byDay.Values.Max();

        DailyTicks.Clear();
        for (var day = 1; day <= daysInMonth; day++)
        {
            byDay.TryGetValue(day, out var amount);
            var height = maxDay <= 0 ? 3 : 3 + (MaxTickHeight - 3) * (double)(amount / maxDay);
            var opacity = amount > 0 ? 1.0 : 0.18;
            var tooltip = amount > 0
                ? $"{new DateOnly(year, month, day):MMM d} — {amount:C}"
                : $"{new DateOnly(year, month, day):MMM d} — no spend";
            DailyTicks.Add(new DailySpendTick(height, opacity, tooltip));
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        IsBusy = true;
        ErrorMessage = "";
        HasError = false;
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
            HasError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

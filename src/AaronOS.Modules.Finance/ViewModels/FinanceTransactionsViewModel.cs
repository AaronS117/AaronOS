using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Finance.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Finance.ViewModels;

/// <summary>A choice in the account filter. A null Account means "every account" — modelled as an
/// explicit option so the filter can be cleared again after picking one.</summary>
public record AccountFilter(string Label, FinanceAccount? Account);

public partial class FinanceTransactionsViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    // ponytail: rows render without virtualization (the shell owns scrolling, so an inner
    // virtualizing host would reintroduce the nested-scroller bug). A few hundred rows is fine;
    // the cap keeps it that way as history grows, and the UI states when it has truncated.
    private const int MaxRows = 300;

    public ObservableCollection<FinanceTransaction> Transactions { get; } = [];
    public ObservableCollection<AccountFilter> AccountFilters { get; } = [];

    [ObservableProperty]
    private AccountFilter? _selectedFilter;

    [ObservableProperty]
    private int _shownCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private bool _isTruncated;

    [ObservableProperty]
    private decimal _totalOut;

    [ObservableProperty]
    private decimal _totalIn;

    [ObservableProperty]
    private bool _hasTransactions;

    partial void OnSelectedFilterChanged(AccountFilter? value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            if (AccountFilters.Count == 0)
            {
                AccountFilters.Add(new AccountFilter("All accounts", null));
                foreach (var account in await db.Set<FinanceAccount>().OrderBy(a => a.Name).ToListAsync())
                {
                    AccountFilters.Add(new AccountFilter(account.Name, account));
                }

                // Setting this re-enters LoadAsync via OnSelectedFilterChanged, which then runs the
                // query with the filter applied — so return rather than querying twice.
                SelectedFilter = AccountFilters[0];
                return;
            }

            var query = db.Set<FinanceTransaction>().AsQueryable();
            if (SelectedFilter?.Account is { } selected)
            {
                query = query.Where(t => t.FinanceAccountId == selected.Id);
            }

            TotalCount = await query.CountAsync();
            var transactions = await query
                .OrderByDescending(t => t.Date)
                .ThenByDescending(t => t.Id)
                .Take(MaxRows)
                .ToListAsync();

            Transactions.Clear();
            foreach (var transaction in transactions)
            {
                Transactions.Add(transaction);
            }

            ShownCount = transactions.Count;
            IsTruncated = TotalCount > ShownCount;
            HasTransactions = TotalCount > 0;

            // Totals cover the whole filtered set, not just the visible page, so the figures do not
            // silently change meaning when the list is truncated.
            TotalOut = await query.Where(t => t.Amount > 0).SumAsync(t => (decimal?)t.Amount) ?? 0;
            TotalIn = -(await query.Where(t => t.Amount < 0).SumAsync(t => (decimal?)t.Amount) ?? 0);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

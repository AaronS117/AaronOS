using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Finance.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Finance.ViewModels;

public partial class FinanceTransactionsViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    public ObservableCollection<FinanceTransaction> Transactions { get; } = [];
    public ObservableCollection<FinanceAccount> Accounts { get; } = [];

    [ObservableProperty]
    private FinanceAccount? _selectedAccount;

    partial void OnSelectedAccountChanged(FinanceAccount? value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();

            if (Accounts.Count == 0)
            {
                var accounts = await db.Set<FinanceAccount>().OrderBy(a => a.Name).ToListAsync();
                foreach (var account in accounts)
                {
                    Accounts.Add(account);
                }
            }

            var query = db.Set<FinanceTransaction>().AsQueryable();
            if (SelectedAccount is not null)
            {
                query = query.Where(t => t.FinanceAccountId == SelectedAccount.Id);
            }

            var transactions = await query.OrderByDescending(t => t.Date).ToListAsync();
            Transactions.Clear();
            foreach (var transaction in transactions)
            {
                Transactions.Add(transaction);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}

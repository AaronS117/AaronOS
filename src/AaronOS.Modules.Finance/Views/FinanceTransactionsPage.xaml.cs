using AaronOS.Core;
using AaronOS.Modules.Finance.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace AaronOS.Modules.Finance.Views;

public sealed partial class FinanceTransactionsPage : Page
{
    public FinanceTransactionsViewModel ViewModel { get; }

    public FinanceTransactionsPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<FinanceTransactionsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}

using AaronOS.Core;
using AaronOS.Modules.Finance.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace AaronOS.Modules.Finance.Views;

public sealed partial class FinanceDashboardPage : Page
{
    public FinanceDashboardViewModel ViewModel { get; }

    public FinanceDashboardPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<FinanceDashboardViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}

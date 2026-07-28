using AaronOS.Core;
using AaronOS.Modules.Trading.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace AaronOS.Modules.Trading.Views;

public sealed partial class TradingDashboardPage : Page
{
    public TradingViewModel ViewModel { get; }

    public TradingDashboardPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<TradingViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}

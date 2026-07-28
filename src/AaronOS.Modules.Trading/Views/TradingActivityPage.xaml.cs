using AaronOS.Core;
using AaronOS.Modules.Trading.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace AaronOS.Modules.Trading.Views;

public sealed partial class TradingActivityPage : Page
{
    public TradingViewModel ViewModel { get; }

    public TradingActivityPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<TradingViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}

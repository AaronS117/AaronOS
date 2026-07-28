using AaronOS.Core;
using AaronOS.Modules.Trading.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace AaronOS.Modules.Trading.Views;

public sealed partial class TradingKeysSection : UserControl
{
    public TradingKeysViewModel ViewModel { get; }

    public TradingKeysSection()
    {
        ViewModel = AppServices.Provider.GetRequiredService<TradingKeysViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += (_, _) => ViewModel.LoadCommand.Execute(null);
    }
}

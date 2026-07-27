using AaronOS.Core;
using AaronOS.Modules.Finance.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace AaronOS.Modules.Finance.Views;

public sealed partial class RetirementPage : Page
{
    public RetirementViewModel ViewModel { get; }

    public RetirementPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<RetirementViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}

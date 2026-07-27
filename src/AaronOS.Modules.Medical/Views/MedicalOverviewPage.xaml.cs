using AaronOS.Core;
using AaronOS.Modules.Medical.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace AaronOS.Modules.Medical.Views;

public sealed partial class MedicalOverviewPage : Page
{
    public MedicalOverviewViewModel ViewModel { get; }

    public MedicalOverviewPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<MedicalOverviewViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}

using AaronOS.Core;
using AaronOS.Modules.BodyMeasurements.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace AaronOS.Modules.BodyMeasurements.Views;

public sealed partial class CheckInPage : Page
{
    public CheckInViewModel ViewModel { get; }

    public CheckInPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<CheckInViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        // Prefills the weight wheel from the last check-in before the picker settles on a default.
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}

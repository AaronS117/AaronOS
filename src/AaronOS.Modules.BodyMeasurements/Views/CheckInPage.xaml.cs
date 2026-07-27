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
    }
}

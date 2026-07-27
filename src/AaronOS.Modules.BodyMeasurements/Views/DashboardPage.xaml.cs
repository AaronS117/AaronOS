using AaronOS.Core;
using AaronOS.Modules.BodyMeasurements.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace AaronOS.Modules.BodyMeasurements.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<DashboardViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
            // Pushed in rather than bound: the figure rebuilds its whole mesh from one check-in,
            // which is far simpler than ten dependency properties each triggering a rebuild.
            BodyModel.Apply(ViewModel.LatestCheckIn, ViewModel.HeightInches);
        };
    }
}

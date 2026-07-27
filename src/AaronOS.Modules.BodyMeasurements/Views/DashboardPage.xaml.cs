using AaronOS.Core;
using AaronOS.Modules.BodyMeasurements.Data;
using AaronOS.Modules.BodyMeasurements.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
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

        // Clicking the figure and clicking a row both open the same editor.
        BodyModel.PartClicked += ViewModel.BeginEdit;

        Loaded += async (_, _) =>
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
            RefreshFigure();
        };

        // Rebuild the figure on every reload, so it reshapes as soon as a measurement is saved.
        ViewModel.Reloaded += RefreshFigure;
    }

    /// <summary>Pushed in rather than bound: the figure rebuilds its whole mesh from one check-in,
    /// which is far simpler than ten dependency properties each triggering a rebuild.</summary>
    private void RefreshFigure() => BodyModel.Apply(ViewModel.LatestCheckIn, ViewModel.HeightInches);

    private void MeasurementRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GoalMetric metric })
        {
            ViewModel.BeginEdit(metric);
        }
    }
}

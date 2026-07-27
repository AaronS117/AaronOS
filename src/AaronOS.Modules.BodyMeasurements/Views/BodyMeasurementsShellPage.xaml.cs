using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AaronOS.Modules.BodyMeasurements.Views;

/// <summary>
/// The module's single nav-pane entry point. Hosts an internal Frame so the shell only needs
/// one NavigationView item per module — this page provides its own top-level navigation
/// to its five pages, per docs/MODULE_GUIDELINES.md.
/// </summary>
public sealed partial class BodyMeasurementsShellPage : Page
{
    public BodyMeasurementsShellPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ContentFrame.Navigate(typeof(DashboardPage));
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(typeof(DashboardPage));
    private void CheckIn_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(typeof(CheckInPage));
    private void History_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(typeof(HistoryPage));
    private void ClothingSizes_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(typeof(ClothingSizesPage));
    private void Goals_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(typeof(GoalsPage));
}

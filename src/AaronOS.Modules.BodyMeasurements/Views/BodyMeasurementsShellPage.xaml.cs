using System.Windows;
using System.Windows.Controls;

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
        Loaded += (_, _) => ContentFrame.Navigate(new DashboardPage());
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new DashboardPage());
    private void CheckIn_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new CheckInPage());
    private void History_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new HistoryPage());
    private void ClothingSizes_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new ClothingSizesPage());
    private void Goals_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new GoalsPage());
}

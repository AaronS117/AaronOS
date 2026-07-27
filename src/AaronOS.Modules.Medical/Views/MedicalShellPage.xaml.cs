using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Medical.Views;

/// <summary>
/// The module's single nav-pane entry point. Hosts an internal Frame so the shell only needs one
/// NavigationView item — this page provides its own top-level navigation to its six pages, per
/// docs/MODULE_GUIDELINES.md.
/// </summary>
public sealed partial class MedicalShellPage : Page
{
    public MedicalShellPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ContentFrame.Navigate(new MedicalOverviewPage());
    }

    private void Overview_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new MedicalOverviewPage());
    private void History_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new HistoryPage());
    private void Medications_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new MedicationsPage());
    private void Visits_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new VisitsPage());
    private void Labs_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new LabsPage());
    private void Import_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new ImportPage());
}

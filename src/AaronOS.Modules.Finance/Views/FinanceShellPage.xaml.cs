using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Finance.Views;

/// <summary>The module's single nav-pane entry point, per docs/MODULE_GUIDELINES.md — mirrors
/// AaronOS.Modules.BodyMeasurements.Views.BodyMeasurementsShellPage exactly.</summary>
public sealed partial class FinanceShellPage : Page
{
    public FinanceShellPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ContentFrame.Navigate(new FinanceDashboardPage());
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new FinanceDashboardPage());
    private void Transactions_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new FinanceTransactionsPage());
}

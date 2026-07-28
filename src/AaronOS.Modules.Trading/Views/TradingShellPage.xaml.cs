using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Trading.Views;

/// <summary>The module's single nav-pane entry point, per docs/MODULE_GUIDELINES.md.</summary>
public sealed partial class TradingShellPage : Page
{
    public TradingShellPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ContentFrame.Navigate(new TradingDashboardPage());
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new TradingDashboardPage());
    private void Activity_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new TradingActivityPage());
}

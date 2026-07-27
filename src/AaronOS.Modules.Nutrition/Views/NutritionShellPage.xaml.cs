using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Nutrition.Views;

/// <summary>
/// The module's single nav-pane entry point. Hosts an internal Frame so the shell only needs one
/// NavigationView item — this page provides its own top-level navigation to its three pages, per
/// docs/MODULE_GUIDELINES.md.
/// </summary>
public sealed partial class NutritionShellPage : Page
{
    public NutritionShellPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ContentFrame.Navigate(new NutritionDashboardPage());
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new NutritionDashboardPage());
    private void Ingredients_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new IngredientsPage());
    private void Inventory_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new InventoryPage());
}

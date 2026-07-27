using AaronOS.Core;
using AaronOS.Modules.Nutrition.Data;
using AaronOS.Modules.Nutrition.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Nutrition.Views;

public sealed partial class InventoryPage : Page
{
    public InventoryViewModel ViewModel { get; }

    public InventoryPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<InventoryViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InventoryItem item })
        {
            _ = ViewModel.DeleteCommand.ExecuteAsync(item);
        }
    }
}

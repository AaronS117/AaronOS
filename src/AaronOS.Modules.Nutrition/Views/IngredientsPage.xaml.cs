using AaronOS.Core;
using AaronOS.Modules.Nutrition.Usda;
using AaronOS.Modules.Nutrition.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Nutrition.Views;

public sealed partial class IngredientsPage : Page
{
    public IngredientsViewModel ViewModel { get; }

    public IngredientsPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<IngredientsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void UsdaResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UsdaSearchResult result })
        {
            _ = ViewModel.AddFromUsdaCommand.ExecuteAsync(result);
        }
    }
}

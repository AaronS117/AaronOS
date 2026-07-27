using AaronOS.Core;
using AaronOS.Modules.Nutrition.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Nutrition.Views;

public sealed partial class NutritionDashboardPage : Page
{
    public NutritionDashboardViewModel ViewModel { get; }

    public NutritionDashboardPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<NutritionDashboardViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void AddRecipeButton_Click(object sender, RoutedEventArgs e) =>
        NavigationService?.Navigate(new RecipeEditPage());

    private void RecipeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecipeCard card })
        {
            NavigationService?.Navigate(new RecipeEditPage(card.Recipe.Id));
        }
    }
}

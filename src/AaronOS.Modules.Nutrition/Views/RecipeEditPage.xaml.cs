using AaronOS.Core;
using AaronOS.Modules.Nutrition.Data;
using AaronOS.Modules.Nutrition.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Nutrition.Views;

public sealed partial class RecipeEditPage : Page
{
    public RecipeEditViewModel ViewModel { get; }

    public RecipeEditPage() : this(null)
    {
    }

    public RecipeEditPage(int? recipeId)
    {
        ViewModel = AppServices.Provider.GetRequiredService<RecipeEditViewModel>();
        ViewModel.SetRecipeId(recipeId);
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void RemoveLineButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecipeIngredient line })
        {
            ViewModel.RemoveLineCommand.Execute(line);
        }
    }
}

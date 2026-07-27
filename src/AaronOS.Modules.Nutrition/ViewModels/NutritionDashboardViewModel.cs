using System.Collections.ObjectModel;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.Nutrition.Calculations;
using AaronOS.Modules.Nutrition.Data;
using AaronOS.Modules.Nutrition.Seeding;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Nutrition.ViewModels;

public record RecipeCard(
    Recipe Recipe,
    RecipeNutritionTotals PerServing,
    List<CompatibilityConcern> Concerns,
    RecipeStockResult Stock)
{
    public bool HasDislikedIngredient => Concerns.Any(c => c.Level == CompatibilityLevel.Blocked);

    public string StockSummary => Stock.HasEverything
        ? "Have everything"
        : $"Missing: {string.Join(", ", Stock.MissingIngredientNames)}";
}

public partial class NutritionDashboardViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    private List<RecipeCard> _allCards = [];

    public ObservableCollection<RecipeCard> VisibleRecipes { get; } = [];

    [ObservableProperty]
    private bool _excludeDisliked = true;

    [ObservableProperty]
    private double _maxCaloriesPerServing = double.NaN;

    [ObservableProperty]
    private bool _sortByUseItUp;

    [ObservableProperty]
    private string _statusMessage = "";

    partial void OnExcludeDislikedChanged(bool value) => ApplyFilters();
    partial void OnMaxCaloriesPerServingChanged(double value) => ApplyFilters();
    partial void OnSortByUseItUpChanged(bool value) => ApplyFilters();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            await IngredientSeeder.SeedIfEmptyAsync(db);

            var recipes = await db.Set<Recipe>()
                .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient).ThenInclude(i => i!.Tags)
                .ToListAsync();
            var inventory = await db.Set<InventoryItem>().ToListAsync();
            var today = DateOnly.FromDateTime(DateTime.Now);

            _allCards = recipes.Select(recipe => new RecipeCard(
                recipe,
                RecipeNutritionCalculator.CalculatePerServing(recipe.Ingredients, Math.Max(recipe.Servings, 1)),
                RecipeCompatibilityChecker.CheckRecipe(recipe.Ingredients),
                RecipeStockChecker.CheckStock(recipe.Ingredients, inventory, today)
            )).ToList();

            ApplyFilters();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilters()
    {
        IEnumerable<RecipeCard> query = _allCards;

        if (ExcludeDisliked)
        {
            query = query.Where(c => !c.HasDislikedIngredient);
        }

        if (!double.IsNaN(MaxCaloriesPerServing))
        {
            query = query.Where(c => c.PerServing.Calories <= (decimal)MaxCaloriesPerServing);
        }

        query = SortByUseItUp
            ? query.OrderByDescending(c => c.Stock.HasExpiringSoonIngredient).ThenBy(c => c.Recipe.Name)
            : query.OrderBy(c => c.Recipe.Name);

        VisibleRecipes.Clear();
        foreach (var card in query)
        {
            VisibleRecipes.Add(card);
        }
    }
}

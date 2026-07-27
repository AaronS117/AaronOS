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
        : $"Missing {string.Join(", ", Stock.MissingIngredientNames)}";

    public string ServingsDisplay => Recipe.Servings == 1 ? "1 serving" : $"{Recipe.Servings} servings";

    public string IngredientCountDisplay => Recipe.Ingredients.Count == 1
        ? "1 ingredient"
        : $"{Recipe.Ingredients.Count} ingredients";

    /// <summary>The single most important caveat, so a card carries one line rather than a list.</summary>
    public string? TopConcern => Concerns
        .OrderByDescending(c => c.Level)
        .FirstOrDefault()?.Message;

    public bool HasConcern => Concerns.Count > 0;

    public bool UseItUp => Stock.HasExpiringSoonIngredient;
}

/// <summary>One row of the dashboard's expiring-soon strip.</summary>
public record ExpiringRow(string Name, string Where, string FreshnessText, bool IsExpired);

public partial class NutritionDashboardViewModel(IDbContextFactory<AaronOsDbContext> dbContextFactory) : ViewModelBase
{
    private List<RecipeCard> _allCards = [];

    public ObservableCollection<RecipeCard> VisibleRecipes { get; } = [];
    public ObservableCollection<ExpiringRow> ExpiringSoon { get; } = [];

    [ObservableProperty]
    private bool _excludeDisliked = true;

    [ObservableProperty]
    private double _maxCaloriesPerServing = double.NaN;

    [ObservableProperty]
    private bool _sortByUseItUp;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private int _makeableCount;

    [ObservableProperty]
    private int _recipeTotal;

    [ObservableProperty]
    private int _ratedIngredientCount;

    [ObservableProperty]
    private int _inventoryCount;

    [ObservableProperty]
    private bool _hasRecipes;

    [ObservableProperty]
    private bool _hasExpiringSoon;

    [ObservableProperty]
    private bool _isFiltered;

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
            var inventory = await db.Set<InventoryItem>()
                .Include(i => i.Ingredient)
                .ToListAsync();
            var today = DateOnly.FromDateTime(DateTime.Now);

            _allCards = recipes.Select(recipe => new RecipeCard(
                recipe,
                RecipeNutritionCalculator.CalculatePerServing(recipe.Ingredients, Math.Max(recipe.Servings, 1)),
                RecipeCompatibilityChecker.CheckRecipe(recipe.Ingredients),
                RecipeStockChecker.CheckStock(recipe.Ingredients, inventory, today)
            )).ToList();

            RecipeTotal = _allCards.Count;
            HasRecipes = _allCards.Count > 0;
            MakeableCount = _allCards.Count(c => c.Stock.HasEverything && !c.HasDislikedIngredient);
            InventoryCount = inventory.Count;
            RatedIngredientCount = await db.Set<Ingredient>().CountAsync(i => i.Rating != null);

            ExpiringSoon.Clear();
            foreach (var item in inventory
                .Where(i => i.IsExpired || i.IsExpiringSoon)
                .OrderBy(i => i.ExpiresOn))
            {
                ExpiringSoon.Add(new ExpiringRow(
                    item.Ingredient?.Name ?? "Unknown",
                    item.StorageLocation.ToString(),
                    item.FreshnessText,
                    item.IsExpired));
            }
            HasExpiringSoon = ExpiringSoon.Count > 0;

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

        // Says so when filters are hiding recipes, rather than quietly showing a subset.
        IsFiltered = _allCards.Count > VisibleRecipes.Count;
        StatusMessage = IsFiltered
            ? $"Showing {VisibleRecipes.Count} of {_allCards.Count}. {_allCards.Count - VisibleRecipes.Count} hidden by filters."
            : "";
    }
}

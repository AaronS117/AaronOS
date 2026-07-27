using AaronOS.Modules.Nutrition.Calculations;
using AaronOS.Modules.Nutrition.Data;

namespace AaronOS.Modules.Nutrition.Tests;

public class RecipeStockCheckerTests
{
    private static readonly DateOnly Today = new(2026, 7, 27);

    private static RecipeIngredient Line(int ingredientId, string name) =>
        new() { IngredientId = ingredientId, Ingredient = new Ingredient { Name = name } };

    [Fact]
    public void ReportsHasEverything_WhenAllIngredientsHaveInventory()
    {
        var lines = new List<RecipeIngredient> { Line(1, "Chicken"), Line(2, "Rice") };
        var inventory = new List<InventoryItem>
        {
            new() { IngredientId = 1, DateAcquired = Today },
            new() { IngredientId = 2, DateAcquired = Today },
        };

        var result = RecipeStockChecker.CheckStock(lines, inventory, Today);

        Assert.True(result.HasEverything);
        Assert.Empty(result.MissingIngredientNames);
    }

    [Fact]
    public void ReportsMissingIngredients_ByName()
    {
        var lines = new List<RecipeIngredient> { Line(1, "Chicken"), Line(2, "Rice") };
        var inventory = new List<InventoryItem> { new() { IngredientId = 1, DateAcquired = Today } };

        var result = RecipeStockChecker.CheckStock(lines, inventory, Today);

        Assert.False(result.HasEverything);
        Assert.Equal(["Rice"], result.MissingIngredientNames);
    }

    [Fact]
    public void FlagsExpiringSoon_WithinThreshold()
    {
        var lines = new List<RecipeIngredient> { Line(1, "Chicken") };
        var inventory = new List<InventoryItem>
        {
            new() { IngredientId = 1, DateAcquired = Today, ExpiresOn = Today.AddDays(2) }
        };

        var result = RecipeStockChecker.CheckStock(lines, inventory, Today, expiringSoonWithinDays: 3);

        Assert.True(result.HasExpiringSoonIngredient);
    }

    [Fact]
    public void DoesNotFlagExpiringSoon_WhenBeyondThreshold()
    {
        var lines = new List<RecipeIngredient> { Line(1, "Chicken") };
        var inventory = new List<InventoryItem>
        {
            new() { IngredientId = 1, DateAcquired = Today, ExpiresOn = Today.AddDays(10) }
        };

        var result = RecipeStockChecker.CheckStock(lines, inventory, Today, expiringSoonWithinDays: 3);

        Assert.False(result.HasExpiringSoonIngredient);
    }

    [Fact]
    public void DoesNotFlagExpiringSoon_ForAlreadyExpiredItems()
    {
        var lines = new List<RecipeIngredient> { Line(1, "Chicken") };
        var inventory = new List<InventoryItem>
        {
            new() { IngredientId = 1, DateAcquired = Today, ExpiresOn = Today.AddDays(-1) }
        };

        var result = RecipeStockChecker.CheckStock(lines, inventory, Today, expiringSoonWithinDays: 3);

        Assert.False(result.HasExpiringSoonIngredient);
    }
}

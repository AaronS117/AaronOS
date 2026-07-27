using AaronOS.Modules.Nutrition.Calculations;
using AaronOS.Modules.Nutrition.Data;

namespace AaronOS.Modules.Nutrition.Tests;

public class RecipeNutritionCalculatorTests
{
    private static RecipeIngredient Line(decimal quantityGrams, decimal caloriesPer100g, decimal proteinPer100g, decimal costPer100g) => new()
    {
        QuantityGrams = quantityGrams,
        Ingredient = new Ingredient
        {
            Name = "Test Ingredient",
            CaloriesPer100g = caloriesPer100g,
            ProteinPer100g = proteinPer100g,
            CostPer100g = costPer100g
        }
    };

    [Fact]
    public void CalculateTotals_SumsAcrossIngredients_ScaledByQuantity()
    {
        var lines = new List<RecipeIngredient>
        {
            Line(quantityGrams: 200, caloriesPer100g: 150, proteinPer100g: 20, costPer100g: 1.00m),
            Line(quantityGrams: 50, caloriesPer100g: 400, proteinPer100g: 5, costPer100g: 2.00m),
        };

        var totals = RecipeNutritionCalculator.CalculateTotals(lines);

        Assert.Equal(500m, totals.Calories); // 200/100*150 + 50/100*400 = 300 + 200
        Assert.Equal(42.5m, totals.Protein);  // 200/100*20 + 50/100*5 = 40 + 2.5
        Assert.Equal(3.00m, totals.Cost);     // 200/100*1 + 50/100*2 = 2 + 1
    }

    [Fact]
    public void CalculatePerServing_DividesTotalsByServings()
    {
        var lines = new List<RecipeIngredient> { Line(quantityGrams: 400, caloriesPer100g: 100, proteinPer100g: 10, costPer100g: 1.00m) };

        var perServing = RecipeNutritionCalculator.CalculatePerServing(lines, servings: 4);

        Assert.Equal(100m, perServing.Calories); // 400 total / 4 servings
        Assert.Equal(10m, perServing.Protein);
    }

    [Fact]
    public void CalculatePerServing_Throws_WhenServingsIsZeroOrNegative()
    {
        var lines = new List<RecipeIngredient> { Line(100, 100, 10, 1.00m) };

        Assert.Throws<ArgumentOutOfRangeException>(() => RecipeNutritionCalculator.CalculatePerServing(lines, servings: 0));
    }

    [Fact]
    public void CalculateTotals_TreatsMissingNutritionFieldsAsZero()
    {
        var lines = new List<RecipeIngredient>
        {
            new() { QuantityGrams = 100, Ingredient = new Ingredient { Name = "Unrated" } }
        };

        var totals = RecipeNutritionCalculator.CalculateTotals(lines);

        Assert.Equal(0m, totals.Calories);
        Assert.Equal(0m, totals.Cost);
    }
}

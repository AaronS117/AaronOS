using AaronOS.Modules.Nutrition.Data;

namespace AaronOS.Modules.Nutrition.Calculations;

public record RecipeNutritionTotals(
    decimal Calories, decimal Protein, decimal Fat, decimal Carbs, decimal Fiber, decimal SodiumMg, decimal Cost);

public static class RecipeNutritionCalculator
{
    public static RecipeNutritionTotals CalculateTotals(IEnumerable<RecipeIngredient> ingredients)
    {
        decimal calories = 0, protein = 0, fat = 0, carbs = 0, fiber = 0, sodium = 0, cost = 0;

        foreach (var ri in ingredients)
        {
            var ingredient = ri.Ingredient
                ?? throw new InvalidOperationException($"RecipeIngredient {ri.Id} has no loaded Ingredient.");
            var factor = ri.QuantityGrams / 100m;

            calories += factor * (ingredient.CaloriesPer100g ?? 0);
            protein += factor * (ingredient.ProteinPer100g ?? 0);
            fat += factor * (ingredient.FatPer100g ?? 0);
            carbs += factor * (ingredient.CarbsPer100g ?? 0);
            fiber += factor * (ingredient.FiberPer100g ?? 0);
            sodium += factor * (ingredient.SodiumMgPer100g ?? 0);
            cost += factor * (ingredient.CostPer100g ?? 0);
        }

        return new RecipeNutritionTotals(calories, protein, fat, carbs, fiber, sodium, cost);
    }

    public static RecipeNutritionTotals CalculatePerServing(IEnumerable<RecipeIngredient> ingredients, int servings)
    {
        if (servings <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(servings), "Servings must be at least 1.");
        }

        var totals = CalculateTotals(ingredients);
        return new RecipeNutritionTotals(
            totals.Calories / servings,
            totals.Protein / servings,
            totals.Fat / servings,
            totals.Carbs / servings,
            totals.Fiber / servings,
            totals.SodiumMg / servings,
            totals.Cost / servings);
    }
}

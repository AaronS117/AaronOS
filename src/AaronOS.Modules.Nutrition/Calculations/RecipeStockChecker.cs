using AaronOS.Modules.Nutrition.Data;

namespace AaronOS.Modules.Nutrition.Calculations;

public record IngredientStockStatus(int IngredientId, string IngredientName, bool InStock, bool ExpiringSoon);

public record RecipeStockResult(bool HasEverything, List<IngredientStockStatus> Ingredients)
{
    public List<string> MissingIngredientNames =>
        Ingredients.Where(i => !i.InStock).Select(i => i.IngredientName).ToList();

    public bool HasExpiringSoonIngredient => Ingredients.Any(i => i.InStock && i.ExpiringSoon);
}

/// <summary>
/// Reports per-ingredient in-stock/missing for a recipe against the current inventory, and
/// whether any in-stock ingredient is within expiringSoonWithinDays of its ExpiresOn (already-
/// expired items don't count as "expiring soon" — they're a separate, worse state the Inventory
/// page flags directly).
/// </summary>
public static class RecipeStockChecker
{
    public static RecipeStockResult CheckStock(
        IEnumerable<RecipeIngredient> recipeIngredients,
        IEnumerable<InventoryItem> inventory,
        DateOnly today,
        int expiringSoonWithinDays = 3)
    {
        var inventoryByIngredient = inventory
            .GroupBy(i => i.IngredientId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var statuses = new List<IngredientStockStatus>();

        foreach (var ri in recipeIngredients)
        {
            var ingredient = ri.Ingredient
                ?? throw new InvalidOperationException($"RecipeIngredient {ri.Id} has no loaded Ingredient.");

            var hasStock = inventoryByIngredient.TryGetValue(ri.IngredientId, out var items);
            var expiringSoon = hasStock && items!.Any(i =>
            {
                if (i.ExpiresOn is not { } expires)
                {
                    return false;
                }

                var daysLeft = expires.DayNumber - today.DayNumber;
                return daysLeft >= 0 && daysLeft <= expiringSoonWithinDays;
            });

            statuses.Add(new IngredientStockStatus(ri.IngredientId, ingredient.Name, hasStock, expiringSoon));
        }

        return new RecipeStockResult(statuses.All(s => s.InStock), statuses);
    }
}

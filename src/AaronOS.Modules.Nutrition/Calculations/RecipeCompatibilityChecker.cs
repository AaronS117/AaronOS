using AaronOS.Modules.Nutrition.Data;

namespace AaronOS.Modules.Nutrition.Calculations;

public enum CompatibilityLevel { Clear, Caution, Blocked }

public record CompatibilityConcern(CompatibilityLevel Level, string Message)
{
    /// <summary>Lets XAML colour a hard flag differently from a soft one with a single DataTrigger.</summary>
    public bool IsBlocked => Level == CompatibilityLevel.Blocked;
}

/// <summary>
/// Flags a recipe's ingredients against preferences: a hard flag for anything rated Dislike, a
/// soft flag for unrated ingredients sharing a tag with a Dislike-rated ingredient (never
/// auto-assumes a rating — just a hint), and a soft note when a recipe's FormUsed differs from
/// the ingredient's PreferredForm.
/// </summary>
public static class RecipeCompatibilityChecker
{
    public static List<CompatibilityConcern> CheckRecipe(IEnumerable<RecipeIngredient> recipeIngredients)
    {
        var items = recipeIngredients.ToList();
        var dislikedTags = items
            .Select(ri => ri.Ingredient)
            .Where(i => i is not null && i.Rating == Rating.Dislike)
            .SelectMany(i => i!.Tags)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var concerns = new List<CompatibilityConcern>();

        foreach (var ri in items)
        {
            var ingredient = ri.Ingredient
                ?? throw new InvalidOperationException($"RecipeIngredient {ri.Id} has no loaded Ingredient.");

            if (ingredient.Rating == Rating.Dislike)
            {
                concerns.Add(new CompatibilityConcern(
                    CompatibilityLevel.Blocked, $"Contains disliked ingredient: {ingredient.Name}."));
                continue;
            }

            if (ingredient.Rating is null)
            {
                var sharedTag = ingredient.Tags.FirstOrDefault(t => dislikedTags.Contains(t.Name));
                if (sharedTag is not null)
                {
                    concerns.Add(new CompatibilityConcern(
                        CompatibilityLevel.Caution,
                        $"Possible dislike (tagged {sharedTag.Name}): {ingredient.Name}."));
                }
            }

            if (!string.IsNullOrWhiteSpace(ri.FormUsed)
                && !string.IsNullOrWhiteSpace(ingredient.PreferredForm)
                && !string.Equals(ri.FormUsed, ingredient.PreferredForm, StringComparison.OrdinalIgnoreCase))
            {
                concerns.Add(new CompatibilityConcern(
                    CompatibilityLevel.Caution,
                    $"You prefer {ingredient.Name} {ingredient.PreferredForm}; this recipe uses {ri.FormUsed}."));
            }
        }

        return concerns;
    }
}

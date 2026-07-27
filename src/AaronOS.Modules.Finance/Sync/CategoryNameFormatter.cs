namespace AaronOS.Modules.Finance.Sync;

/// <summary>Turns Plaid's SCREAMING_SNAKE_CASE category codes (e.g. "FOOD_AND_DRINK") into a
/// readable label ("Food And Drink") for display.</summary>
public static class CategoryNameFormatter
{
    public static string Humanize(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return "Uncategorized";
        }

        var words = category.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }
}

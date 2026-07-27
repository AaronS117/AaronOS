using System.IO;
using System.Text.Json;
using AaronOS.Modules.Nutrition.Data;

namespace AaronOS.Modules.Nutrition.ShelfLife;

/// <summary>
/// Estimates an expiration date from a hand-curated FDA FoodKeeper-style reference dataset,
/// matched by case-insensitive keyword containment against the ingredient name (first match in
/// list order wins — see Resources/ShelfLifeReference.json's ordering note). Takes the JSON text
/// directly via the constructor rather than loading the embedded resource itself, so the matching
/// logic is testable without touching the assembly's resource stream.
/// </summary>
public class ShelfLifeEstimator
{
    private readonly List<ShelfLifeReferenceEntry> _entries;

    public ShelfLifeEstimator(string referenceJson)
    {
        _entries = JsonSerializer.Deserialize<List<ShelfLifeReferenceEntry>>(referenceJson) ?? [];
    }

    public static ShelfLifeEstimator LoadFromEmbeddedResource()
    {
        var assembly = typeof(ShelfLifeEstimator).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "AaronOS.Modules.Nutrition.Resources.ShelfLifeReference.json")
            ?? throw new InvalidOperationException("ShelfLifeReference.json embedded resource not found.");
        using var reader = new StreamReader(stream);
        return new ShelfLifeEstimator(reader.ReadToEnd());
    }

    public ShelfLifeReferenceEntry? FindMatch(string ingredientName) =>
        _entries.FirstOrDefault(e => ingredientName.Contains(e.Keyword, StringComparison.OrdinalIgnoreCase));

    public DateOnly? EstimateExpiration(string ingredientName, StorageLocation storageLocation, DateOnly dateAcquired)
    {
        var match = FindMatch(ingredientName);
        if (match is null)
        {
            return null;
        }

        var days = storageLocation switch
        {
            StorageLocation.Fridge => match.FridgeDays,
            StorageLocation.Freezer => match.FreezerDays,
            StorageLocation.Pantry => match.PantryDays,
            _ => throw new ArgumentOutOfRangeException(nameof(storageLocation))
        };

        return dateAcquired.AddDays(days);
    }
}

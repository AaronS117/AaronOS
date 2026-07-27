namespace AaronOS.Modules.Nutrition.Seeding;

public record IngredientSeedEntry(
    string Name,
    decimal? CaloriesPer100g,
    decimal? ProteinPer100g,
    decimal? FatPer100g,
    decimal? CarbsPer100g,
    decimal? FiberPer100g,
    decimal? SodiumMgPer100g);

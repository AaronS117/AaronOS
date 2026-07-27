using AaronOS.Modules.Nutrition.Seeding;

namespace AaronOS.Modules.Nutrition.Tests;

public class IngredientSeederTests
{
    private const string SampleJson = """
        [
          { "Name": "Chicken breast, raw", "CaloriesPer100g": 120, "ProteinPer100g": 22.5, "FatPer100g": 2.6, "CarbsPer100g": 0, "FiberPer100g": 0, "SodiumMgPer100g": 45 },
          { "Name": "Apple, raw", "CaloriesPer100g": 52, "ProteinPer100g": 0.3, "FatPer100g": 0.2, "CarbsPer100g": 13.8, "FiberPer100g": 2.4, "SodiumMgPer100g": 1 }
        ]
        """;

    [Fact]
    public void ParseSeedFile_MapsEveryEntryToAnIngredient()
    {
        var ingredients = IngredientSeeder.ParseSeedFile(SampleJson);

        Assert.Equal(2, ingredients.Count);
        Assert.Equal("Chicken breast, raw", ingredients[0].Name);
        Assert.Equal(120m, ingredients[0].CaloriesPer100g);
        Assert.Equal(22.5m, ingredients[0].ProteinPer100g);
    }

    [Fact]
    public void ParseSeedFile_LeavesRatingTagsAndCostUnset()
    {
        var ingredients = IngredientSeeder.ParseSeedFile(SampleJson);

        Assert.All(ingredients, i =>
        {
            Assert.Null(i.Rating);
            Assert.Empty(i.Tags);
            Assert.Null(i.CostPer100g);
        });
    }

    [Fact]
    public void ParseSeedFile_ReturnsEmptyList_ForEmptyJsonArray()
    {
        var ingredients = IngredientSeeder.ParseSeedFile("[]");

        Assert.Empty(ingredients);
    }
}

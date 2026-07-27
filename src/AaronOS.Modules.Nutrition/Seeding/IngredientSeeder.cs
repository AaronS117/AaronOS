using System.IO;
using System.Text.Json;
using AaronOS.Core.Data;
using AaronOS.Modules.Nutrition.Data;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Nutrition.Seeding;

public static class IngredientSeeder
{
    /// <summary>Pure parse step, kept separate from the DB write so it's testable without EF.</summary>
    public static List<Ingredient> ParseSeedFile(string json)
    {
        var entries = JsonSerializer.Deserialize<List<IngredientSeedEntry>>(json) ?? [];
        return entries.Select(e => new Ingredient
        {
            Name = e.Name,
            CaloriesPer100g = e.CaloriesPer100g,
            ProteinPer100g = e.ProteinPer100g,
            FatPer100g = e.FatPer100g,
            CarbsPer100g = e.CarbsPer100g,
            FiberPer100g = e.FiberPer100g,
            SodiumMgPer100g = e.SodiumMgPer100g
        }).ToList();
    }

    /// <summary>No-op if any Ingredient rows already exist — safe to call on every dashboard load.</summary>
    public static async Task SeedIfEmptyAsync(AaronOsDbContext db)
    {
        if (await db.Set<Ingredient>().AnyAsync())
        {
            return;
        }

        var assembly = typeof(IngredientSeeder).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "AaronOS.Modules.Nutrition.Resources.IngredientSeed.json");
        if (stream is null)
        {
            return;
        }

        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        var ingredients = ParseSeedFile(json);

        db.Set<Ingredient>().AddRange(ingredients);
        await db.SaveChangesAsync();
    }
}

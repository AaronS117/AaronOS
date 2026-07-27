namespace AaronOS.Modules.Nutrition.Data;

public class Ingredient
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public Rating? Rating { get; set; }
    public string? PreferredForm { get; set; }
    public decimal? CaloriesPer100g { get; set; }
    public decimal? ProteinPer100g { get; set; }
    public decimal? FatPer100g { get; set; }
    public decimal? CarbsPer100g { get; set; }
    public decimal? FiberPer100g { get; set; }
    public decimal? SodiumMgPer100g { get; set; }
    public decimal? CostPer100g { get; set; }
    public int? FdcId { get; set; }
    public List<Tag> Tags { get; set; } = [];
}

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

    // Read-only computed display members. EF ignores getter-only properties, so no [NotMapped] is
    // needed — same convention as FinanceAccount.SignedBalance/IsLiability.

    public bool IsLiked => Rating == Data.Rating.Like;
    public bool IsDisliked => Rating == Data.Rating.Dislike;

    public string RatingDisplay => Rating switch
    {
        Data.Rating.Like => "Like",
        Data.Rating.Dislike => "Dislike",
        Data.Rating.Neutral => "Neutral",
        _ => "—"
    };

    /// <summary>Requires Tags to have been Include()d; shows an em dash when untagged.</summary>
    public string TagsDisplay => Tags.Count == 0 ? "—" : string.Join(", ", Tags.Select(t => t.Name));

    public string CaloriesDisplay => CaloriesPer100g?.ToString("0") ?? "—";

    public string CostDisplay => CostPer100g is { } cost ? cost.ToString("C") : "—";
}

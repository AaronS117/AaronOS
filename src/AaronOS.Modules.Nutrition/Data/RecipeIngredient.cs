namespace AaronOS.Modules.Nutrition.Data;

public class RecipeIngredient
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public Recipe? Recipe { get; set; }
    public int IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    public decimal QuantityGrams { get; set; }
    public string? DisplayAmount { get; set; }
    public string? FormUsed { get; set; }
}

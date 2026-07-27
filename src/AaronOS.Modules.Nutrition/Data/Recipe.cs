namespace AaronOS.Modules.Nutrition.Data;

public class Recipe
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Instructions { get; set; }
    public int Servings { get; set; } = 1;
    public List<RecipeIngredient> Ingredients { get; set; } = [];
}

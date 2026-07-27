namespace AaronOS.Modules.Nutrition.Data;

public class Tag
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public List<Ingredient> Ingredients { get; set; } = [];
}

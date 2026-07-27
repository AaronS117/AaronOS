namespace AaronOS.Modules.Nutrition.Data;

public class InventoryItem
{
    public int Id { get; set; }
    public int IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    public StorageLocation StorageLocation { get; set; }
    public DateOnly DateAcquired { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? QuantityLabel { get; set; }
    public string? Notes { get; set; }
}

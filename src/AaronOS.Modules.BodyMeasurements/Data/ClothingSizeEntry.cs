namespace AaronOS.Modules.BodyMeasurements.Data;

public class ClothingSizeEntry
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public ClothingCategory Category { get; set; }
    public string SizeLabel { get; set; } = "";
    public string? Brand { get; set; }
    public string? Notes { get; set; }
}

namespace AaronOS.Modules.BodyMeasurements.Data;

/// <summary>One logged check-in. All measurement fields are optional — log only what you measured that day.</summary>
public class BodyCheckIn
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public decimal? WeightLb { get; set; }
    public decimal? NeckIn { get; set; }
    public decimal? ChestIn { get; set; }
    public decimal? WaistIn { get; set; }
    public decimal? HipsIn { get; set; }
    public decimal? BicepLeftIn { get; set; }
    public decimal? BicepRightIn { get; set; }
    public decimal? ThighLeftIn { get; set; }
    public decimal? ThighRightIn { get; set; }
    public decimal? CalfLeftIn { get; set; }
    public decimal? CalfRightIn { get; set; }
    public string? Notes { get; set; }
}

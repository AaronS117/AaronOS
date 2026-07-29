namespace AaronOS.Modules.Trading.Data;

/// <summary>
/// The highest price a held position has reached, so a trailing stop has something to trail from.
///
/// Kept here rather than derived from the broker because a broker reports what a position cost and what
/// it is worth, never what it peaked at in between — and the peak is the entire basis of a trailing
/// stop. Reset when a position is closed, so a later re-entry starts fresh rather than measuring against
/// a high from a previous holding.
/// </summary>
public class PositionPeak
{
    public int Id { get; set; }
    public string Symbol { get; set; } = "";
    public decimal PeakPrice { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

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

    /// <summary>
    /// When the trailing stop last sold this symbol, or null if it never has.
    ///
    /// The row outlives the position on purpose. Without it a stop sells and the model, seeing no
    /// holding and a brief that says to hold the index, buys straight back on the next cycle fifteen
    /// minutes later — paying the spread twice and calling it risk management.
    /// </summary>
    public DateTime? StoppedOutUtc { get; set; }
}

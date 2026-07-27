namespace AaronOS.Modules.Medical.Data;

public class LabResult
{
    public int Id { get; set; }
    public required string TestName { get; set; }

    /// <summary>Numeric result when there is one. Null for textual results.</summary>
    public decimal? Value { get; set; }

    /// <summary>Textual result, kept alongside Value because real results include "Negative" and
    /// "&lt;0.01" — forcing those into a decimal would lose them.</summary>
    public string? ValueText { get; set; }

    public string? Unit { get; set; }
    public decimal? ReferenceLow { get; set; }
    public decimal? ReferenceHigh { get; set; }
    public DateOnly? TakenOn { get; set; }
    public RecordSource Source { get; set; } = RecordSource.Manual;
    public string? ExternalId { get; set; }

    public bool IsImported => Source == RecordSource.Imported;
    public bool HasReferenceRange => ReferenceLow is not null || ReferenceHigh is not null;

    /// <summary>True only when a numeric value falls outside a known bound. A one-sided range is
    /// still usable; no value, or no range at all, is never "out of range".</summary>
    public bool IsOutOfRange => Value is { } v
        && ((ReferenceLow is { } lo && v < lo) || (ReferenceHigh is { } hi && v > hi));

    public string ValueDisplay => Value is { } v
        ? (string.IsNullOrWhiteSpace(Unit) ? v.ToString("0.##") : $"{v.ToString("0.##")} {Unit}")
        : (string.IsNullOrWhiteSpace(ValueText) ? "—" : ValueText!);

    public string RangeDisplay => (ReferenceLow, ReferenceHigh) switch
    {
        ({ } lo, { } hi) => $"{lo.ToString("0.##")}–{hi.ToString("0.##")}",
        ({ } lo, null) => $"≥ {lo.ToString("0.##")}",
        (null, { } hi) => $"≤ {hi.ToString("0.##")}",
        _ => "—"
    };

    public string TakenDisplay => TakenOn?.ToString("MMM d, yyyy") ?? "—";
}

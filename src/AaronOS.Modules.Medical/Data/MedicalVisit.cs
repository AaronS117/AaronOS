namespace AaronOS.Modules.Medical.Data;

public class MedicalVisit
{
    public int Id { get; set; }
    public DateOnly? Date { get; set; }
    public string? VisitType { get; set; }
    public int? ProviderId { get; set; }
    public Provider? Provider { get; set; }
    public string? Facility { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public RecordSource Source { get; set; } = RecordSource.Manual;
    public string? ExternalId { get; set; }

    public bool IsImported => Source == RecordSource.Imported;
    public string DateDisplay => Date?.ToString("MMM d, yyyy") ?? "—";
    public string TypeDisplay => string.IsNullOrWhiteSpace(VisitType) ? "Visit" : VisitType;
    public string WhereDisplay => string.IsNullOrWhiteSpace(Facility) ? "—" : Facility;
    public string ReasonDisplay => string.IsNullOrWhiteSpace(Reason) ? "—" : Reason;
    public string ProviderDisplay => Provider?.Name ?? "—";

    /// <summary>Label used when a document points at this visit.</summary>
    public string ShortLabel => $"{DateDisplay} · {TypeDisplay}";
}

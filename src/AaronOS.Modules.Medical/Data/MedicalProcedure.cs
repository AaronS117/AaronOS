namespace AaronOS.Modules.Medical.Data;

public class MedicalProcedure
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateOnly? Date { get; set; }
    public int? ProviderId { get; set; }
    public Provider? Provider { get; set; }
    public string? Facility { get; set; }
    public string? Notes { get; set; }
    public RecordSource Source { get; set; } = RecordSource.Manual;
    public string? ExternalId { get; set; }

    public bool IsImported => Source == RecordSource.Imported;
    public string DateDisplay => Date?.ToString("MMM d, yyyy") ?? "—";
    public string FacilityDisplay => string.IsNullOrWhiteSpace(Facility) ? "—" : Facility;
}

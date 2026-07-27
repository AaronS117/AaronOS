namespace AaronOS.Modules.Medical.Data;

public class Immunization
{
    public int Id { get; set; }
    public required string Vaccine { get; set; }
    public DateOnly? DateGiven { get; set; }
    public int? DoseNumber { get; set; }
    public string? Notes { get; set; }
    public RecordSource Source { get; set; } = RecordSource.Manual;
    public string? ExternalId { get; set; }

    public bool IsImported => Source == RecordSource.Imported;
    public string DateDisplay => DateGiven?.ToString("MMM d, yyyy") ?? "—";
    public string DoseDisplay => DoseNumber is { } n ? $"Dose {n}" : "—";
}

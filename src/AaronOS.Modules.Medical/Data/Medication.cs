namespace AaronOS.Modules.Medical.Data;

public class Medication
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Dose { get; set; }
    public string? Frequency { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? ProviderId { get; set; }
    public Provider? Provider { get; set; }
    public string? Notes { get; set; }
    public RecordSource Source { get; set; } = RecordSource.Manual;
    public string? ExternalId { get; set; }

    /// <summary>Active until an end date has passed. No end date means it is still being taken.</summary>
    public bool IsActive => EndDate is null || EndDate.Value >= DateOnly.FromDateTime(DateTime.Now);
    public bool IsImported => Source == RecordSource.Imported;

    public string DoseDisplay
    {
        get
        {
            var parts = new[] { Dose, Frequency }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            return parts.Length == 0 ? "—" : string.Join(" · ", parts);
        }
    }

    public string StartedDisplay => StartDate?.ToString("MMM yyyy") ?? "—";
    public string ProviderDisplay => Provider?.Name ?? "—";
}

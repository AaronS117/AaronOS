namespace AaronOS.Modules.Medical.Data;

public class Allergy
{
    public int Id { get; set; }
    public required string Substance { get; set; }
    public string? Reaction { get; set; }
    public AllergySeverity Severity { get; set; } = AllergySeverity.Unknown;
    public string? Notes { get; set; }
    public RecordSource Source { get; set; } = RecordSource.Manual;
    public string? ExternalId { get; set; }

    public bool IsSevere => Severity == AllergySeverity.Severe;
    public bool IsModerate => Severity == AllergySeverity.Moderate;
    public bool IsImported => Source == RecordSource.Imported;
    public string SeverityDisplay => Severity == AllergySeverity.Unknown ? "—" : Severity.ToString();
    public string ReactionDisplay => string.IsNullOrWhiteSpace(Reaction) ? "—" : Reaction;
}

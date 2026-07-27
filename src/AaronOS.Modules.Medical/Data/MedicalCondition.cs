namespace AaronOS.Modules.Medical.Data;

public class MedicalCondition
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Code { get; set; }
    public DateOnly? OnsetDate { get; set; }
    public DateOnly? ResolvedDate { get; set; }
    public ConditionStatus Status { get; set; } = ConditionStatus.Active;
    public string? Notes { get; set; }
    public RecordSource Source { get; set; } = RecordSource.Manual;
    public string? ExternalId { get; set; }

    // Getter-only computed display members: EF ignores them, so no [NotMapped] is needed. Same
    // convention as FinanceTransaction.DateDisplay — compute the shape the UI wants here so XAML
    // binds one plain property instead of running a value converter.
    public bool IsActive => Status != ConditionStatus.Resolved;
    public bool IsImported => Source == RecordSource.Imported;
    public string OnsetDisplay => OnsetDate?.ToString("MMM yyyy") ?? "—";
    public string StatusDisplay => Status.ToString();
}

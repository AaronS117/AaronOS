namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// One logged completion. Next-due is always derived from these rows rather than stored on
/// <see cref="Routine"/>: a stored "next due" column would need rewriting on every completion and
/// would drift silently if a completion were later edited or deleted.
/// </summary>
public class RoutineCompletion
{
    public int Id { get; set; }
    public int RoutineId { get; set; }
    public DateTime CompletedAt { get; set; }
    public string? Note { get; set; }
}

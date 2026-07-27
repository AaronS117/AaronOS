namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// A dated override on top of the recurring template. Two shapes, distinguished by whether
/// <see cref="ScheduleBlockId"/> is set:
/// a modification of a template block (cancel it for PTO, or replace its times for a short day),
/// or a standalone one-off entry that no template block produced.
/// </summary>
public class ScheduleException
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }

    /// <summary>Null for a standalone one-off entry.</summary>
    public int? ScheduleBlockId { get; set; }

    /// <summary>True means the referenced block does not occur on <see cref="Date"/>.</summary>
    public bool IsCancelled { get; set; }

    /// <summary>Required for a standalone entry; ignored when modifying a block.</summary>
    public ScheduleBlockKind? Kind { get; set; }

    public string? Label { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public string? Note { get; set; }

    public bool IsStandalone => ScheduleBlockId is null;
}

namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// One recurring entry in the weekly template — "work, Mon-Fri, 8 to 5". Reality is layered on
/// top with <see cref="ScheduleException"/> rather than by editing these rows.
/// </summary>
public class ScheduleBlock
{
    public int Id { get; set; }
    public ScheduleBlockKind Kind { get; set; }
    public string Label { get; set; } = "";
    public DayOfWeekFlags DaysOfWeek { get; set; }

    /// <summary>Local wall-clock time of day.</summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>Local wall-clock time of day. When less than <see cref="StartTime"/> the block
    /// wraps past midnight into the following day — which is how a sleep block is expressed.</summary>
    public TimeSpan EndTime { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Null means open-ended.</summary>
    public DateOnly? EffectiveTo { get; set; }

    public bool IsActive { get; set; } = true;

    public bool WrapsMidnight => EndTime < StartTime;
}

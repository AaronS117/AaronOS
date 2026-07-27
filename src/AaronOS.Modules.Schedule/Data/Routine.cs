namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// A recurring chore. Exactly one of <see cref="IntervalDays"/> and
/// <see cref="PreferredDaysOfWeek"/> drives its due date: the litter box is an interval
/// ("every 2 days"), trash night is a weekday ("Tuesdays"). Both set, or neither, is invalid.
/// </summary>
public class Routine
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public RoutineCategory Category { get; set; }

    /// <summary>Days between completions. Null when the routine is weekday-pinned instead.</summary>
    public int? IntervalDays { get; set; }

    /// <summary>Fixed weekdays. Null when the routine is interval-driven instead.</summary>
    public DayOfWeekFlags? PreferredDaysOfWeek { get; set; }

    /// <summary>A ranking hint for the suggestion engine, not a hard slot.</summary>
    public TimeSpan? PreferredTimeOfDay { get; set; }

    /// <summary>Used to check whether the routine actually fits a free gap.</summary>
    public int? EstimatedMinutes { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsIntervalDriven => IntervalDays is > 0;
}

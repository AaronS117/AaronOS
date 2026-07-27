namespace AaronOS.Modules.Schedule.Routines;

/// <param name="OverdueByDays">Zero when the routine is due today or later; otherwise how many
/// days past <see cref="NextDue"/> it is.</param>
/// <param name="IsDue">True when <see cref="NextDue"/> is on or before the date passed to
/// <see cref="RoutineScheduler.Evaluate"/>. Stored rather than computed because a record has no
/// clock of its own and must not reach for DateTime.Today.</param>
public sealed record RoutineDueState(
    int RoutineId,
    DateOnly NextDue,
    int OverdueByDays,
    DateTime? LastCompletedAt,
    bool IsDue)
{
    public bool IsOverdue => OverdueByDays > 0;
}

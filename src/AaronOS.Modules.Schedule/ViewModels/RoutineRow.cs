using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Routines;

namespace AaronOS.Modules.Schedule.ViewModels;

/// <summary>A routine paired with its computed due state, so the page binds to one object per row
/// instead of correlating two collections in XAML.</summary>
public sealed record RoutineRow(Routine Routine, RoutineDueState Due)
{
    public string Name => Routine.Name;

    public string Cadence => Routine.IntervalDays is { } days
        ? $"every {days} day{(days == 1 ? "" : "s")}"
        : $"{Routine.PreferredDaysOfWeek}";

    public string DueDisplay => Due switch
    {
        { IsOverdue: true } => $"overdue by {Due.OverdueByDays} day{(Due.OverdueByDays == 1 ? "" : "s")}",
        { IsDue: true } => "due today",
        _ => $"next {Due.NextDue:ddd MMM d}",
    };

    public string LastDoneDisplay => Due.LastCompletedAt is { } last
        ? $"last done {last:ddd MMM d}"
        : "never done";
}

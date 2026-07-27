using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Routines;

/// <summary>
/// Pure next-due computation. Takes today as a parameter rather than reading the clock so the
/// rules are testable at any date (see RoutineSchedulerTests).
/// </summary>
public static class RoutineScheduler
{
    public static IReadOnlyList<RoutineDueState> EvaluateAll(
        IReadOnlyList<Routine> routines,
        IReadOnlyList<RoutineCompletion> completions,
        DateOnly today)
    {
        var byRoutine = completions.GroupBy(c => c.RoutineId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RoutineCompletion>)g.ToList());

        return routines
            .Where(r => r.IsActive)
            .Select(r => Evaluate(
                r,
                byRoutine.TryGetValue(r.Id, out var mine) ? mine : [],
                today))
            .ToList();
    }

    public static RoutineDueState Evaluate(
        Routine routine,
        IReadOnlyList<RoutineCompletion> completions,
        DateOnly today)
    {
        var last = completions.Count == 0 ? null : (DateTime?)completions.Max(c => c.CompletedAt);

        var nextDue = routine switch
        {
            { IntervalDays: > 0 } => NextIntervalDue(routine.IntervalDays!.Value, last, today),
            { PreferredDaysOfWeek: { } days } when days != DayOfWeekFlags.None => NextWeekdayDue(days, last, today),
            _ => throw new InvalidOperationException(
                $"Routine {routine.Id} ('{routine.Name}') has neither IntervalDays nor PreferredDaysOfWeek set."),
        };

        var overdue = nextDue < today ? today.DayNumber - nextDue.DayNumber : 0;
        return new RoutineDueState(routine.Id, nextDue, overdue, last, IsDue: nextDue <= today);
    }

    private static DateOnly NextIntervalDue(int intervalDays, DateTime? last, DateOnly today)
        => last is null
            ? today                                                   // never done: do it now
            : DateOnly.FromDateTime(last.Value).AddDays(intervalDays);

    /// <summary>
    /// The earliest matching weekday that hasn't been completed. With no completion, that is the
    /// next matching weekday on or after today. With one, it is the next matching weekday strictly
    /// after the completion — which is how a missed Tuesday shows up as overdue rather than being
    /// silently rolled forward to next Tuesday.
    /// </summary>
    private static DateOnly NextWeekdayDue(DayOfWeekFlags days, DateTime? last, DateOnly today)
    {
        var searchFrom = last is null ? today : DateOnly.FromDateTime(last.Value).AddDays(1);

        for (var date = searchFrom; date < searchFrom.AddDays(8); date = date.AddDays(1))
        {
            if (days.Includes(date.DayOfWeek)) return date;
        }

        // Unreachable for any non-None flag set: eight consecutive days cover every weekday.
        throw new InvalidOperationException($"No weekday in {days} matched within 8 days of {searchFrom}.");
    }
}

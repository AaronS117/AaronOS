using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Routines;

namespace AaronOS.Modules.Schedule.Tests;

public class RoutineSchedulerTests
{
    private static readonly DateOnly Today = new(2026, 7, 10); // a Friday

    private static Routine Interval(int days) => new()
    {
        Id = 1, Name = "Scoop litter box", Category = RoutineCategory.LitterBox, IntervalDays = days,
    };

    private static Routine OnDays(DayOfWeekFlags days) => new()
    {
        Id = 2, Name = "Take out trash", Category = RoutineCategory.Trash, PreferredDaysOfWeek = days,
    };

    private static RoutineCompletion Done(int routineId, DateOnly date) =>
        new() { RoutineId = routineId, CompletedAt = date.ToDateTime(new TimeOnly(21, 0)) };

    [Fact]
    public void NeverCompletedInterval_IsDueToday()
    {
        var state = RoutineScheduler.Evaluate(Interval(2), [], Today);

        Assert.Equal(Today, state.NextDue);
        Assert.Equal(0, state.OverdueByDays);
        Assert.True(state.IsDue);
        Assert.False(state.IsOverdue);
        Assert.Null(state.LastCompletedAt);
    }

    [Fact]
    public void CompletedToday_IsNextDueAfterTheInterval()
    {
        var state = RoutineScheduler.Evaluate(Interval(2), [Done(1, Today)], Today);

        Assert.Equal(Today.AddDays(2), state.NextDue);
        Assert.False(state.IsDue);
        Assert.Equal(0, state.OverdueByDays);
    }

    [Fact]
    public void OverdueInterval_ReportsDaysPastDue()
    {
        // Completed 5 days ago on a 2-day interval: due 3 days ago.
        var state = RoutineScheduler.Evaluate(Interval(2), [Done(1, Today.AddDays(-5))], Today);

        Assert.Equal(Today.AddDays(-3), state.NextDue);
        Assert.Equal(3, state.OverdueByDays);
        Assert.True(state.IsOverdue);
    }

    [Fact]
    public void UsesTheMostRecentCompletion_NotTheFirst()
    {
        var completions = new[] { Done(1, Today.AddDays(-9)), Done(1, Today.AddDays(-1)), Done(1, Today.AddDays(-5)) };

        var state = RoutineScheduler.Evaluate(Interval(2), completions, Today);

        Assert.Equal(Today.AddDays(1), state.NextDue);
        Assert.Equal(Today.AddDays(-1).ToDateTime(new TimeOnly(21, 0)), state.LastCompletedAt);
    }

    [Fact]
    public void WeekdayPinned_IsDueOnItsWeekday()
    {
        // Today is Friday; the routine is pinned to Friday and hasn't been done today.
        var state = RoutineScheduler.Evaluate(OnDays(DayOfWeekFlags.Friday), [], Today);

        Assert.Equal(Today, state.NextDue);
        Assert.True(state.IsDue);
    }

    [Fact]
    public void WeekdayPinned_SkipsAWeekdayAlreadyCompleted()
    {
        var state = RoutineScheduler.Evaluate(OnDays(DayOfWeekFlags.Friday), [Done(2, Today)], Today);

        Assert.Equal(Today.AddDays(7), state.NextDue);
        Assert.False(state.IsDue);
    }

    [Fact]
    public void WeekdayPinned_MissedDay_IsOverdue()
    {
        // Pinned to Tuesday, last done two Tuesdays ago, today is Friday: Tuesday the 7th was missed.
        var state = RoutineScheduler.Evaluate(OnDays(DayOfWeekFlags.Tuesday), [Done(2, new DateOnly(2026, 6, 30))], Today);

        Assert.Equal(new DateOnly(2026, 7, 7), state.NextDue);
        Assert.Equal(3, state.OverdueByDays);
    }

    [Fact]
    public void EvaluateAll_SkipsInactiveRoutines_AndPartitionsCompletionsByRoutine()
    {
        var litter = Interval(2);
        var trash = OnDays(DayOfWeekFlags.Friday);
        var retired = Interval(1);
        retired.Id = 3;
        retired.IsActive = false;

        var states = RoutineScheduler.EvaluateAll([litter, trash, retired], [Done(1, Today)], Today);

        Assert.Equal([1, 2], states.Select(s => s.RoutineId));
        Assert.Equal(Today.AddDays(2), states[0].NextDue); // used only routine 1's completion
        Assert.Equal(Today, states[1].NextDue);
    }

    [Fact]
    public void MisconfiguredRoutine_ThrowsRatherThanGuessing()
    {
        var broken = new Routine { Id = 9, Name = "Neither", Category = RoutineCategory.Other };

        Assert.Throws<InvalidOperationException>(() => RoutineScheduler.Evaluate(broken, [], Today));
    }
}

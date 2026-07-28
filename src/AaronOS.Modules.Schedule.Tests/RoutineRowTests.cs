using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.Routines;
using AaronOS.Modules.Schedule.ViewModels;

namespace AaronOS.Modules.Schedule.Tests;

/// <summary>
/// Display formatting for a routine row. The weekday branch of <see cref="RoutineRow.Cadence"/> was
/// unreachable in production until the Routines page grew a weekday picker, so these pin what the
/// user actually reads for a trash-night routine.
/// </summary>
public class RoutineRowTests
{
    private static readonly DateOnly Today = new(2026, 7, 7);

    private static RoutineRow Row(Routine routine) =>
        new(routine, new RoutineDueState(routine.Id, Today, 0, null, IsDue: true));

    [Theory]
    [InlineData(DayOfWeekFlags.Tuesday, "Tuesday")]
    [InlineData(DayOfWeekFlags.Monday | DayOfWeekFlags.Thursday, "Monday, Thursday")]
    [InlineData(DayOfWeekFlags.Weekdays, "weekdays")]
    [InlineData(DayOfWeekFlags.Weekend, "weekends")]
    [InlineData(DayOfWeekFlags.EveryDay, "every day")]
    public void Cadence_ReadsAWeekdayPinnedRoutineAsItsDays(DayOfWeekFlags days, string expected)
    {
        var row = Row(new Routine { Id = 1, Name = "Take out trash", PreferredDaysOfWeek = days });

        Assert.Equal(expected, row.Cadence);
    }

    [Theory]
    [InlineData(1, "every 1 day")]
    [InlineData(2, "every 2 days")]
    public void Cadence_ReadsAnIntervalRoutineAsItsInterval(int intervalDays, string expected)
    {
        var row = Row(new Routine { Id = 1, Name = "Scoop litter box", IntervalDays = intervalDays });

        Assert.Equal(expected, row.Cadence);
    }
}

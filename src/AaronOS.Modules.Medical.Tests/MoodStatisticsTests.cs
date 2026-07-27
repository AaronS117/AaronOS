using AaronOS.Modules.Medical.Calculations;
using AaronOS.Modules.Medical.Data;

namespace AaronOS.Modules.Medical.Tests;

public class MoodStatisticsTests
{
    private static readonly DateOnly Today = new(2026, 7, 27);

    private static MoodEntry Day(int daysAgo, int mood, decimal? sleep = null) =>
        new() { Date = Today.AddDays(-daysAgo), Mood = mood, SleepHours = sleep };

    [Fact]
    public void EmptyLogSummarisesToNothing()
    {
        var s = MoodStatistics.Summarise([], Today);

        Assert.Equal(0, s.DaysLogged);
        Assert.Equal("No entries yet", s.SwingDescription);
    }

    [Fact]
    public void AveragesAndCountsWithinTheWindow()
    {
        var s = MoodStatistics.Summarise([Day(0, 2), Day(1, -2), Day(2, 0)], Today);

        Assert.Equal(3, s.DaysLogged);
        Assert.Equal(0, s.AverageMood);
        Assert.Equal(-2, s.Lowest);
        Assert.Equal(2, s.Highest);
    }

    [Fact]
    public void SwingSeparatesAnUnstableLogFromASteadyOne()
    {
        // The case that motivated reporting swing at all: identical averages, opposite realities.
        var unstable = MoodStatistics.Summarise([Day(0, 4), Day(1, -4), Day(2, 4), Day(3, -4)], Today);
        var steady = MoodStatistics.Summarise([Day(0, 0), Day(1, 0), Day(2, 0), Day(3, 0)], Today);

        Assert.Equal(steady.AverageMood, unstable.AverageMood);
        Assert.Equal(8, unstable.Swing);
        Assert.Equal(0, steady.Swing);
        Assert.Contains("Wide swing", unstable.SwingDescription);
        Assert.DoesNotContain("Wide swing", steady.SwingDescription);
    }

    [Fact]
    public void CountsLowAndElevatedDaysSeparately()
    {
        var s = MoodStatistics.Summarise([Day(0, -3), Day(1, -2), Day(2, 0), Day(3, 3), Day(4, 5)], Today);

        Assert.Equal(2, s.LowDays);
        Assert.Equal(2, s.ElevatedDays);   // an even day counts as neither
    }

    [Fact]
    public void ExcludesEntriesOutsideTheWindow()
    {
        var s = MoodStatistics.Summarise([Day(0, 3), Day(29, 3), Day(30, -5), Day(400, -5)], Today, windowDays: 30);

        Assert.Equal(2, s.DaysLogged);     // days 0 and 29 only
        Assert.Equal(3, s.Highest);
        Assert.Equal(3, s.Lowest);
    }

    [Fact]
    public void IgnoresFutureDatedEntries()
    {
        var s = MoodStatistics.Summarise([Day(0, 1), Day(-5, -5)], Today);

        Assert.Equal(1, s.DaysLogged);
    }

    [Fact]
    public void AveragesSleepOnlyOverDaysThatRecordedIt()
    {
        var s = MoodStatistics.Summarise([Day(0, 0, 8m), Day(1, 0, 6m), Day(2, 0)], Today);

        Assert.Equal(7.0, s.AverageSleepHours);
    }

    [Fact]
    public void SleepIsNullWhenNeverRecorded()
    {
        Assert.Null(MoodStatistics.Summarise([Day(0, 0), Day(1, 0)], Today).AverageSleepHours);
    }

    [Fact]
    public void MonthlyAveragesAreOrderedOldestFirst()
    {
        var months = MoodStatistics.ByMonth([
            new MoodEntry { Date = new DateOnly(2026, 1, 5), Mood = -4 },
            new MoodEntry { Date = new DateOnly(2026, 1, 20), Mood = -2 },
            new MoodEntry { Date = new DateOnly(2026, 7, 3), Mood = 1 }
        ]);

        Assert.Equal(2, months.Count);
        Assert.Equal("Jan 2026", months[0].Label);
        Assert.Equal(-3, months[0].AverageMood);
        Assert.Equal(2, months[0].DaysLogged);
        Assert.True(months[0].IsLow);          // the winter month reads low
        Assert.False(months[1].IsLow);
    }

    [Fact]
    public void MonthlyAveragesSeparateTheSameMonthAcrossYears()
    {
        var months = MoodStatistics.ByMonth([
            new MoodEntry { Date = new DateOnly(2025, 1, 5), Mood = -1 },
            new MoodEntry { Date = new DateOnly(2026, 1, 5), Mood = -4 }
        ]);

        Assert.Equal(2, months.Count);
        Assert.Equal("Jan 2025", months[0].Label);
        Assert.Equal("Jan 2026", months[1].Label);
    }

    [Fact]
    public void MoodLabelsAndDirectionReadCorrectly()
    {
        Assert.Equal("Very low", new MoodEntry { Mood = -5 }.MoodLabel);
        Assert.Equal("Low", new MoodEntry { Mood = -2 }.MoodLabel);
        Assert.Equal("Even", new MoodEntry { Mood = 0 }.MoodLabel);
        Assert.Equal("Elevated", new MoodEntry { Mood = 3 }.MoodLabel);
        Assert.Equal("Very elevated", new MoodEntry { Mood = 5 }.MoodLabel);

        // Direction must be visible at a glance; an unsigned number cannot show it.
        Assert.Equal("+3", new MoodEntry { Mood = 3 }.MoodDisplay);
        Assert.Equal("-3", new MoodEntry { Mood = -3 }.MoodDisplay);
        Assert.Equal("0", new MoodEntry { Mood = 0 }.MoodDisplay);
    }

    [Fact]
    public void MeasuredSleepWinsOverTheTypedFigure()
    {
        // The pad is the better witness. It does not overwrite the entry, only what gets displayed.
        var entry = Day(0, 0, sleep: 6m);
        var measured = new Dictionary<DateOnly, decimal> { [entry.Date] = 7.4m };

        Assert.Equal(7.4m, MoodStatistics.SleepFor(entry, measured));
        Assert.Equal(6m, entry.SleepHours);
    }

    [Fact]
    public void TypedSleepSurvivesWhereNoMeasurementExists()
    {
        var entry = Day(0, 0, sleep: 6m);

        Assert.Equal(6m, MoodStatistics.SleepFor(entry, new Dictionary<DateOnly, decimal>()));
        Assert.Equal(6m, MoodStatistics.SleepFor(entry, null));
    }

    [Fact]
    public void MeasuredNightsCanSupplySleepForADayNothingWasTypedOn()
    {
        var entry = Day(0, 2);   // mood logged, sleep left blank
        var measured = new Dictionary<DateOnly, decimal> { [entry.Date] = 8m };

        var s = MoodStatistics.Summarise([entry], Today, measuredSleep: measured);

        Assert.Equal(8.0, s.AverageSleepHours);
    }

    [Fact]
    public void EvenLowAndElevatedAreMutuallyExclusive()
    {
        foreach (var mood in Enumerable.Range(MoodEntry.MoodFloor, MoodEntry.MoodCeiling - MoodEntry.MoodFloor + 1))
        {
            var e = new MoodEntry { Mood = mood };
            Assert.Single(new[] { e.IsLow, e.IsEven, e.IsElevated }.Where(f => f));
        }
    }
}

using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Tests;

public class AgendaBuilderTests
{
    private static ScheduleBlock Work(DayOfWeekFlags days) => new()
    {
        Id = 1,
        Kind = ScheduleBlockKind.Work,
        Label = "Core hours",
        DaysOfWeek = days,
        StartTime = new TimeSpan(8, 0, 0),
        EndTime = new TimeSpan(17, 0, 0),
        EffectiveFrom = new DateOnly(2026, 1, 1),
        IsActive = true,
    };

    private static ScheduleBlock Sleep() => new()
    {
        Id = 10,
        Kind = ScheduleBlockKind.Sleep,
        Label = "Sleep",
        DaysOfWeek = DayOfWeekFlags.EveryDay,
        StartTime = new TimeSpan(23, 0, 0),
        EndTime = new TimeSpan(7, 0, 0),   // wraps midnight
        EffectiveFrom = new DateOnly(2026, 1, 1),
        IsActive = true,
    };

    // Mon 2026-07-06 .. Sun 2026-07-12
    private static readonly DateOnly Monday = new(2026, 7, 6);
    private static readonly DateOnly Sunday = new(2026, 7, 12);

    [Fact]
    public void ExpandsBlock_OnlyOnItsWeekdays()
    {
        var days = AgendaBuilder.Build(Monday, Sunday, [Work(DayOfWeekFlags.Weekdays)], [], []);

        Assert.Equal(7, days.Count);
        Assert.All(days.Take(5), d => Assert.Single(d.Entries));
        Assert.Empty(days[5].Entries); // Saturday
        Assert.Empty(days[6].Entries); // Sunday
        Assert.Equal(new TimeSpan(8, 0, 0), days[0].Entries[0].Start);
        Assert.Equal(AgendaEntrySource.Block, days[0].Entries[0].Source);
    }

    [Fact]
    public void SkipsBlocks_OutsideTheirEffectiveWindow()
    {
        var starting = Work(DayOfWeekFlags.EveryDay);
        starting.EffectiveFrom = new DateOnly(2026, 7, 8);
        starting.EffectiveTo = new DateOnly(2026, 7, 9);

        var days = AgendaBuilder.Build(Monday, Sunday, [starting], [], []);

        Assert.Empty(days[0].Entries);                 // Mon 6th, before EffectiveFrom
        Assert.Single(days[2].Entries);                // Wed 8th
        Assert.Single(days[3].Entries);                // Thu 9th
        Assert.Empty(days[4].Entries);                 // Fri 10th, after EffectiveTo
    }

    [Fact]
    public void SkipsInactiveBlocks()
    {
        var inactive = Work(DayOfWeekFlags.EveryDay);
        inactive.IsActive = false;

        var days = AgendaBuilder.Build(Monday, Monday, [inactive], [], []);

        Assert.Empty(days[0].Entries);
    }

    [Fact]
    public void OrdersEntriesByStartTime()
    {
        var evening = Work(DayOfWeekFlags.EveryDay);
        evening.Id = 2;
        evening.Label = "Evening";
        evening.StartTime = new TimeSpan(19, 0, 0);
        evening.EndTime = new TimeSpan(21, 0, 0);

        var days = AgendaBuilder.Build(Monday, Monday, [evening, Work(DayOfWeekFlags.EveryDay)], [], []);

        Assert.Equal(["Core hours", "Evening"], days[0].Entries.Select(e => e.Label));
    }

    [Fact]
    public void CancellationException_RemovesTheBlockForThatDayOnly()
    {
        ScheduleException pto = new() { Id = 1, Date = Monday, ScheduleBlockId = 1, IsCancelled = true, Note = "PTO" };

        var days = AgendaBuilder.Build(Monday, Sunday, [Work(DayOfWeekFlags.Weekdays)], [pto], []);

        Assert.Empty(days[0].Entries);   // Monday cancelled
        Assert.Single(days[1].Entries);  // Tuesday unaffected
    }

    [Fact]
    public void TimeOverrideException_ReplacesTheBlocksTimes()
    {
        ScheduleException shortDay = new()
        {
            Id = 1,
            Date = Monday,
            ScheduleBlockId = 1,
            StartTime = new TimeSpan(8, 0, 0),
            EndTime = new TimeSpan(12, 0, 0),
        };

        var days = AgendaBuilder.Build(Monday, Monday, [Work(DayOfWeekFlags.Weekdays)], [shortDay], []);

        var entry = Assert.Single(days[0].Entries);
        Assert.Equal(new TimeSpan(12, 0, 0), entry.End);
        Assert.Equal(AgendaEntrySource.Exception, entry.Source);
        Assert.Equal("Core hours", entry.Label); // label carries over from the block
    }

    [Fact]
    public void StandaloneException_AddsAnEntryWithNoParentBlock()
    {
        ScheduleException oneOff = new()
        {
            Id = 1,
            Date = Monday,
            Kind = ScheduleBlockKind.Work,
            Label = "Deploy window",
            StartTime = new TimeSpan(20, 0, 0),
            EndTime = new TimeSpan(23, 0, 0),
        };

        var days = AgendaBuilder.Build(Monday, Monday, [Work(DayOfWeekFlags.Weekdays)], [oneOff], []);

        Assert.Equal(["Core hours", "Deploy window"], days[0].Entries.Select(e => e.Label));
        Assert.Equal(AgendaEntrySource.Exception, days[0].Entries[1].Source);
    }

    [Fact]
    public void OrphanedException_IsIgnored()
    {
        // Block 99 was deleted; the exception row survives. It must not throw or invent an entry.
        ScheduleException orphan = new() { Id = 1, Date = Monday, ScheduleBlockId = 99, IsCancelled = true };

        var days = AgendaBuilder.Build(Monday, Monday, [Work(DayOfWeekFlags.Weekdays)], [orphan], []);

        Assert.Single(days[0].Entries);
    }

    [Fact]
    public void ExternalEvents_MergeInStartOrder_AndFreeEventsAreExcluded()
    {
        ExternalEventEntry standup = new(Monday, new TimeSpan(9, 30, 0), new TimeSpan(10, 0, 0), "Standup", IsBusy: true);
        ExternalEventEntry earlyCall = new(Monday, new TimeSpan(7, 0, 0), new TimeSpan(7, 30, 0), "Call", IsBusy: true);
        ExternalEventEntry fyi = new(Monday, new TimeSpan(11, 0, 0), new TimeSpan(12, 0, 0), "FYI: launch", IsBusy: false);
        ExternalEventEntry otherDay = new(Monday.AddDays(1), new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0), "Tomorrow", IsBusy: true);

        var days = AgendaBuilder.Build(Monday, Monday, [Work(DayOfWeekFlags.Weekdays)], [], [standup, earlyCall, fyi, otherDay]);

        Assert.Equal(["Call", "Core hours", "Standup"], days[0].Entries.Select(e => e.Label));
        Assert.Equal(AgendaEntrySource.External, days[0].Entries[0].Source);
        Assert.Equal(ScheduleBlockKind.Personal, days[0].Entries[0].Kind);
    }

    [Fact]
    public void WrappingBlock_IsSplitAcrossTheDayBoundary()
    {
        var days = AgendaBuilder.Build(Monday, Monday.AddDays(1), [Sleep()], [], []);

        // Monday carries the tail of Sunday night plus Monday night's opening segment.
        Assert.Equal(
            [(TimeSpan.Zero, new TimeSpan(7, 0, 0)), (new TimeSpan(23, 0, 0), new TimeSpan(24, 0, 0))],
            days[0].Entries.Select(e => (e.Start, e.End)));
        // No entry may wrap: every End is after its Start.
        Assert.All(days[0].Entries, e => Assert.True(e.End > e.Start));
    }

    [Fact]
    public void FreeGaps_AreTheSpansBetweenCommittedEntries()
    {
        var days = AgendaBuilder.Build(Monday, Monday, [Sleep(), Work(DayOfWeekFlags.Weekdays)], [], []);

        // Sleep 00:00-07:00 and 23:00-24:00, work 08:00-17:00.
        Assert.Equal(
            [(new TimeSpan(7, 0, 0), new TimeSpan(8, 0, 0)), (new TimeSpan(17, 0, 0), new TimeSpan(23, 0, 0))],
            days[0].FreeGaps.Select(g => (g.Start, g.End)));
        Assert.Equal(60, days[0].FreeGaps[0].Minutes);
        Assert.Equal(360, days[0].FreeGaps[1].Minutes);
    }

    [Fact]
    public void FreeGaps_MergeOverlappingEntriesBeforeMeasuring()
    {
        var overlapping = Work(DayOfWeekFlags.EveryDay);
        overlapping.Id = 2;
        overlapping.Label = "Overlapping";
        overlapping.StartTime = new TimeSpan(16, 0, 0);
        overlapping.EndTime = new TimeSpan(18, 0, 0);

        var days = AgendaBuilder.Build(Monday, Monday, [Work(DayOfWeekFlags.EveryDay), overlapping], [], []);

        // 08:00-17:00 and 16:00-18:00 union to 08:00-18:00, so exactly two gaps, not three.
        Assert.Equal(
            [(TimeSpan.Zero, new TimeSpan(8, 0, 0)), (new TimeSpan(18, 0, 0), new TimeSpan(24, 0, 0))],
            days[0].FreeGaps.Select(g => (g.Start, g.End)));
    }

    [Fact]
    public void EmptyDay_IsOneFullDayGap()
    {
        var days = AgendaBuilder.Build(Monday, Monday, [Work(DayOfWeekFlags.Weekend)], [], []);

        var gap = Assert.Single(days[0].FreeGaps);
        Assert.Equal(TimeSpan.Zero, gap.Start);
        Assert.Equal(new TimeSpan(24, 0, 0), gap.End);
    }

    [Fact]
    public void FullyBookedDay_HasNoGaps()
    {
        var allDay = Work(DayOfWeekFlags.EveryDay);
        allDay.StartTime = TimeSpan.Zero;
        allDay.EndTime = new TimeSpan(24, 0, 0);

        var days = AgendaBuilder.Build(Monday, Monday, [allDay], [], []);

        Assert.Empty(days[0].FreeGaps);
    }

    [Fact]
    public void FirstCommitment_SkipsSleep()
    {
        var days = AgendaBuilder.Build(Monday, Monday, [Sleep(), Work(DayOfWeekFlags.Weekdays)], [], []);

        Assert.Equal("Core hours", days[0].FirstCommitment!.Label);
    }
}

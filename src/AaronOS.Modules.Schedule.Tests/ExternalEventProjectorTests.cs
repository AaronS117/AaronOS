using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.External;

namespace AaronOS.Modules.Schedule.Tests;

public class ExternalEventProjectorTests
{
    private static ExternalEvent Event(DateTime start, DateTime end, string title = "Meeting", bool allDay = false, bool busy = true) =>
        new()
        {
            Id = 1, ExternalCalendarId = 1, ExternalUid = "uid", Title = title,
            StartsAt = start, EndsAt = end, IsAllDay = allDay, IsBusy = busy, LastSeenAt = start,
        };

    [Fact]
    public void SingleDayEvent_MapsToOneEntry()
    {
        var entries = ExternalEventProjector.ToAgendaEntries(
            [Event(new DateTime(2026, 7, 6, 9, 30, 0), new DateTime(2026, 7, 6, 10, 0, 0))]);

        var only = Assert.Single(entries);
        Assert.Equal(new DateOnly(2026, 7, 6), only.Date);
        Assert.Equal(new TimeSpan(9, 30, 0), only.Start);
        Assert.Equal(new TimeSpan(10, 0, 0), only.End);
        Assert.True(only.IsBusy);
    }

    [Fact]
    public void EventSpanningMidnight_IsSplitPerDay()
    {
        // AgendaBuilder works in per-day wall-clock spans, so a 22:00-02:00 event must arrive as
        // two entries or its second half silently vanishes.
        var entries = ExternalEventProjector.ToAgendaEntries(
            [Event(new DateTime(2026, 7, 6, 22, 0, 0), new DateTime(2026, 7, 7, 2, 0, 0), "Deploy")])
            .OrderBy(e => e.Date).ToList();

        Assert.Equal(2, entries.Count);
        Assert.Equal((new DateOnly(2026, 7, 6), new TimeSpan(22, 0, 0), new TimeSpan(24, 0, 0)),
            (entries[0].Date, entries[0].Start, entries[0].End));
        Assert.Equal((new DateOnly(2026, 7, 7), TimeSpan.Zero, new TimeSpan(2, 0, 0)),
            (entries[1].Date, entries[1].Start, entries[1].End));
    }

    [Fact]
    public void MultiDayAllDayEvent_CoversEveryDayInFull()
    {
        var entries = ExternalEventProjector.ToAgendaEntries(
            [Event(new DateTime(2026, 7, 9), new DateTime(2026, 7, 11), "Holiday", allDay: true)])
            .OrderBy(e => e.Date).ToList();

        // DTEND is exclusive for all-day events, so 9th-11th covers the 9th and 10th.
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e =>
        {
            Assert.Equal(TimeSpan.Zero, e.Start);
            Assert.Equal(new TimeSpan(24, 0, 0), e.End);
        });
    }

    [Fact]
    public void FreeEvent_KeepsItsIsBusyFlag_ForAgendaBuilderToFilter()
    {
        var entries = ExternalEventProjector.ToAgendaEntries(
            [Event(new DateTime(2026, 7, 6, 11, 0, 0), new DateTime(2026, 7, 6, 12, 0, 0), busy: false)]);

        Assert.False(Assert.Single(entries).IsBusy);
    }

    [Fact]
    public void ZeroLengthEvent_IsSkipped()
    {
        // A reminder-style event with identical start and end would produce a zero-width entry that
        // muddles gap computation for no benefit.
        var entries = ExternalEventProjector.ToAgendaEntries(
            [Event(new DateTime(2026, 7, 6, 9, 0, 0), new DateTime(2026, 7, 6, 9, 0, 0))]);

        Assert.Empty(entries);
    }

    [Fact]
    public void TimedEventEndingExactlyAtMidnight_IsNotBackDatedLikeAnAllDayEvent()
    {
        // DTEND-is-exclusive back-dating only applies to all-day events. A timed 22:00-00:00
        // meeting really does run to the day boundary: applying the all-day adjustment here would
        // set the end a tick short of 24:00 (spawning a spurious near-zero free gap) and also
        // spawn a degenerate 00:00-00:00 second-day entry if not guarded.
        var entries = ExternalEventProjector.ToAgendaEntries(
            [Event(new DateTime(2026, 7, 6, 22, 0, 0), new DateTime(2026, 7, 7, 0, 0, 0), "Late shift")]);

        var only = Assert.Single(entries);
        Assert.Equal(new DateOnly(2026, 7, 6), only.Date);
        Assert.Equal(new TimeSpan(22, 0, 0), only.Start);
        Assert.Equal(new TimeSpan(24, 0, 0), only.End);
    }

    [Fact]
    public void AbsurdlyLongEvent_IsCappedRatherThanExpandingForever()
    {
        // A malformed feed can carry a decade-long event; expanding it per-day would allocate
        // thousands of entries. Cap at 60 days.
        var entries = ExternalEventProjector.ToAgendaEntries(
            [Event(new DateTime(2026, 1, 1), new DateTime(2036, 1, 1), "Broken", allDay: true)]);

        Assert.Equal(60, entries.Count);
    }
}

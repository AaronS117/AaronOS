using AaronOS.Modules.Schedule.Data;
using AaronOS.Modules.Schedule.External;

namespace AaronOS.Modules.Schedule.Tests;

public class ExternalEventMergerTests
{
    private static readonly DateTime SeenAt = new(2026, 7, 6, 8, 0, 0);

    private static ExternalEvent Existing(string uid, string title, int startHour) => new()
    {
        Id = uid.GetHashCode(), ExternalCalendarId = 1, ExternalUid = uid, Title = title,
        StartsAt = new DateTime(2026, 7, 6, startHour, 0, 0),
        EndsAt = new DateTime(2026, 7, 6, startHour + 1, 0, 0),
        IsBusy = true, LastSeenAt = SeenAt.AddDays(-1),
    };

    private static ExternalEventDto Fetched(string uid, string title, int startHour, bool isBusy = true) =>
        new(uid, title, new DateTime(2026, 7, 6, startHour, 0, 0), new DateTime(2026, 7, 6, startHour + 1, 0, 0),
            IsAllDay: false, Location: null, isBusy);

    [Fact]
    public void NewUid_IsInserted()
    {
        var plan = ExternalEventMerger.Plan([], [Fetched("uid-1", "Standup", 9)]);

        Assert.Equal("uid-1", Assert.Single(plan.ToInsert).ExternalUid);
        Assert.Empty(plan.ToUpdate);
        Assert.Empty(plan.ToDelete);
    }

    [Fact]
    public void KnownUid_IsUpdatedInPlace_NotReinserted()
    {
        var existing = Existing("uid-1", "Standup", 9);

        var plan = ExternalEventMerger.Plan([existing], [Fetched("uid-1", "Standup (moved)", 10)]);

        Assert.Empty(plan.ToInsert);
        Assert.Empty(plan.ToDelete);
        var (target, incoming) = Assert.Single(plan.ToUpdate);
        Assert.Same(existing, target); // the tracked entity, so its Id survives
        Assert.Equal("Standup (moved)", incoming.Title);
    }

    [Fact]
    public void UidAbsentFromTheFetch_IsDeleted()
    {
        var cancelled = Existing("uid-2", "Cancelled meeting", 14);

        var plan = ExternalEventMerger.Plan(
            [Existing("uid-1", "Standup", 9), cancelled],
            [Fetched("uid-1", "Standup", 9)]);

        Assert.Same(cancelled, Assert.Single(plan.ToDelete));
    }

    [Fact]
    public void MergingTheSameBatchTwice_IsIdempotent()
    {
        var existing = Existing("uid-1", "Standup", 9);
        var fetched = Fetched("uid-1", "Standup", 9);

        // Apply the first plan the way the caller would, then re-plan against the result.
        var first = ExternalEventMerger.Plan([existing], [fetched]);
        foreach (var (target, incoming) in first.ToUpdate) ExternalEventMerger.CopyInto(incoming, target, SeenAt);

        var second = ExternalEventMerger.Plan([existing], [fetched]);

        Assert.Empty(second.ToInsert);
        Assert.Empty(second.ToDelete);
        Assert.Single(second.ToUpdate); // an unchanged event still "updates", but to identical values
        Assert.Equal("Standup", existing.Title);
        Assert.Equal(new DateTime(2026, 7, 6, 9, 0, 0), existing.StartsAt);
    }

    [Fact]
    public void DuplicateUidsInOneFetch_KeepTheLastAndDoNotThrow()
    {
        // A malformed feed can repeat a UID. Throwing would fail the whole sync over one bad row.
        var plan = ExternalEventMerger.Plan([], [Fetched("uid-1", "First", 9), Fetched("uid-1", "Second", 11)]);

        var inserted = Assert.Single(plan.ToInsert);
        Assert.Equal("Second", inserted.Title);
    }

    [Fact]
    public void EmptyFetch_DeletesEverything()
    {
        // A calendar that has genuinely been cleared must clear locally too — but note the caller
        // only ever passes a successful full-window fetch here, never a failed one.
        var plan = ExternalEventMerger.Plan([Existing("uid-1", "Standup", 9)], []);

        Assert.Single(plan.ToDelete);
        Assert.Empty(plan.ToInsert);
    }

    [Fact]
    public void CopyInto_OverwritesEveryMutableFieldAndStampsSeenAt()
    {
        var target = Existing("uid-1", "Old title", 9);
        var dto = new ExternalEventDto("uid-1", "New title",
            new DateTime(2026, 7, 6, 15, 0, 0), new DateTime(2026, 7, 6, 16, 0, 0),
            IsAllDay: true, Location: "Room 2", IsBusy: false);

        ExternalEventMerger.CopyInto(dto, target, SeenAt);

        Assert.Equal("New title", target.Title);
        Assert.Equal(new DateTime(2026, 7, 6, 15, 0, 0), target.StartsAt);
        Assert.Equal(new DateTime(2026, 7, 6, 16, 0, 0), target.EndsAt);
        Assert.True(target.IsAllDay);
        Assert.Equal("Room 2", target.Location);
        Assert.False(target.IsBusy);
        Assert.Equal(SeenAt, target.LastSeenAt);
        // Identity must survive — that is the whole reason for copying rather than replacing.
        Assert.Equal("uid-1", target.ExternalUid);
        Assert.Equal(1, target.ExternalCalendarId);
    }

    [Fact]
    public void CopyInto_ClearsLocationWhenIncomingIsNull()
    {
        var target = Existing("uid-1", "Old title", 9);
        target.Location = "Room 2";
        var dto = new ExternalEventDto("uid-1", "Old title",
            new DateTime(2026, 7, 6, 9, 0, 0), new DateTime(2026, 7, 6, 10, 0, 0),
            IsAllDay: false, Location: null, IsBusy: true);

        ExternalEventMerger.CopyInto(dto, target, SeenAt);

        Assert.Null(target.Location);
    }

    [Fact]
    public void ExistingRowsFromMoreThanOneCalendar_Throws()
    {
        // ExternalUid is unique only per calendar (composite index on ExternalCalendarId +
        // ExternalUid). Plan() merges one calendar's fetch at a time; feeding it rows from two
        // calendars that happen to share a UID must fail loudly rather than silently drop a row.
        var calendar1Row = Existing("uid-1", "Standup", 9);
        var calendar2Row = Existing("uid-1", "Standup", 9);
        calendar2Row.ExternalCalendarId = 2;

        Assert.Throws<ArgumentException>(() =>
            ExternalEventMerger.Plan([calendar1Row, calendar2Row], [Fetched("uid-1", "Standup", 9)]));
    }
}

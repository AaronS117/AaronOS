using AaronOS.Modules.Schedule.External;

namespace AaronOS.Modules.Schedule.Tests;

public class IcsFeedClientTests
{
    private static string FixtureText() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-calendar.ics"));

    private static readonly DateOnly From = new(2026, 7, 6);
    private static readonly DateOnly To = new(2026, 7, 31);

    // The fixture's timed events are authored in America/New_York. IcsFeedClient converts to this
    // machine's local wall clock, so the expected values are computed from the same zone rather
    // than hardcoded as the literal Eastern time — this machine is not necessarily Eastern.
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static DateTime Local(int year, int month, int day, int hour, int minute) =>
        TimeZoneInfo.ConvertTime(
            DateTime.SpecifyKind(new DateTime(year, month, day, hour, minute, 0), DateTimeKind.Unspecified),
            Eastern,
            TimeZoneInfo.Local);

    [Fact]
    public void Parses_ASimpleEvent()
    {
        var events = IcsFeedClient.Parse(FixtureText(), From, To);

        var standup = Assert.Single(events, e => e.Title == "Standup");
        Assert.Equal(Local(2026, 7, 6, 9, 30), standup.StartsAt);
        Assert.Equal(Local(2026, 7, 6, 10, 0), standup.EndsAt);
        Assert.Equal("Room 1", standup.Location);
        Assert.True(standup.IsBusy);
        Assert.False(standup.IsAllDay);
    }

    [Fact]
    public void ExpandsARecurringEvent_IntoDistinctOccurrences()
    {
        var events = IcsFeedClient.Parse(FixtureText(), From, To);

        var weekly = events.Where(e => e.Title == "Weekly sync").OrderBy(e => e.StartsAt).ToList();

        // COUNT=3 from Tue 7 July: the 7th, 14th, and 21st.
        Assert.Equal(3, weekly.Count);
        Assert.Equal(
            [Local(2026, 7, 7, 14, 0), Local(2026, 7, 14, 14, 0), Local(2026, 7, 21, 14, 0)],
            weekly.Select(e => e.StartsAt));
        // Each occurrence must carry its own end, not the master event's DTEND repeated — a
        // regression here would make every weekly meeting silently end on the first week's date.
        Assert.Equal(
            [Local(2026, 7, 7, 15, 0), Local(2026, 7, 14, 15, 0), Local(2026, 7, 21, 15, 0)],
            weekly.Select(e => e.EndsAt));

        // Each occurrence needs its own UID or the unique index collapses them into one row.
        Assert.Equal(3, weekly.Select(e => e.ExternalUid).Distinct().Count());
    }

    [Fact]
    public void MarksAnAllDayEvent()
    {
        var events = IcsFeedClient.Parse(FixtureText(), From, To);

        var holiday = Assert.Single(events, e => e.Title == "Company holiday");
        Assert.True(holiday.IsAllDay);
        Assert.Equal(new DateOnly(2026, 7, 9), DateOnly.FromDateTime(holiday.StartsAt));
        // Exclusive next-day midnight, not the same instant as StartsAt: AgendaBuilder discards a
        // zero-duration span, so an end equal to the start would make the holiday vanish silently.
        Assert.Equal(new DateTime(2026, 7, 10), holiday.EndsAt);
    }

    [Fact]
    public void MarksATransparentEventAsFree()
    {
        var events = IcsFeedClient.Parse(FixtureText(), From, To);

        var fyi = Assert.Single(events, e => e.Title == "FYI only");
        Assert.False(fyi.IsBusy);
    }

    [Fact]
    public void ExcludesEventsOutsideTheWindow()
    {
        var events = IcsFeedClient.Parse(FixtureText(), From, To);

        Assert.DoesNotContain(events, e => e.Title == "Next year");
    }

    [Fact]
    public void MalformedInput_ThrowsRatherThanSilentlyReturningNothing()
    {
        // A truncated or HTML error-page response must surface as a failure the sync service can
        // record, not as "the calendar is empty" — which would delete every cached event.
        Assert.ThrowsAny<Exception>(() => IcsFeedClient.Parse("<html>Sign in</html>", From, To));
    }
}

using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Calendar;
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Tests;

public class CalendarItemMapperTests
{
    private static readonly DateOnly Day = new(2026, 7, 28);

    private static AgendaEntry Entry(int fromHour, int toHour, ScheduleBlockKind kind, string label,
        AgendaEntrySource source = AgendaEntrySource.Block) =>
        new(new TimeSpan(fromHour, 0, 0), new TimeSpan(toHour, 0, 0), kind, label, source);

    private static AgendaDay DayWith(params AgendaEntry[] entries) => new(Day, entries, []);

    [Fact]
    public void TimedEntriesStayInTheGrid()
    {
        var result = CalendarItemMapper.ForDay(DayWith(Entry(9, 10, ScheduleBlockKind.Work, "Standup")));

        Assert.Empty(result.AllDay);
        var item = Assert.Single(result.Timed);
        Assert.Equal(Day, item.Date);
        Assert.Equal(new TimeSpan(9, 0, 0), item.Start);
        Assert.Equal(new TimeSpan(10, 0, 0), item.End);
        Assert.Equal("Standup", item.Label);
    }

    [Fact]
    public void AFullDaySpanIsLiftedIntoTheAllDayBand()
    {
        // 00:00-24:00 in a time grid is a block filling the whole column, which would bury every
        // real meeting behind it. It belongs in the band above the grid instead.
        var result = CalendarItemMapper.ForDay(
            DayWith(Entry(0, 24, ScheduleBlockKind.Personal, "Company holiday", AgendaEntrySource.External)));

        Assert.Empty(result.Timed);
        var item = Assert.Single(result.AllDay);
        Assert.True(item.IsAllDay);
        Assert.Equal("Company holiday", item.Label);
    }

    [Fact]
    public void AWrappedSleepTailIsNotTreatedAsAllDay()
    {
        // AgendaBuilder splits a midnight-wrapping block, so a sleep tail arrives as 00:00-07:00.
        // That is a partial day and must stay in the grid: banding it would lose its actual hours.
        var result = CalendarItemMapper.ForDay(DayWith(Entry(0, 7, ScheduleBlockKind.Sleep, "Sleep")));

        Assert.Empty(result.AllDay);
        var item = Assert.Single(result.Timed);
        Assert.Equal(new TimeSpan(7, 0, 0), item.End);
    }

    [Fact]
    public void AnEveningBlockRunningToMidnightStaysInTheGrid()
    {
        // 18:00-24:00 ends at the day boundary but is not an all-day item. Keying the band on the
        // END alone rather than the whole span would wrongly lift this out of the grid.
        var result = CalendarItemMapper.ForDay(DayWith(Entry(18, 24, ScheduleBlockKind.Work, "Late shift")));

        Assert.Empty(result.AllDay);
        Assert.Single(result.Timed);
    }

    [Fact]
    public void AnExternalEntryPresentsAsAMeetingRatherThanPersonalTime()
    {
        var result = CalendarItemMapper.ForDay(
            DayWith(Entry(11, 12, ScheduleBlockKind.Personal, "1:1", AgendaEntrySource.External)));

        Assert.Equal(CalendarItemKind.Meeting, Assert.Single(result.Timed).Kind);
    }

    [Theory]
    [InlineData(ScheduleBlockKind.Work, CalendarItemKind.Work)]
    [InlineData(ScheduleBlockKind.Sleep, CalendarItemKind.Sleep)]
    [InlineData(ScheduleBlockKind.Personal, CalendarItemKind.Personal)]
    public void ABlockKeepsItsOwnKind(ScheduleBlockKind blockKind, CalendarItemKind expected)
    {
        Assert.Equal(expected, CalendarItemMapper.KindOf(blockKind, AgendaEntrySource.Block));
    }
}

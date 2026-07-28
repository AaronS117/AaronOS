using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Calendar;

/// <summary>Items for one day, already separated into the band above the grid and the grid itself.</summary>
public sealed record DayItems(IReadOnlyList<CalendarItem> AllDay, IReadOnlyList<CalendarItem> Timed);

/// <summary>
/// Turns an AgendaDay into what the grid draws. Pure: no clock, no DbContext.
/// </summary>
public static class CalendarItemMapper
{
    public static DayItems ForDay(AgendaDay day)
    {
        var allDay = new List<CalendarItem>();
        var timed = new List<CalendarItem>();

        foreach (var entry in day.Entries)
        {
            var item = new CalendarItem(
                day.Date,
                entry.Start,
                entry.End,
                entry.Label,
                KindOf(entry.Kind, entry.Source),
                entry.Source == AgendaEntrySource.External ? "from calendar" : null);

            (item.IsAllDay ? allDay : timed).Add(item);
        }

        return new DayItems(allDay, timed);
    }

    /// <summary>
    /// An external event arrives as Personal/External because AgendaBuilder has no better kind for
    /// it; presenting that as personal time is wrong, so source wins over kind for externals.
    /// </summary>
    public static CalendarItemKind KindOf(ScheduleBlockKind kind, AgendaEntrySource source)
    {
        if (source == AgendaEntrySource.External) return CalendarItemKind.Meeting;

        return kind switch
        {
            ScheduleBlockKind.Work => CalendarItemKind.Work,
            ScheduleBlockKind.Sleep => CalendarItemKind.Sleep,
            ScheduleBlockKind.Personal => CalendarItemKind.Personal,
            _ => CalendarItemKind.Other,
        };
    }
}

namespace AaronOS.Modules.Schedule.Calendar;

/// <summary>
/// One thing on the calendar, in terms the grid can render without knowing where it came from.
///
/// Deliberately NOT AgendaEntry. A calendar is plausibly useful to other modules — Medical's
/// appointments, Nutrition's meal times — and MODULE_GUIDELINES.md forbids one module reading
/// another's entities, so the shared shape would have to move to AaronOS.Core. Rendering from this
/// record is the whole preparation for that; nothing else in the grid needs to change when it happens.
/// </summary>
/// <param name="Detail">Optional secondary line — a location, or a source name. May be null.</param>
public sealed record CalendarItem(
    DateOnly Date,
    TimeSpan Start,
    TimeSpan End,
    string Label,
    CalendarItemKind Kind,
    string? Detail)
{
    private static readonly TimeSpan DayEnd = TimeSpan.FromHours(24);

    /// <summary>
    /// True only for a span covering the entire day. Both ends matter: an evening block running
    /// 18:00-24:00 also ends at the boundary but is not an all-day item.
    /// </summary>
    public bool IsAllDay => Start == TimeSpan.Zero && End == DayEnd;

    public int Minutes => (int)(End - Start).TotalMinutes;
}

/// <summary>
/// How the calendar presents an item. Distinct from ScheduleBlockKind, which describes what a block
/// IS — this describes how it should read, and adds Meeting, which no block kind supplies.
///
/// Deliberately NOT one value per imaginable activity. ScheduleBlockKind is only
/// { Work, Sleep, Personal }, and routine categories (Gym, LitterBox, Trash...) are not placed on the
/// calendar by this plan, so a Gym or Chore value here would be a kind nothing can produce. Add one
/// when something actually maps to it.
/// </summary>
public enum CalendarItemKind { Work, Sleep, Personal, Meeting, Other }

namespace AaronOS.Modules.Schedule.Data;

public enum ScheduleBlockKind { Work, Sleep, Personal }

/// <summary>A set of weekdays stored as a single int column. Flag values match
/// (1 &lt;&lt; (int)DayOfWeek) so <see cref="DayOfWeekFlagsExtensions.From"/> is a shift.</summary>
[Flags]
public enum DayOfWeekFlags
{
    None = 0,
    Sunday = 1,
    Monday = 2,
    Tuesday = 4,
    Wednesday = 8,
    Thursday = 16,
    Friday = 32,
    Saturday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekend = Saturday | Sunday,
    EveryDay = Weekdays | Weekend,
}

public static class DayOfWeekFlagsExtensions
{
    public static DayOfWeekFlags From(DayOfWeek day) => (DayOfWeekFlags)(1 << (int)day);

    public static bool Includes(this DayOfWeekFlags flags, DayOfWeek day) => (flags & From(day)) != 0;
}

public enum RoutineCategory { Gym, Cleaning, LitterBox, Trash, Other }

public enum GoalStatus { Active, Paused, Done, Abandoned }

public enum ReleaseCategory { Media, Product }

public enum InboxItemKind { Appointment, Delivery, Release, Deadline, Other }

public enum InboxItemStatus { Pending, Accepted, Dismissed }

public enum CalendarProvider { OutlookIcs, GoogleCalendar }

/// <summary>Where an agenda entry came from, so the UI can style it and the suggestion engine
/// can tell a template block from a real meeting.</summary>
public enum AgendaEntrySource { Block, Exception, External }

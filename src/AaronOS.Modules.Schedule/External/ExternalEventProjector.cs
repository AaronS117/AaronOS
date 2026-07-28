using AaronOS.Modules.Schedule.Agenda;
using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.External;

/// <summary>
/// Maps stored <see cref="ExternalEvent"/> rows onto the plain <see cref="ExternalEventEntry"/>
/// record that <see cref="AgendaBuilder"/> accepts. This mapping is why the agenda logic carries no
/// dependency on the external-calendar tables.
///
/// An event that spans midnight is split per day, because AgendaBuilder works in per-day wall-clock
/// spans and would otherwise drop everything after the first day boundary.
/// </summary>
public static class ExternalEventProjector
{
    /// <summary>Guard against a malformed feed carrying a multi-year event.</summary>
    private const int MaxDaysPerEvent = 60;

    private static readonly TimeSpan DayEnd = new(24, 0, 0);

    public static IReadOnlyList<ExternalEventEntry> ToAgendaEntries(IReadOnlyList<ExternalEvent> events)
    {
        var entries = new List<ExternalEventEntry>();

        foreach (var e in events)
        {
            if (e.EndsAt <= e.StartsAt) continue; // zero-length or inverted: nothing to show

            var firstDay = DateOnly.FromDateTime(e.StartsAt);

            // All-day DTEND is exclusive, so an event ending at midnight ends on the previous day.
            // Only all-day events use that exclusive convention: a timed event that happens to end
            // exactly at midnight really does run to that day's boundary, and back-dating it here
            // would leave lastDay one day short, producing a fractional-tick end time instead of a
            // clean 24:00.
            var lastMoment = e.IsAllDay && e.EndsAt.TimeOfDay == TimeSpan.Zero
                ? e.EndsAt.AddTicks(-1)
                : e.EndsAt;
            var lastDay = DateOnly.FromDateTime(lastMoment);

            var dayCount = Math.Min(lastDay.DayNumber - firstDay.DayNumber + 1, MaxDaysPerEvent);

            for (var i = 0; i < dayCount; i++)
            {
                var date = firstDay.AddDays(i);
                var start = i == 0 ? e.StartsAt.TimeOfDay : TimeSpan.Zero;
                var end = date == lastDay ? lastMoment.TimeOfDay : DayEnd;

                // An all-day event, or any middle day of a multi-day event, covers the full day.
                if (e.IsAllDay)
                {
                    start = TimeSpan.Zero;
                    end = DayEnd;
                }

                if (end <= start) continue;

                entries.Add(new ExternalEventEntry(date, start, end, e.Title, e.IsBusy));
            }
        }

        return entries;
    }
}

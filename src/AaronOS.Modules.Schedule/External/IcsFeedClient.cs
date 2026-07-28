using AaronOS.Modules.Schedule.Data;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using System.Net.Http;
using IcalCalendar = Ical.Net.Calendar;

namespace AaronOS.Modules.Schedule.External;

/// <summary>
/// Reads a published-calendar ICS feed. Anonymous HTTP GET plus a parse — no OAuth, no app
/// registration. The trade is freshness: published Outlook calendars can lag by hours.
///
/// Parsing uses Ical.Net rather than hand-rolled string work. RRULE expansion, VTIMEZONE
/// resolution, line unfolding, and value escaping are considerably more code to get right than a
/// package reference, and getting them subtly wrong yields a calendar that is quietly incorrect.
/// </summary>
public sealed class IcsFeedClient(IHttpClientFactory httpClientFactory) : IExternalCalendarSource
{
    public CalendarProvider Provider => CalendarProvider.OutlookIcs;

    public async Task<IReadOnlyList<ExternalEventDto>> FetchAsync(
        ExternalCalendar calendar,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(calendar.IcsUrl))
        {
            throw new InvalidOperationException("This calendar has no ICS URL configured.");
        }

        var http = httpClientFactory.CreateClient(nameof(IcsFeedClient));
        using var response = await http.GetAsync(calendar.IcsUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return Parse(text, from, to);
    }

    /// <summary>
    /// Separated from the HTTP fetch so it is testable against a checked-in fixture
    /// (see IcsFeedClientTests) with no network involved.
    /// </summary>
    public static IReadOnlyList<ExternalEventDto> Parse(string icsText, DateOnly from, DateOnly to)
    {
        // Throws on input that isn't a calendar — which is the desired behaviour: a login redirect
        // or error page must surface as a failure, never as an empty calendar, because an empty
        // successful fetch legitimately deletes every cached event.
        var calendar = IcalCalendar.Load(icsText);

        var windowStart = new CalDateTime(from.ToDateTime(TimeOnly.MinValue));
        // TimeOnly.MaxValue, not MinValue, is load-bearing here: TakeWhileBefore below is a strict
        // "before" comparison, so capping at midnight would silently drop every all-day event
        // falling on the window's last day. Verified against 5.2.3.
        var windowEnd = new CalDateTime(to.ToDateTime(TimeOnly.MaxValue));

        // Ical.Net 5.x's GetOccurrences(start) is unbounded above (it keeps expanding RRULEs
        // indefinitely); TakeWhileBefore is what actually caps the window at windowEnd.
        // Load's nullable annotation allows null in theory, but in practice it throws on anything
        // it cannot parse rather than returning null (verified empirically against this package).
        var occurrences = calendar!.GetOccurrences(windowStart, null).TakeWhileBefore(windowEnd);

        var results = new List<ExternalEventDto>();

        foreach (var occurrence in occurrences)
        {
            if (occurrence.Source is not CalendarEvent source) continue;

            var startTime = occurrence.Period.StartTime;
            // Period.EndTime comes back null for computed occurrences in this version; the actual
            // end (start + duration, or the plain DTEND for non-recurring events) is
            // EffectiveEndTime. EffectiveEndTime already applies RFC 5545's own defaults for a
            // VEVENT with no DTEND/DURATION (one day for a DATE start, zero duration for a
            // DATE-TIME start — verified against 5.2.3), so the "?? startTime" below is defensive
            // rather than load-bearing: it has no fixture case that exercises it.
            var endTime = occurrence.Period.EffectiveEndTime ?? startTime;

            var isAllDay = !startTime.HasTime;

            DateTime start;
            DateTime end;
            if (isAllDay)
            {
                // All-day (VALUE=DATE) values are floating — no time zone attached. Converting them
                // through AsUtc/ToLocalTime would shift the date by this machine's UTC offset, which
                // is wrong: an all-day event has no "instant", only a calendar date. Use the raw
                // wall-clock value. DTEND for an all-day event is already the exclusive next day, so
                // this naturally yields start 00:00 to end 24:00 (i.e. next day's 00:00).
                start = startTime.Value;
                end = endTime.Value;
            }
            else
            {
                // Timed events carry a real TZID (resolved via the feed's VTIMEZONE), so AsUtc is a
                // true instant; converting that to this machine's local time zone gives the wall
                // clock the agenda expects.
                start = startTime.AsUtc.ToLocalTime();
                end = endTime.AsUtc.ToLocalTime();
            }

            // TRANSP:TRANSPARENT means the time is not blocked. Anything else — including a missing
            // TRANSP — is treated as busy, which is the ICS default.
            var isBusy = !string.Equals(source.Transparency, "TRANSPARENT", StringComparison.OrdinalIgnoreCase);

            // Each occurrence of a recurring event needs its own stable identity: the bare UID
            // repeats across occurrences, and the unique (calendar, uid) index would collapse a
            // weekly meeting into a single row. Tradeoff: keying on the occurrence's own start
            // means moving a single occurrence produces a delete-plus-insert rather than an
            // update. Harmless today — ExternalEvent carries no per-row user state — but it would
            // lose data the moment something like a snooze or dismissal is added to the row, at
            // which point keying on RECURRENCE-ID would be the fix.
            var uid = $"{source.Uid}#{start:yyyyMMddTHHmmss}";

            results.Add(new ExternalEventDto(
                uid,
                string.IsNullOrWhiteSpace(source.Summary) ? "(untitled)" : source.Summary,
                start,
                end,
                isAllDay,
                source.Location,
                isBusy));
        }

        return results;
    }
}

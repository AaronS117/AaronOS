using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.External;

/// <summary>
/// One way of reading an external calendar. The seam exists because the work calendar's transport
/// is uncertain: a published ICS feed today, potentially Microsoft Graph later if the tenant
/// requires it. A Graph provider implements this and is registered alongside; nothing else changes.
///
/// Implementations are read-only. Never write to an external calendar.
/// </summary>
public interface IExternalCalendarSource
{
    CalendarProvider Provider { get; }

    /// <summary>
    /// Returns every event in [<paramref name="from"/>, <paramref name="to"/>] — a full-window
    /// fetch, because <see cref="ExternalEventMerger"/> deletes local rows absent from the result.
    /// Throw on failure; the caller records the message and leaves the cache alone.
    /// </summary>
    Task<IReadOnlyList<ExternalEventDto>> FetchAsync(
        ExternalCalendar calendar,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}

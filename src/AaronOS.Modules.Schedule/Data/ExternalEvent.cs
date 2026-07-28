namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// A cached external event. Cached rather than fetched live because the suggestion engine has to
/// reason about tomorrow's commitments offline, and a published-ICS feed is slow enough that
/// re-fetching on every navigation would make the UI feel broken.
///
/// Times are local wall clock, converted at the source boundary.
/// </summary>
public class ExternalEvent
{
    public int Id { get; set; }
    public int ExternalCalendarId { get; set; }

    /// <summary>The source's own identifier. Unique per calendar — that index is what makes
    /// re-syncing idempotent.</summary>
    public string ExternalUid { get; set; } = "";

    public string Title { get; set; } = "";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool IsAllDay { get; set; }
    public string? Location { get; set; }

    /// <summary>False for free/FYI events. AgendaBuilder excludes those entirely: a free event
    /// should not consume a gap or move the recommended bedtime.</summary>
    public bool IsBusy { get; set; } = true;

    /// <summary>When the last sync last saw this event. Diagnostic only — deletion is driven by
    /// absence from a full-window fetch, not by this timestamp going stale.</summary>
    public DateTime LastSeenAt { get; set; }
}

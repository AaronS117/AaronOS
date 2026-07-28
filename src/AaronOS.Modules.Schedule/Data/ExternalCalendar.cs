namespace AaronOS.Modules.Schedule.Data;

/// <summary>
/// One configured external calendar, plus the outcome of its last sync. Both the success timestamp
/// and the error text live on the row rather than in a log, so the Settings page can show what
/// happened without the user going looking.
/// </summary>
public class ExternalCalendar
{
    public int Id { get; set; }
    public CalendarProvider Provider { get; set; }
    public string DisplayName { get; set; } = "";

    /// <summary>The published-calendar ICS URL. <see cref="CalendarProvider.OutlookIcs"/> only.</summary>
    public string? IcsUrl { get; set; }

    /// <summary>Google's calendar id, usually "primary". <see cref="CalendarProvider.GoogleCalendar"/> only.</summary>
    public string? RemoteCalendarId { get; set; }

    /// <summary>DPAPI-protected OAuth token blob (current-user scope). Google only; null for ICS,
    /// which is anonymous. Never store this value in plaintext.</summary>
    public byte[]? EncryptedToken { get; set; }

    public bool IsEnabled { get; set; } = true;
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>Null after a successful sync; the failure message otherwise.</summary>
    public string? LastError { get; set; }
}

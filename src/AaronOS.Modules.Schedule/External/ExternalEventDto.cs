namespace AaronOS.Modules.Schedule.External;

/// <summary>
/// One event as a source returned it. Times are already converted to local wall clock by the
/// source, so nothing downstream has to know about time zones.
/// </summary>
public sealed record ExternalEventDto(
    string ExternalUid,
    string Title,
    DateTime StartsAt,
    DateTime EndsAt,
    bool IsAllDay,
    string? Location,
    bool IsBusy);

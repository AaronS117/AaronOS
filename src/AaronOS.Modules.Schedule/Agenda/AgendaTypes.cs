using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Agenda;

/// <summary>One committed span on a single day. Times are wall-clock offsets from that day's
/// midnight, always within [00:00, 24:00] — a block that wraps midnight is split by
/// <see cref="AgendaBuilder"/> so consumers never have to reason about wrapping.</summary>
public sealed record AgendaEntry(
    TimeSpan Start,
    TimeSpan End,
    ScheduleBlockKind Kind,
    string Label,
    AgendaEntrySource Source);

/// <summary>An uncommitted span. Sleep counts as committed, so gaps are naturally waking hours.</summary>
public sealed record FreeGap(TimeSpan Start, TimeSpan End)
{
    private static readonly TimeSpan FullDay = TimeSpan.FromHours(24);

    public int Minutes => (int)(End - Start).TotalMinutes;

    public string StartDisplay => Format(Start);
    public string EndDisplay => Format(End);

    /// <summary>
    /// The `hh` format specifier reads the Hours component after Days are stripped, so a TimeSpan
    /// of exactly one day would render as "00" — making a full-day gap read "00:00 - 00:00"
    /// alongside its own 1440-minute count. Midnight-as-end is spelled 24:00 here for that reason.
    /// </summary>
    private static string Format(TimeSpan value) =>
        value == FullDay ? "24:00" : value.ToString(@"hh\:mm");
}

public sealed record AgendaDay(
    DateOnly Date,
    IReadOnlyList<AgendaEntry> Entries,
    IReadOnlyList<FreeGap> FreeGaps)
{
    /// <summary>The first entry that isn't sleep — what a bedtime recommendation works back from.
    /// Null when the day has no waking commitments.</summary>
    public AgendaEntry? FirstCommitment =>
        Entries.FirstOrDefault(e => e.Kind != ScheduleBlockKind.Sleep);
}

/// <summary>
/// A cached external calendar event, flattened to a single day. Deliberately a plain record rather
/// than the ExternalEvent entity so the agenda logic carries no dependency on the external-calendar
/// tables — those arrive in a later phase and map their rows into this shape.
/// </summary>
public sealed record ExternalEventEntry(
    DateOnly Date,
    TimeSpan Start,
    TimeSpan End,
    string Title,
    bool IsBusy);

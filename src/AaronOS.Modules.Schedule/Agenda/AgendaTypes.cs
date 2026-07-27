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
    AgendaEntrySource Source)
{
    /// <summary>Bind these in XAML rather than formatting Start/End directly — see <see cref="WallClock"/>.</summary>
    public string StartDisplay => Start.ToWallClock();
    public string EndDisplay => End.ToWallClock();
}

/// <summary>An uncommitted span. Sleep counts as committed, so gaps are naturally waking hours.</summary>
public sealed record FreeGap(TimeSpan Start, TimeSpan End)
{
    public int Minutes => (int)(End - Start).TotalMinutes;

    public string StartDisplay => Start.ToWallClock();
    public string EndDisplay => End.ToWallClock();
}

/// <summary>
/// Wall-clock rendering for agenda times. Exactly one day is spelled "24:00" because the `hh`
/// custom format specifier reads the Hours component after Days are stripped, so it would
/// otherwise render as "00:00" — making an end-of-day boundary look like midnight-at-the-start.
/// </summary>
internal static class WallClock
{
    private static readonly TimeSpan FullDay = TimeSpan.FromHours(24);

    internal static string ToWallClock(this TimeSpan value) =>
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

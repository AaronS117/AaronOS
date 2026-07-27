using AaronOS.Modules.Schedule.Data;

namespace AaronOS.Modules.Schedule.Agenda;

/// <summary>
/// The single place recurrence is interpreted. Pure by design — it takes materialised lists and
/// returns values, with no DbContext or clock dependency, so every rule below is testable against
/// in-memory data (see AgendaBuilderTests).
/// </summary>
public static class AgendaBuilder
{
    public static IReadOnlyList<AgendaDay> Build(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<ScheduleBlock> blocks,
        IReadOnlyList<ScheduleException> exceptions,
        IReadOnlyList<ExternalEventEntry> externalEvents)
    {
        var days = new List<AgendaDay>();
        var carry = new Dictionary<DateOnly, List<AgendaEntry>>();

        // Start a day early so a wrapping block from the night before contributes to `from`,
        // then drop that warm-up day from the result.
        for (var date = from.AddDays(-1); date <= to; date = date.AddDays(1))
        {
            var dayExceptions = exceptions.Where(e => e.Date == date).ToList();
            var entries = carry.TryGetValue(date, out var carried) ? carried : [];
            carry.Remove(date);

            foreach (var block in blocks)
            {
                if (!IsActiveOn(block, date)) continue;

                var over = dayExceptions.FirstOrDefault(e => e.ScheduleBlockId == block.Id);
                if (over is null)
                {
                    AddSpan(entries, carry, date, block.StartTime, block.EndTime, block.Kind, block.Label, AgendaEntrySource.Block);
                    continue;
                }

                if (over.IsCancelled) continue;

                AddSpan(
                    entries,
                    carry,
                    date,
                    over.StartTime ?? block.StartTime,
                    over.EndTime ?? block.EndTime,
                    over.Kind ?? block.Kind,
                    over.Label ?? block.Label,
                    AgendaEntrySource.Exception);
            }

            foreach (var standalone in dayExceptions.Where(e => e.IsStandalone && !e.IsCancelled))
            {
                // A standalone entry without times is meaningless; skip rather than guess.
                if (standalone.StartTime is not { } start || standalone.EndTime is not { } end) continue;

                AddSpan(
                    entries,
                    carry,
                    date,
                    start,
                    end,
                    standalone.Kind ?? ScheduleBlockKind.Personal,
                    standalone.Label ?? "(untitled)",
                    AgendaEntrySource.Exception);
            }

            foreach (var external in externalEvents.Where(e => e.Date == date && e.IsBusy))
            {
                AddSpan(entries, carry, date, external.Start, external.End, ScheduleBlockKind.Personal, external.Title, AgendaEntrySource.External);
            }

            entries.Sort(CompareByStart);

            if (date >= from)
            {
                days.Add(new AgendaDay(date, entries, ComputeFreeGaps(entries)));
            }
        }

        return days;
    }

    private static readonly TimeSpan DayEnd = new(24, 0, 0);

    /// <summary>
    /// Adds a span to <paramref name="today"/>, and when it wraps past midnight also records the
    /// portion that lands on the following day so callers never see End &lt; Start. The tail is
    /// stashed in <paramref name="carry"/> keyed by the date it belongs to, because that date may
    /// not have been built yet.
    /// </summary>
    private static void AddSpan(
        List<AgendaEntry> today,
        Dictionary<DateOnly, List<AgendaEntry>> carry,
        DateOnly date,
        TimeSpan start,
        TimeSpan end,
        ScheduleBlockKind kind,
        string label,
        AgendaEntrySource source)
    {
        // A zero-duration commitment is meaningless, and it is NOT a wrap — ScheduleBlock.WrapsMidnight
        // uses strict `<`, so treating equal times as wrapping would fabricate a near-full-day entry
        // plus a phantom tail on the next day.
        if (end == start) return;

        if (end > start)
        {
            today.Add(new AgendaEntry(start, end, kind, label, source));
            return;
        }

        today.Add(new AgendaEntry(start, DayEnd, kind, label, source));

        // A span ending exactly at midnight (end == 00:00) has no next-day remainder — the tail
        // would run from 00:00 to 00:00, which is the same zero-duration case the guard above
        // exists to prevent. Only carry a tail when it has real duration.
        if (end == TimeSpan.Zero) return;

        var next = date.AddDays(1);
        if (!carry.TryGetValue(next, out var list))
        {
            carry[next] = list = [];
        }
        list.Add(new AgendaEntry(TimeSpan.Zero, end, kind, label, source));
    }

    /// <summary>
    /// The complement of the union of committed entries across [00:00, 24:00]. Sleep entries count
    /// as committed, which is what makes the gaps waking hours without needing a separate
    /// "available window" concept.
    /// </summary>
    private static List<FreeGap> ComputeFreeGaps(List<AgendaEntry> sortedEntries)
    {
        var gaps = new List<FreeGap>();
        var cursor = TimeSpan.Zero;

        foreach (var entry in sortedEntries)
        {
            if (entry.Start > cursor)
            {
                gaps.Add(new FreeGap(cursor, entry.Start));
            }
            if (entry.End > cursor)
            {
                cursor = entry.End;
            }
        }

        if (cursor < DayEnd)
        {
            gaps.Add(new FreeGap(cursor, DayEnd));
        }

        return gaps;
    }

    private static bool IsActiveOn(ScheduleBlock block, DateOnly date)
    {
        if (!block.IsActive) return false;
        if (date < block.EffectiveFrom) return false;
        if (block.EffectiveTo is { } end && date > end) return false;
        return block.DaysOfWeek.Includes(date.DayOfWeek);
    }

    private static int CompareByStart(AgendaEntry a, AgendaEntry b)
    {
        var byStart = a.Start.CompareTo(b.Start);
        return byStart != 0 ? byStart : a.End.CompareTo(b.End);
    }
}

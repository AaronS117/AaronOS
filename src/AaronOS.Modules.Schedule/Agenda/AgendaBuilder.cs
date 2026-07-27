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

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var dayExceptions = exceptions.Where(e => e.Date == date).ToList();
            var entries = new List<AgendaEntry>();

            foreach (var block in blocks)
            {
                if (!IsActiveOn(block, date)) continue;

                var over = dayExceptions.FirstOrDefault(e => e.ScheduleBlockId == block.Id);
                if (over is null)
                {
                    entries.Add(new AgendaEntry(
                        block.StartTime, block.EndTime, block.Kind, block.Label, AgendaEntrySource.Block));
                    continue;
                }

                if (over.IsCancelled) continue;

                entries.Add(new AgendaEntry(
                    over.StartTime ?? block.StartTime,
                    over.EndTime ?? block.EndTime,
                    over.Kind ?? block.Kind,
                    over.Label ?? block.Label,
                    AgendaEntrySource.Exception));
            }

            foreach (var standalone in dayExceptions.Where(e => e.IsStandalone && !e.IsCancelled))
            {
                // A standalone entry without times is meaningless; skip rather than guess.
                if (standalone.StartTime is not { } start || standalone.EndTime is not { } end) continue;

                entries.Add(new AgendaEntry(
                    start,
                    end,
                    standalone.Kind ?? ScheduleBlockKind.Personal,
                    standalone.Label ?? "(untitled)",
                    AgendaEntrySource.Exception));
            }

            foreach (var external in externalEvents.Where(e => e.Date == date && e.IsBusy))
            {
                entries.Add(new AgendaEntry(
                    external.Start, external.End, ScheduleBlockKind.Personal, external.Title, AgendaEntrySource.External));
            }

            entries.Sort(CompareByStart);
            days.Add(new AgendaDay(date, entries, []));
        }

        return days;
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

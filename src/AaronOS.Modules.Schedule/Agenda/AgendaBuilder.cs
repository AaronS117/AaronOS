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
            var entries = new List<AgendaEntry>();

            foreach (var block in blocks)
            {
                if (!IsActiveOn(block, date)) continue;
                entries.Add(new AgendaEntry(
                    block.StartTime, block.EndTime, block.Kind, block.Label, AgendaEntrySource.Block));
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

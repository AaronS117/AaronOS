using AaronOS.Core.Data;
using AaronOS.Modules.Medical.Data;
using Microsoft.EntityFrameworkCore;

namespace AaronOS.Modules.Medical.Withings;

public record SleepSyncResult(int Added, int Updated, DateOnly? Earliest, DateOnly? Latest)
{
    public int Total => Added + Updated;

    public string Summary => Total == 0
        ? "No nights returned for that range."
        : $"{Added} new, {Updated} updated ({Earliest:d MMM} – {Latest:d MMM}).";
}

/// <summary>
/// Fetches sleep summaries and upserts them by wake date. Safe to re-run over a range that was
/// already imported: nights are matched on their date, so a repeat sync corrects rows instead of
/// duplicating them.
/// </summary>
public class WithingsSleepImporter(
    WithingsApiClient api,
    IDbContextFactory<AaronOsDbContext> dbContextFactory)
{
    /// <summary>
    /// The pad only stores a rolling window server-side, and pulling several years of nothing on the
    /// first sync is pointless. Bounded to keep a first run quick.
    /// </summary>
    public const int MaxBackfillDays = 120;

    public async Task<SleepSyncResult> SyncAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var series = await api.GetSleepSummariesAsync(from, to, ct);
        var nights = WithingsSleepMapper.ToNights(series);

        if (nights.Count == 0)
        {
            return new SleepSyncResult(0, 0, null, null);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var dates = nights.Select(n => n.Date).ToList();
        var existing = await db.Set<SleepNight>()
            .Where(s => dates.Contains(s.Date))
            .ToDictionaryAsync(s => s.Date, ct);

        var added = 0;
        var updated = 0;

        foreach (var night in nights)
        {
            if (existing.TryGetValue(night.Date, out var row))
            {
                CopyInto(night, row);
                updated++;
            }
            else
            {
                db.Add(night);
                added++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new SleepSyncResult(added, updated, nights.Min(n => n.Date), nights.Max(n => n.Date));
    }

    /// <summary>
    /// Determines the range for a routine sync: from the morning after the newest night already
    /// stored, up to today. Falls back to the full backfill window on an empty table. Derived from the
    /// data rather than from a stored "last synced" marker, which cannot drift out of step with what
    /// was actually saved.
    /// </summary>
    public async Task<(DateOnly From, DateOnly To)> NextRangeAsync(DateOnly today, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var latest = await db.Set<SleepNight>()
            .OrderByDescending(s => s.Date)
            .Select(s => (DateOnly?)s.Date)
            .FirstOrDefaultAsync(ct);

        // Re-fetches the newest stored night rather than starting the day after it: a night synced
        // before the pad finished processing can still gain fields, and the upsert makes it free.
        var from = latest ?? today.AddDays(-MaxBackfillDays);
        return (from > today ? today : from, today);
    }

    private static void CopyInto(SleepNight source, SleepNight target)
    {
        target.BedTime = source.BedTime;
        target.WakeTime = source.WakeTime;
        target.TimeZone = source.TimeZone;
        target.AsleepSeconds = source.AsleepSeconds;
        target.LightSeconds = source.LightSeconds;
        target.DeepSeconds = source.DeepSeconds;
        target.RemSeconds = source.RemSeconds;
        target.AwakeSeconds = source.AwakeSeconds;
        target.TimeToSleepSeconds = source.TimeToSleepSeconds;
        target.WakeUpCount = source.WakeUpCount;
        target.SleepScore = source.SleepScore;
        target.HeartRateAverage = source.HeartRateAverage;
        target.HeartRateMin = source.HeartRateMin;
        target.HeartRateMax = source.HeartRateMax;
        target.BreathingRateAverage = source.BreathingRateAverage;
        target.BreathingDisturbances = source.BreathingDisturbances;
        target.SnoringSeconds = source.SnoringSeconds;
        target.SnoringEpisodes = source.SnoringEpisodes;
        target.PeriodCount = source.PeriodCount;
        target.Source = source.Source;
    }
}

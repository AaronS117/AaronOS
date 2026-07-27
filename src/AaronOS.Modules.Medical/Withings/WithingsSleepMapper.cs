using AaronOS.Modules.Medical.Data;

namespace AaronOS.Modules.Medical.Withings;

/// <summary>
/// Turns Withings sleep periods into one <see cref="SleepNight"/> per night. Pure — no HTTP, no
/// database, no clock — so every rule below is directly testable.
/// </summary>
public static class WithingsSleepMapper
{
    private record Period(WithingsSleepSeries Series, DateTime Bed, DateTime Wake, string? Zone)
    {
        public WithingsSleepData Data => Series.Data!;
    }

    /// <summary>
    /// Groups periods by the local date they *ended*, so a night is filed under the morning you woke
    /// into rather than the evening you went to bed. Withings keys its own <c>date</c> field to the
    /// start, which for any normal night lands on the previous day and would misalign every night
    /// against the mood entry it belongs with.
    /// </summary>
    public static List<SleepNight> ToNights(IEnumerable<WithingsSleepSeries> series) =>
        series
            .Where(s => s.Data is not null && s.EndDate > s.StartDate)
            .Select(ToPeriod)
            .GroupBy(p => DateOnly.FromDateTime(p.Wake))
            .Select(g => Combine(g.Key, g.ToList()))
            .OrderBy(n => n.Date)
            .ToList();

    private static Period ToPeriod(WithingsSleepSeries s)
    {
        var zone = ResolveZone(s.Timezone);
        var bed = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(s.StartDate), zone).DateTime;
        var wake = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(s.EndDate), zone).DateTime;
        return new Period(s, bed, wake, s.Timezone);
    }

    /// <summary>
    /// Withings reports IANA zone ids, which .NET resolves natively on Windows from .NET 6 onward.
    /// An unknown or missing id falls back to this machine's zone: being an hour out is recoverable,
    /// throwing away the night is not.
    /// </summary>
    private static TimeZoneInfo ResolveZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>
    /// Durations are summed across every period that ended on this date. Point-in-time readings
    /// (score, heart rate, breathing) are taken from the longest period instead, because averaging a
    /// full night against a twenty-minute nap would drag the night's numbers around for no reason.
    /// </summary>
    private static SleepNight Combine(DateOnly date, List<Period> periods)
    {
        var main = periods.OrderByDescending(p => p.Data.AsleepSeconds).ThenBy(p => p.Bed).First();

        return new SleepNight
        {
            Date = date,
            BedTime = periods.Min(p => p.Bed),
            WakeTime = periods.Max(p => p.Wake),
            TimeZone = main.Zone,

            AsleepSeconds = periods.Sum(p => p.Data.AsleepSeconds),
            LightSeconds = SumOrNull(periods.Select(p => p.Data.LightSleepDuration)),
            DeepSeconds = SumOrNull(periods.Select(p => p.Data.DeepSleepDuration)),
            RemSeconds = SumOrNull(periods.Select(p => p.Data.RemSleepDuration)),
            AwakeSeconds = SumOrNull(periods.Select(p => p.Data.WakeUpDuration)),
            TimeToSleepSeconds = main.Data.DurationToSleep,
            WakeUpCount = SumOrNull(periods.Select(p => p.Data.WakeUpCount)),
            SnoringSeconds = SumOrNull(periods.Select(p => p.Data.Snoring)),
            SnoringEpisodes = SumOrNull(periods.Select(p => p.Data.SnoringEpisodeCount)),

            SleepScore = main.Data.SleepScore,
            HeartRateAverage = main.Data.HeartRateAverage,
            HeartRateMin = Reduce(periods.Select(p => p.Data.HeartRateMin), Math.Min),
            HeartRateMax = Reduce(periods.Select(p => p.Data.HeartRateMax), Math.Max),
            BreathingRateAverage = main.Data.BreathingRateAverage,
            BreathingDisturbances = main.Data.BreathingDisturbances,

            PeriodCount = periods.Count,
            Source = RecordSource.Imported
        };
    }

    /// <summary>
    /// Sums only the values that are present, and returns null when none are. Treating an absent
    /// field as zero would turn "this unit does not report REM" into "you got no REM sleep".
    /// </summary>
    private static int? SumOrNull(IEnumerable<int?> values) => Reduce(values, (a, b) => a + b);

    /// <summary>Combines the values that are present, or null when none are.</summary>
    private static int? Reduce(IEnumerable<int?> values, Func<int, int, int> combine)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : present.Aggregate(combine);
    }
}

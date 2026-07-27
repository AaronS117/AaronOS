namespace AaronOS.Modules.Medical.Data;

/// <summary>
/// One night of measured sleep from the Withings pad. Kept separate from <see cref="MoodEntry"/> on
/// purpose: a mood entry is a self-report, and letting an import overwrite the hours someone typed
/// themselves would destroy the only record of what they believed at the time. Measured data wins
/// on display, but it does not overwrite.
///
/// Durations are stored in seconds, exactly as the API reports them, so nothing is lost to rounding
/// at rest. Hours are computed for display.
/// </summary>
public class SleepNight
{
    public int Id { get; set; }

    /// <summary>
    /// The local date you *woke up*, not the date you went to bed. Withings labels a sleep period by
    /// its start date, which puts a normal 11pm-to-7am night on the previous day. That would misalign
    /// every night against the mood entry it belongs to, since "how I slept" is about the morning you
    /// wake into. The mapper re-keys to the wake date; see WithingsSleepMapper.
    /// </summary>
    public DateOnly Date { get; set; }

    public DateTime BedTime { get; set; }
    public DateTime WakeTime { get; set; }
    public string? TimeZone { get; set; }

    public int AsleepSeconds { get; set; }
    public int? LightSeconds { get; set; }
    public int? DeepSeconds { get; set; }
    public int? RemSeconds { get; set; }
    public int? AwakeSeconds { get; set; }
    public int? TimeToSleepSeconds { get; set; }

    public int? WakeUpCount { get; set; }
    public int? SleepScore { get; set; }
    public int? HeartRateAverage { get; set; }
    public int? HeartRateMin { get; set; }
    public int? HeartRateMax { get; set; }
    public int? BreathingRateAverage { get; set; }

    /// <summary>
    /// Withings' raw breathing disturbances index. Deliberately stored and shown as the raw number:
    /// Health Mate buckets it into few/moderate/high, but the thresholds are not published, and
    /// inventing them here would be inventing a clinical reading.
    /// </summary>
    public int? BreathingDisturbances { get; set; }

    public int? SnoringSeconds { get; set; }
    public int? SnoringEpisodes { get; set; }

    /// <summary>
    /// How many separate sleep periods were summed into this night. Anything above 1 means the night
    /// was fragmented or a nap got folded in, which is worth seeing rather than hiding inside a total.
    /// </summary>
    public int PeriodCount { get; set; } = 1;

    public RecordSource Source { get; set; } = RecordSource.Imported;

    public decimal Hours => Math.Round(AsleepSeconds / 3600m, 1);
    public decimal? TimeInBedHours =>
        WakeTime > BedTime ? Math.Round((decimal)(WakeTime - BedTime).TotalHours, 1) : null;

    /// <summary>Time asleep as a share of time in bed. Null when the window is unusable.</summary>
    public int? EfficiencyPercent
    {
        get
        {
            var inBed = (WakeTime - BedTime).TotalSeconds;
            return inBed <= 0 || AsleepSeconds <= 0
                ? null
                : (int)Math.Round(Math.Min(100, AsleepSeconds / inBed * 100));
        }
    }

    public string DateDisplay => Date.ToString("ddd d MMM");
    public string HoursDisplay => $"{Hours:0.#} h";
    public string WindowDisplay => $"{BedTime:HH:mm} – {WakeTime:HH:mm}";
    public string ScoreDisplay => SleepScore is { } s ? s.ToString() : "—";
    public string EfficiencyDisplay => EfficiencyPercent is { } p ? $"{p}%" : "—";
    public string HeartRateDisplay => HeartRateAverage is { } hr ? $"{hr} bpm" : "—";
    public string BreathingDisplay => BreathingDisturbances is { } b ? b.ToString() : "—";
    public string WakeUpCountDisplay => WakeUpCount is { } c ? c.ToString() : "—";

    public string StagesDisplay
    {
        get
        {
            var parts = new List<string>(3);
            if (DeepSeconds is { } d) parts.Add($"deep {d / 3600m:0.#}");
            if (RemSeconds is { } r) parts.Add($"REM {r / 3600m:0.#}");
            if (LightSeconds is { } l) parts.Add($"light {l / 3600m:0.#}");
            return parts.Count == 0 ? "—" : string.Join(" · ", parts);
        }
    }

    public bool IsFragmented => PeriodCount > 1;
    public string FragmentNote => PeriodCount > 1 ? $"{PeriodCount} periods" : "";
}

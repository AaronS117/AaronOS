using AaronOS.Modules.Medical.Data;

namespace AaronOS.Modules.Medical.Calculations;

/// <summary>Mood summary over a window. Swing is the point: an average alone hides instability.</summary>
public record MoodSummary(
    int DaysLogged,
    double AverageMood,
    int Lowest,
    int Highest,
    int Swing,
    int LowDays,
    int ElevatedDays,
    double? AverageSleepHours)
{
    /// <summary>
    /// A mood log's average is close to useless on its own: someone alternating −4 and +4 averages the
    /// same as someone steady at 0. Swing is what distinguishes them, so it is stated plainly.
    /// </summary>
    public string SwingDescription => DaysLogged == 0
        ? "No entries yet"
        : Swing >= 6
            ? $"Wide swing — {Lowest:+#;-#;0} to {Highest:+#;-#;0} across {DaysLogged} days"
            : $"Range {Lowest:+#;-#;0} to {Highest:+#;-#;0} across {DaysLogged} days";
}

/// <summary>One month's average, for answering "is it actually worse in winter?".</summary>
public record MonthlyMood(int Year, int Month, double AverageMood, int DaysLogged)
{
    public string Label => new DateOnly(Year, Month, 1).ToString("MMM yyyy");
    public bool IsLow => AverageMood <= -1.5;
}

/// <summary>
/// Pure summaries over mood entries — no database, no clock, so every case is directly testable.
/// The caller passes "today" in rather than this reading it, which keeps the window boundaries
/// deterministic in tests.
/// </summary>
public static class MoodStatistics
{
    /// <summary>
    /// Measured sleep from the pad takes precedence over a typed figure for the same date, falling
    /// back to what was typed when no measurement exists. Resolved in one place so the summary, the
    /// chart and the ledger cannot disagree about which number is authoritative.
    /// </summary>
    public static decimal? SleepFor(MoodEntry entry, IReadOnlyDictionary<DateOnly, decimal>? measured) =>
        measured is not null && measured.TryGetValue(entry.Date, out var hours) ? hours : entry.SleepHours;

    public static MoodSummary Summarise(
        IEnumerable<MoodEntry> entries,
        DateOnly today,
        int windowDays = 30,
        IReadOnlyDictionary<DateOnly, decimal>? measuredSleep = null)
    {
        var cutoff = today.AddDays(-(windowDays - 1));
        var window = entries.Where(e => e.Date >= cutoff && e.Date <= today).ToList();

        if (window.Count == 0)
        {
            return new MoodSummary(0, 0, 0, 0, 0, 0, 0, null);
        }

        var moods = window.Select(e => e.Mood).ToList();
        var sleeps = window
            .Select(e => SleepFor(e, measuredSleep))
            .Where(h => h is not null)
            .Select(h => (double)h!.Value)
            .ToList();

        return new MoodSummary(
            DaysLogged: window.Count,
            AverageMood: Math.Round(moods.Average(), 1),
            Lowest: moods.Min(),
            Highest: moods.Max(),
            Swing: moods.Max() - moods.Min(),
            LowDays: window.Count(e => e.IsLow),
            ElevatedDays: window.Count(e => e.IsElevated),
            AverageSleepHours: sleeps.Count == 0 ? null : Math.Round(sleeps.Average(), 1));
    }

    /// <summary>
    /// Month-by-month averages, oldest first. This is what turns "worse in winter" from a recollection
    /// into something with numbers attached.
    /// </summary>
    public static List<MonthlyMood> ByMonth(IEnumerable<MoodEntry> entries) =>
        entries
            .GroupBy(e => (e.Date.Year, e.Date.Month))
            .Select(g => new MonthlyMood(
                g.Key.Year,
                g.Key.Month,
                Math.Round(g.Average(e => e.Mood), 1),
                g.Count()))
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .ToList();
}

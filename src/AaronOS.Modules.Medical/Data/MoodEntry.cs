namespace AaronOS.Modules.Medical.Data;

/// <summary>
/// One day's self-reported mood, sleep and energy.
///
/// Mood is deliberately *bidirectional* rather than a 0–10 "how good was today" score. A single
/// goodness scale cannot distinguish a good day from an elevated or wired one, and that distinction
/// is the whole point when the pattern being tracked swings both ways. Sleep is recorded alongside it
/// because it is the strongest single signal in mood tracking and the first thing a clinician asks
/// about — and because a mood log without sleep cannot show the two moving together.
///
/// This is a self-report diary, not a diagnostic instrument. It exists to replace recall with data in
/// a conversation with a clinician.
/// </summary>
public class MoodEntry
{
    /// <summary>Lowest and highest mood values. 0 is an even day, not an absent one.</summary>
    public const int MoodFloor = -5;
    public const int MoodCeiling = 5;

    public int Id { get; set; }

    /// <summary>One entry per day; saving the same date again updates it rather than adding another.</summary>
    public DateOnly Date { get; set; }

    /// <summary>−5 severely low … 0 even … +5 severely elevated.</summary>
    public int Mood { get; set; }

    /// <summary>1 (drained) … 5 (very energetic). Kept separate from mood: they come apart.</summary>
    public int Energy { get; set; } = 3;

    public decimal? SleepHours { get; set; }

    public string? Note { get; set; }

    // Computed display members, as elsewhere in this module.

    public bool IsLow => Mood <= -2;
    public bool IsElevated => Mood >= 2;
    public bool IsEven => Mood is > -2 and < 2;

    public string MoodLabel => Mood switch
    {
        <= -4 => "Very low",
        -3 or -2 => "Low",
        -1 or 0 or 1 => "Even",
        2 or 3 => "Elevated",
        _ => "Very elevated"
    };

    /// <summary>Signed so a glance distinguishes direction, which an unsigned number cannot.</summary>
    public string MoodDisplay => Mood > 0 ? $"+{Mood}" : Mood.ToString();

    public string EnergyDisplay => $"{Energy}/5";

    public string SleepDisplay => SleepHours is { } h ? $"{h:0.#} h" : "—";

    public string DateDisplay => Date.ToString("ddd d MMM");

    public string NoteDisplay => string.IsNullOrWhiteSpace(Note) ? "" : Note;
}

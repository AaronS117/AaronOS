namespace AaronOS.Core.Data;

/// <summary>
/// Plausibility limits for body figures, shared by everything that reads them.
///
/// This exists because of a real failure: the settings page offered a plain number box labelled
/// "Height (inches)", a height of 6 was entered meaning six feet, and it was stored and used as six
/// inches. Nothing rejected it. Every measurement was then compared against a six-inch body, so every
/// ratio pinned at its limit and the 3D figure rendered as an inflated blob — while BMI, computed from
/// the same value, read in the tens of thousands.
///
/// The input now asks for feet and inches separately, which is the actual fix. These limits are the
/// backstop: one definition of "possible", so a value that slips in from anywhere is caught rather
/// than quietly producing nonsense.
/// </summary>
public static class BodyMetrics
{
    /// <summary>Roughly the shortest and tallest adults on record, with room to spare.</summary>
    public const decimal MinHeightInches = 24m;
    public const decimal MaxHeightInches = 100m;

    /// <summary>A single circumference — neck through calf. The floor is above zero because a
    /// measurement of nothing is missing data, not a small body part.</summary>
    public const decimal MinCircumferenceInches = 4m;
    public const decimal MaxCircumferenceInches = 100m;

    public static bool IsPlausibleHeight(decimal inches) =>
        inches is >= MinHeightInches and <= MaxHeightInches;

    public static bool IsPlausibleCircumference(decimal inches) =>
        inches is >= MinCircumferenceInches and <= MaxCircumferenceInches;

    /// <summary>Splits a height in inches into whole feet and the remaining inches, for display.</summary>
    public static (int Feet, decimal Inches) ToFeetAndInches(decimal totalInches)
    {
        var feet = (int)(totalInches / 12);
        return (feet, totalInches - (feet * 12));
    }
}

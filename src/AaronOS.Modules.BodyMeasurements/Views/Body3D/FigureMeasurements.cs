using AaronOS.Core.Data;
using AaronOS.Modules.BodyMeasurements.Data;

namespace AaronOS.Modules.BodyMeasurements.Views.Body3D;

/// <summary>The circumferences the figure is built from, in inches, already defaulted.</summary>
public readonly record struct FigureMeasurements(
    double HeightInches,
    double Neck, double Chest, double Waist, double Hips,
    double BicepLeft, double BicepRight,
    double ThighLeft, double ThighRight,
    double CalfLeft, double CalfRight)
{
    /// <summary>
    /// Average adult proportions, used for whichever values have not been recorded. Applied per
    /// measurement rather than all-or-nothing, so a check-in with only a waist still shows that waist
    /// truthfully against an otherwise average figure.
    ///
    /// A value outside human range is treated the same as a missing one. That is not paranoia: a height
    /// of 6 was once stored meaning six feet, and because every measurement is scaled relative to
    /// height, the figure inflated to its limits everywhere at once. Falling back to the average keeps
    /// the figure readable, and the settings page is where the value gets refused outright.
    /// </summary>
    public static FigureMeasurements FromCheckIn(BodyCheckIn? c, decimal? heightInches)
    {
        double Height(decimal? value, double fallback) =>
            value is { } v && BodyMetrics.IsPlausibleHeight(v) ? (double)v : fallback;

        double Girth(decimal? value, double fallback) =>
            value is { } v && BodyMetrics.IsPlausibleCircumference(v) ? (double)v : fallback;

        return new FigureMeasurements(
            HeightInches: Height(heightInches, 70),
            Neck: Girth(c?.NeckIn, 15),
            Chest: Girth(c?.ChestIn, 40),
            Waist: Girth(c?.WaistIn, 34),
            Hips: Girth(c?.HipsIn, 40),
            BicepLeft: Girth(c?.BicepLeftIn, 13),
            BicepRight: Girth(c?.BicepRightIn, 13),
            ThighLeft: Girth(c?.ThighLeftIn, 22),
            ThighRight: Girth(c?.ThighRightIn, 22),
            CalfLeft: Girth(c?.CalfLeftIn, 15),
            CalfRight: Girth(c?.CalfRightIn, 15));
    }

    public static bool HasAnyMeasurement(BodyCheckIn? c) =>
        c is not null &&
        (c.NeckIn ?? c.ChestIn ?? c.WaistIn ?? c.HipsIn
         ?? c.BicepLeftIn ?? c.BicepRightIn
         ?? c.ThighLeftIn ?? c.ThighRightIn
         ?? c.CalfLeftIn ?? c.CalfRightIn) is not null;
}

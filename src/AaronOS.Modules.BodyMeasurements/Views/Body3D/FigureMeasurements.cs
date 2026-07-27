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
    /// <summary>Average adult proportions, used for whichever values have not been recorded. Applied
    /// per measurement rather than all-or-nothing, so a check-in with only a waist still shows that
    /// waist truthfully against an otherwise average figure.</summary>
    public static FigureMeasurements FromCheckIn(BodyCheckIn? c, decimal? heightInches)
    {
        double V(decimal? value, double fallback) => (double)(value ?? (decimal)fallback);

        return new FigureMeasurements(
            HeightInches: V(heightInches, 70),
            Neck: V(c?.NeckIn, 15),
            Chest: V(c?.ChestIn, 40),
            Waist: V(c?.WaistIn, 34),
            Hips: V(c?.HipsIn, 40),
            BicepLeft: V(c?.BicepLeftIn, 13),
            BicepRight: V(c?.BicepRightIn, 13),
            ThighLeft: V(c?.ThighLeftIn, 22),
            ThighRight: V(c?.ThighRightIn, 22),
            CalfLeft: V(c?.CalfLeftIn, 15),
            CalfRight: V(c?.CalfRightIn, 15));
    }

    public static bool HasAnyMeasurement(BodyCheckIn? c) =>
        c is not null &&
        (c.NeckIn ?? c.ChestIn ?? c.WaistIn ?? c.HipsIn
         ?? c.BicepLeftIn ?? c.BicepRightIn
         ?? c.ThighLeftIn ?? c.ThighRightIn
         ?? c.CalfLeftIn ?? c.CalfRightIn) is not null;
}

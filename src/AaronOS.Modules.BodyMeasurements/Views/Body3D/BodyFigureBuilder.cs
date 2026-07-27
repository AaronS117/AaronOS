using AaronOS.Modules.BodyMeasurements.Data;
using System.Windows.Media.Media3D;

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

/// <summary>
/// Assembles the humanoid from lofted tubes. Vertical landmarks are fixed fractions of overall
/// height (standard anatomical proportions) while every width comes from a real measurement — so the
/// figure changes shape as the numbers change, but never turns into a funhouse mirror because one
/// value is missing.
/// </summary>
public static class BodyFigureBuilder
{
    // Depth-to-width ratios. A torso is markedly flatter front-to-back than a limb, which is close
    // to round; using one ratio everywhere is what makes procedural bodies look like plumbing.
    private const double TorsoDepthRatio = 0.72;
    private const double LimbDepthRatio = 0.92;
    private const double NeckDepthRatio = 0.88;

    // Vertical landmarks as fractions of total height.
    private const double AnkleY = 0.039, CalfY = 0.16, KneeY = 0.285, ThighY = 0.42;
    private const double HipY = 0.53, WaistY = 0.615, ChestY = 0.72, ShoulderY = 0.815;
    private const double NeckTopY = 0.86, ChinY = 0.868;

    public static Model3DGroup Build(FigureMeasurements m, Material material)
    {
        var h = m.HeightInches;
        var group = new Model3DGroup();

        var (hipHalfW, hipHalfD) = HumanMeshBuilder.FromCircumference(m.Hips, TorsoDepthRatio);
        var (waistHalfW, waistHalfD) = HumanMeshBuilder.FromCircumference(m.Waist, TorsoDepthRatio);
        var (chestHalfW, chestHalfD) = HumanMeshBuilder.FromCircumference(m.Chest, TorsoDepthRatio);
        var (neckHalfW, neckHalfD) = HumanMeshBuilder.FromCircumference(m.Neck, NeckDepthRatio);

        // Shoulders read wider and flatter than the chest measurement alone would give.
        var shoulderHalfW = chestHalfW * 1.16;
        var shoulderHalfD = chestHalfD * 0.86;

        group.Children.Add(Model(HumanMeshBuilder.BuildTube(
        [
            new Ring(h * 0.475, hipHalfW * 0.86, hipHalfD * 0.86),
            new Ring(h * HipY, hipHalfW, hipHalfD),
            new Ring(h * WaistY, waistHalfW, waistHalfD),
            new Ring(h * ChestY, chestHalfW, chestHalfD),
            new Ring(h * ShoulderY, shoulderHalfW, shoulderHalfD),
            new Ring(h * (ShoulderY + 0.012), shoulderHalfW * 0.82, shoulderHalfD * 0.9),
        ]), material));

        group.Children.Add(Model(HumanMeshBuilder.BuildTube(
        [
            new Ring(h * (ShoulderY + 0.005), neckHalfW * 1.25, neckHalfD * 1.25),
            new Ring(h * NeckTopY, neckHalfW, neckHalfD),
            new Ring(h * ChinY, neckHalfW * 0.95, neckHalfD * 0.95),
        ]), material));

        // Head sized from height rather than a measurement — head circumference is not tracked.
        var headCentreY = h * 0.935;
        group.Children.Add(Model(
            HumanMeshBuilder.BuildEllipsoid(h * 0.0405, h * 0.052, h * 0.047),
            material,
            new TranslateTransform3D(0, headCentreY, 0)));

        AddArm(group, material, m.BicepRight, h, shoulderHalfW, isRight: true);
        AddArm(group, material, m.BicepLeft, h, shoulderHalfW, isRight: false);

        AddLeg(group, material, m.ThighRight, m.CalfRight, h, hipHalfW, isRight: true);
        AddLeg(group, material, m.ThighLeft, m.CalfLeft, h, hipHalfW, isRight: false);

        return group;
    }

    /// <summary>
    /// Arms are built vertically in local space with the shoulder at the origin, then rotated and
    /// translated into place. Far simpler than computing rotated ring positions by hand, and it lets
    /// the hand ride along with the arm automatically.
    /// </summary>
    private static void AddArm(Model3DGroup group, Material material, double bicep, double h, double shoulderHalfW, bool isRight)
    {
        var (r, d) = HumanMeshBuilder.FromCircumference(bicep, LimbDepthRatio);

        var arm = new Model3DGroup();
        arm.Children.Add(Model(HumanMeshBuilder.BuildTube(
        [
            new Ring(0, r * 1.18, d * 1.18),          // deltoid
            new Ring(-h * 0.055, r * 1.04, d * 1.04),
            new Ring(-h * 0.105, r, d),               // bicep belly
            new Ring(-h * 0.185, r * 0.80, d * 0.80), // elbow
            new Ring(-h * 0.245, r * 0.86, d * 0.86), // forearm belly
            new Ring(-h * 0.365, r * 0.55, d * 0.55), // wrist
        ]), material));

        arm.Children.Add(Model(
            HumanMeshBuilder.BuildEllipsoid(r * 0.60, h * 0.030, d * 0.42),
            material,
            new TranslateTransform3D(0, -h * 0.395, 0)));

        var sign = isRight ? 1 : -1;
        var transforms = new Transform3DGroup();
        // Positive rotation about Z swings the downward-pointing arm toward +X, so the right arm
        // takes a positive angle: a relaxed A-pose rather than arms clipping into the torso.
        transforms.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), sign * 11)));
        transforms.Children.Add(new TranslateTransform3D(sign * (shoulderHalfW + r * 0.10), h * (ShoulderY - 0.005), 0));

        arm.Transform = transforms;
        group.Children.Add(arm);
    }

    private static void AddLeg(Model3DGroup group, Material material, double thigh, double calf, double h, double hipHalfW, bool isRight)
    {
        var (thighR, thighD) = HumanMeshBuilder.FromCircumference(thigh, LimbDepthRatio);
        var (calfR, calfD) = HumanMeshBuilder.FromCircumference(calf, LimbDepthRatio);

        var hipTop = h * HipY;
        var leg = new Model3DGroup();
        leg.Children.Add(Model(HumanMeshBuilder.BuildTube(
        [
            new Ring(0, thighR * 1.06, thighD * 1.06),
            new Ring(-(hipTop - h * ThighY), thighR, thighD),
            new Ring(-(hipTop - h * KneeY), thighR * 0.66, thighD * 0.66),
            new Ring(-(hipTop - h * CalfY), calfR, calfD),
            new Ring(-(hipTop - h * AnkleY), calfR * 0.56, calfD * 0.56),
        ]), material));

        // Foot: flattened and offset forward, so the figure reads as standing rather than floating.
        leg.Children.Add(Model(
            HumanMeshBuilder.BuildEllipsoid(calfR * 0.72, h * 0.017, h * 0.062),
            material,
            new TranslateTransform3D(0, -(hipTop - h * 0.017), h * 0.030)));

        var sign = isRight ? 1 : -1;
        var transforms = new Transform3DGroup();
        transforms.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), sign * 2.5)));
        transforms.Children.Add(new TranslateTransform3D(sign * hipHalfW * 0.46, hipTop, 0));

        leg.Transform = transforms;
        group.Children.Add(leg);
    }

    /// <summary>BackMaterial is always set: it keeps a part visible even if a ring's winding ends up
    /// reversed, which would otherwise show as an invisible limb.</summary>
    private static GeometryModel3D Model(MeshGeometry3D mesh, Material material, Transform3D? transform = null) =>
        new(mesh, material) { BackMaterial = material, Transform = transform ?? Transform3D.Identity };
}

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
/// Assembles the figure from spline-lofted cross-sections.
///
/// Vertical landmarks are fixed fractions of overall height — standard anatomical proportions — while
/// every width comes from a real measurement. That split is deliberate: the figure changes shape as
/// the numbers change, but a missing measurement can never stretch it into a funhouse mirror.
///
/// Each part is returned tagged with the <see cref="GoalMetric"/> it represents so the view can hit-test
/// a click straight to the measurement it should edit.
/// </summary>
public static class BodyFigureBuilder
{
    // Depth-to-width ratios. A torso is markedly flatter front-to-back than a limb, which is close to
    // round; using one ratio everywhere is what makes procedural bodies look like plumbing.
    private const double TorsoDepthRatio = 0.72;
    private const double LimbDepthRatio = 0.92;
    private const double NeckDepthRatio = 0.88;

    // Vertical landmarks as fractions of total height.
    private const double AnkleY = 0.039, KneeY = 0.285, HipY = 0.53;
    private const double WaistY = 0.615, ChestY = 0.72, ShoulderY = 0.815, ChinY = 0.868;

    public static IReadOnlyList<(GeometryModel3D Model, GoalMetric? Metric)> Build(FigureMeasurements m, Material material)
    {
        var h = m.HeightInches;
        var parts = new List<(GeometryModel3D, GoalMetric?)>();

        var (hipW, hipD) = HumanMeshBuilder.FromCircumference(m.Hips, TorsoDepthRatio);
        var (waistW, waistD) = HumanMeshBuilder.FromCircumference(m.Waist, TorsoDepthRatio);
        var (chestW, chestD) = HumanMeshBuilder.FromCircumference(m.Chest, TorsoDepthRatio);
        var (neckW, neckD) = HumanMeshBuilder.FromCircumference(m.Neck, NeckDepthRatio);

        // Shoulders read wider and flatter than the chest measurement alone gives.
        var shoulderW = chestW * 1.27;

        // The torso is one continuous loft from pelvis to trapezius. Squareness near 2.5 gives the
        // rounded-rectangle section a real ribcage has, and the front depths run deeper than the back
        // because chest and belly project forward while the back is comparatively flat.
        var torso = HumanMeshBuilder.BuildLoft(
        [
            new Section(h * 0.455, hipW * 0.80, hipD * 0.78, hipD * 0.80, 2.3),
            new Section(h * 0.490, hipW * 0.97, hipD * 0.94, hipD * 1.02, 2.5),
            new Section(h * HipY, hipW, hipD * 0.98, hipD * 1.06, 2.6),
            new Section(h * 0.570, waistW * 1.05, waistD * 1.10, waistD * 0.92, 2.6),
            new Section(h * WaistY, waistW, waistD * 1.12, waistD * 0.88, 2.5),
            new Section(h * 0.665, waistW * 1.06, waistD * 1.10, waistD * 0.90, 2.5),
            new Section(h * ChestY, chestW, chestD * 1.06, chestD * 0.92, 2.5),
            new Section(h * 0.768, chestW * 1.02, chestD * 0.98, chestD * 0.90, 2.4),
            new Section(h * ShoulderY, shoulderW, chestD * 0.86, chestD * 0.84, 2.6),
            new Section(h * 0.836, shoulderW * 0.62, chestD * 0.66, chestD * 0.68, 2.4),
        ], segments: 40, subdivisions: 7);

        // Torso covers chest, waist and hips, so its click target is resolved by height at the hit
        // point rather than by the model itself.
        parts.Add((Tagged(torso, material), GoalMetric.Chest));

        parts.Add((Tagged(HumanMeshBuilder.BuildLoft(
        [
            new Section(h * 0.826, neckW * 1.34, neckD * 1.30, neckD * 1.34, 2.4),
            new Section(h * 0.845, neckW * 1.06, neckD * 1.04, neckD * 1.06, 2.2),
            new Section(h * 0.858, neckW, neckD, neckD, 2.1),
            new Section(h * (ChinY + 0.004), neckW * 0.96, neckD * 0.98, neckD * 0.96, 2.1),
        ], segments: 32, subdivisions: 5), material), GoalMetric.Neck));

        // Head is lofted rather than a sphere: a chin, jaw, cheekbones and a crown are what stop it
        // reading as a ball on a stick. Sized from height, since head circumference is not tracked.
        parts.Add((Tagged(HumanMeshBuilder.BuildLoft(
        [
            new Section(h * 0.870, h * 0.021, h * 0.030, h * 0.022, 2.2),
            new Section(h * 0.881, h * 0.034, h * 0.043, h * 0.032, 2.3),
            new Section(h * 0.895, h * 0.043, h * 0.050, h * 0.041, 2.3),
            new Section(h * 0.912, h * 0.047, h * 0.052, h * 0.047, 2.2),
            new Section(h * 0.935, h * 0.048, h * 0.051, h * 0.050, 2.1),
            new Section(h * 0.960, h * 0.045, h * 0.047, h * 0.048, 2.1),
            new Section(h * 0.980, h * 0.036, h * 0.038, h * 0.039, 2.0),
            new Section(h * 0.996, h * 0.016, h * 0.017, h * 0.018, 2.0),
        ], segments: 34, subdivisions: 6), material), null));

        AddArm(parts, material, m.BicepRight, h, shoulderW, isRight: true);
        AddArm(parts, material, m.BicepLeft, h, shoulderW, isRight: false);

        AddLeg(parts, material, m.ThighRight, m.CalfRight, h, hipW, isRight: true);
        AddLeg(parts, material, m.ThighLeft, m.CalfLeft, h, hipW, isRight: false);

        return parts;
    }

    /// <summary>
    /// Arms are lofted vertically in local space with the shoulder at the origin, then rotated and
    /// translated into place — far simpler than computing rotated slice positions, and the hand rides
    /// along automatically.
    /// </summary>
    private static void AddArm(
        List<(GeometryModel3D, GoalMetric?)> parts, Material material,
        double bicep, double h, double shoulderW, bool isRight)
    {
        var (r, d) = HumanMeshBuilder.FromCircumference(bicep, LimbDepthRatio);
        var sign = isRight ? 1 : -1;

        var arm = HumanMeshBuilder.BuildLoft(
        [
            new Section(h * 0.020, r * 0.92, d * 0.92, d * 0.94, 2.4),   // buried in the shoulder
            new Section(h * 0.006, r * 1.14, d * 1.12, d * 1.14, 2.3),
            new Section(-h * 0.018, r * 1.20, d * 1.17, d * 1.19, 2.2),  // deltoid
            new Section(-h * 0.045, r * 1.10, d * 1.08, d * 1.10, 2.1),
            new Section(-h * 0.090, r, d, d, 2.0),                       // bicep belly
            new Section(-h * 0.140, r * 0.90, d * 0.90, d * 0.90, 2.0),
            new Section(-h * 0.185, r * 0.76, d * 0.80, d * 0.78, 2.1),  // elbow
            new Section(-h * 0.225, r * 0.84, d * 0.84, d * 0.82, 2.0),  // forearm belly
            new Section(-h * 0.290, r * 0.68, d * 0.68, d * 0.66, 2.0),
            new Section(-h * 0.345, r * 0.50, d * 0.44, d * 0.44, 2.2),  // wrist
        ], segments: 30, subdivisions: 6);

        // Hand: flattened and tapered rather than a sphere, which reads as a paddle at this scale.
        var hand = HumanMeshBuilder.BuildLoft(
        [
            new Section(-h * 0.335, r * 0.50, d * 0.43, d * 0.43, 2.3),
            new Section(-h * 0.372, r * 0.62, d * 0.40, d * 0.40, 2.8),
            new Section(-h * 0.400, r * 0.60, d * 0.36, d * 0.36, 2.9),
            new Section(-h * 0.424, r * 0.44, d * 0.28, d * 0.28, 2.6),
        ], segments: 26, subdivisions: 5);

        var transforms = new Transform3DGroup();
        // Positive rotation about Z swings a downward-pointing limb toward +X, so the right arm takes
        // a positive angle: a relaxed A-pose that clears the torso instead of clipping into it.
        transforms.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), sign * 9)));
        transforms.Children.Add(new TranslateTransform3D(sign * (shoulderW - r * 0.42), h * (ShoulderY - 0.004), 0));

        var part = isRight ? GoalMetric.BicepRight : GoalMetric.BicepLeft;
        foreach (var mesh in new[] { arm, hand })
        {
            var model = Tagged(mesh, material);
            model.Transform = transforms;
            parts.Add((model, part));
        }
    }

    private static void AddLeg(
        List<(GeometryModel3D, GoalMetric?)> parts, Material material,
        double thigh, double calf, double h, double hipW, bool isRight)
    {
        var (thighW, thighD) = HumanMeshBuilder.FromCircumference(thigh, LimbDepthRatio);
        var (calfW, calfD) = HumanMeshBuilder.FromCircumference(calf, LimbDepthRatio);
        var sign = isRight ? 1 : -1;
        var hipTop = h * HipY;

        // Local Y is measured down from the hip, so each landmark is the drop from HipY to it.
        double Drop(double fraction) => -(HipY - fraction) * h;

        var leg = HumanMeshBuilder.BuildLoft(
        [
            new Section(h * 0.020, thighW * 1.02, thighD * 1.00, thighD * 1.06, 2.4),
            new Section(0, thighW * 1.10, thighD * 1.06, thighD * 1.14, 2.3),   // glute / hip blend
            new Section(Drop(0.470), thighW * 1.04, thighD * 1.02, thighD * 1.06, 2.2),
            new Section(Drop(0.420), thighW, thighD, thighD, 2.1),              // thigh belly
            new Section(Drop(0.360), thighW * 0.88, thighD * 0.88, thighD * 0.88, 2.1),
            new Section(Drop(0.310), thighW * 0.74, thighD * 0.76, thighD * 0.74, 2.1),
            new Section(Drop(KneeY), thighW * 0.66, thighD * 0.70, thighD * 0.66, 2.2),  // knee
            new Section(Drop(0.250), calfW * 1.00, calfD * 0.98, calfD * 1.06, 2.1),
            new Section(Drop(0.200), calfW, calfD * 0.96, calfD * 1.10, 2.0),   // calf belly
            new Section(Drop(0.130), calfW * 0.78, calfD * 0.76, calfD * 0.80, 2.0),
            new Section(Drop(0.075), calfW * 0.60, calfD * 0.56, calfD * 0.58, 2.1),
            new Section(Drop(AnkleY), calfW * 0.50, calfD * 0.46, calfD * 0.48, 2.2),  // ankle
        ], segments: 32, subdivisions: 6);

        // Foot is lofted along its own axis then tipped forward, so it is a wedge pointing ahead
        // rather than a ball at the ankle.
        var foot = HumanMeshBuilder.BuildLoft(
        [
            new Section(0, calfW * 0.46, h * 0.012, h * 0.012, 2.4),
            new Section(h * 0.030, calfW * 0.60, h * 0.020, h * 0.020, 2.7),
            new Section(h * 0.070, calfW * 0.58, h * 0.016, h * 0.016, 2.8),
            new Section(h * 0.098, calfW * 0.44, h * 0.010, h * 0.010, 2.6),
        ], segments: 24, subdivisions: 4);

        var footTransforms = new Transform3DGroup();
        // +90° about X maps the foot's local +Y (its length) onto +Z, pointing it forward.
        footTransforms.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 90)));
        footTransforms.Children.Add(new TranslateTransform3D(0, Drop(AnkleY) - h * 0.020, -h * 0.022));

        var legTransforms = new Transform3DGroup();
        legTransforms.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), sign * 1.8)));
        legTransforms.Children.Add(new TranslateTransform3D(sign * hipW * 0.40, hipTop, 0));

        var thighPart = isRight ? GoalMetric.ThighRight : GoalMetric.ThighLeft;
        var legModel = Tagged(leg, material);
        legModel.Transform = legTransforms;
        parts.Add((legModel, thighPart));

        var footModel = Tagged(foot, material);
        var footFull = new Transform3DGroup();
        footFull.Children.Add(footTransforms);
        footFull.Children.Add(legTransforms);
        footModel.Transform = footFull;
        parts.Add((footModel, null));
    }

    /// <summary>
    /// The torso is a single mesh spanning hips, waist and chest, so a click on it is resolved by how
    /// high up the body it landed rather than by which model was hit.
    /// </summary>
    public static GoalMetric ResolveTorsoPart(double localY, double heightInches) => localY switch
    {
        _ when localY <= heightInches * 0.565 => GoalMetric.Hips,
        _ when localY <= heightInches * 0.668 => GoalMetric.Waist,
        _ when localY <= heightInches * 0.790 => GoalMetric.Chest,
        _ => GoalMetric.Neck,
    };

    /// <summary>BackMaterial is always set: it keeps a part visible even if a slice's winding ends up
    /// reversed, which would otherwise show as an invisible limb.</summary>
    private static GeometryModel3D Tagged(MeshGeometry3D mesh, Material material) =>
        new(mesh, material) { BackMaterial = material };
}

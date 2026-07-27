using AaronOS.Modules.BodyMeasurements.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AaronOS.Modules.BodyMeasurements.Views.Body3D;

/// <summary>
/// Reshapes the base human mesh to match a set of recorded measurements.
///
/// Each vertex is pushed away from (or pulled toward) the axis of the region it belongs to, by the
/// ratio between a recorded circumference and the base mesh's own circumference at the same place.
/// Because every vertex carries a feathered weight per region, a change to one measurement fades out
/// across the neighbouring anatomy instead of stopping at a seam.
///
/// Vertical proportions are never scaled independently: the whole mesh is scaled by height, and only
/// girth responds to the tape measure. That is the difference between a figure that reflects real
/// numbers and one that can be stretched into a funhouse mirror by a single typo.
/// </summary>
public static class BodyMeshDeformer
{
    // Landmark heights as fractions of total height, which is where each measurement is actually
    // taken on a body. They are converted to each region's own axis parameter at run time, so they
    // stay meaningful for the arms and legs even though those axes run diagonally.
    private const double HipsY = 0.530, WaistY = 0.615, ChestY = 0.720, NeckY = 0.857;
    private const double ThighY = 0.410, CalfY = 0.225, AnkleY = 0.039;

    // Where the neck and head begin, matching the boundaries the asset was segmented on: by 0.840 the
    // shoulders have ended, and by 0.874 the jaw has begun.
    private const double NeckBottomY = 0.840, HeadBottomY = 0.874;

    // The bicep is given as a position along the arm's own axis rather than a height: the arm runs
    // diagonally from shoulder to hand, so "20% of the way down the arm" locates the bicep belly far
    // more reliably than any height fraction.
    private const double BicepT = 0.20;

    // A measurement well outside the plausible range — a typo, or a base girth taken from a slice the
    // measuring plane barely clipped — must not be allowed to turn the figure inside out.
    private const double MinScale = 0.55, MaxScale = 1.90;

    /// <summary>Builds the figure at the given measurements, in inches, with the feet at Y=0.</summary>
    public static MeshGeometry3D Build(FigureMeasurements m)
    {
        var mesh = HumanBaseMesh.Instance;
        var height = m.HeightInches;
        var scales = ScaleCurves(mesh, m);

        var positions = new Point3D[mesh.Positions.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            var p = mesh.Positions[i];
            var displacement = new Vector3D();

            for (var r = 0; r < mesh.Regions.Length; r++)
            {
                var weight = mesh.Regions[r].WeightAt(i);
                if (weight <= 0)
                {
                    continue;
                }

                // The scale comes from the clamped position along the axis, but the direction it acts in
                // comes from the unclamped one, so vertices past either end of a region grow sideways
                // rather than being pushed off the end of the axis.
                var region = mesh.Regions[r];
                displacement += weight * (scales[r].At(region.Parameter(p)) - 1) * region.Radial(p);
            }

            var moved = p + displacement;
            positions[i] = new Point3D(moved.X * height, moved.Y * height, moved.Z * height);
        }

        return new MeshGeometry3D
        {
            Positions = new Point3DCollection(positions),
            TriangleIndices = new Int32Collection(mesh.Indices),
            Normals = new Vector3DCollection(SmoothNormals(positions, mesh.Indices)),
        };
    }

    /// <summary>
    /// Which measurement a click landed on. The whole figure is one mesh, so this is resolved
    /// geometrically: the region whose axis the hit point sits closest to, relative to that region's
    /// own thickness, is the region the surface belongs to.
    /// </summary>
    public static GoalMetric? ResolveMetric(Point3D hit, double heightInches)
    {
        if (heightInches <= 0)
        {
            return null;
        }

        var mesh = HumanBaseMesh.Instance;
        var p = new Point3D(hit.X / heightInches, hit.Y / heightInches, hit.Z / heightInches);

        // Head and neck are settled by height, the same way the asset defines them. The neck is only
        // a couple of inches tall, so competing on distance-to-axis against the far larger torso is
        // unreliable there; above the shoulders, height alone is unambiguous.
        if (p.Y > HeadBottomY)
        {
            return null;   // the head has no measurement behind it, so clicks on it open nothing
        }

        if (p.Y > NeckBottomY)
        {
            return GoalMetric.Neck;
        }

        var best = -1;
        var bestRatio = double.MaxValue;
        var bestT = 0.0;

        for (var r = 0; r < mesh.Regions.Length; r++)
        {
            if (r == BodyRegionKind.Neck)
            {
                continue;   // already settled by height above
            }

            var region = mesh.Regions[r];
            var t = region.Parameter(p);
            var radius = region.GirthAt(t) / (2 * Math.PI);
            if (radius <= 0)
            {
                continue;
            }

            // Distance to the axis divided by the region's own radius: a point on this region's
            // surface scores about 1, while a point on any other region scores well above it.
            var ratio = (p - region.AxisPoint(t)).Length / radius;
            if (ratio < bestRatio)
            {
                (best, bestRatio, bestT) = (r, ratio, t);
            }
        }

        return best switch
        {
            BodyRegionKind.ArmRight => GoalMetric.BicepRight,
            BodyRegionKind.ArmLeft => GoalMetric.BicepLeft,
            BodyRegionKind.LegRight => KneeOrBelow(bestT) ? GoalMetric.CalfRight : GoalMetric.ThighRight,
            BodyRegionKind.LegLeft => KneeOrBelow(bestT) ? GoalMetric.CalfLeft : GoalMetric.ThighLeft,
            BodyRegionKind.Torso => TorsoMetric(mesh.Regions[BodyRegionKind.Torso], bestT),
            _ => null,
        };

        bool KneeOrBelow(double t) => t > Midpoint(mesh.Regions[BodyRegionKind.LegRight], ThighY, CalfY);
    }

    private static GoalMetric TorsoMetric(BodyRegion torso, double t)
    {
        if (t < Midpoint(torso, HipsY, WaistY))
        {
            return GoalMetric.Hips;
        }

        return t < Midpoint(torso, WaistY, ChestY) ? GoalMetric.Waist : GoalMetric.Chest;
    }

    private static double Midpoint(BodyRegion region, double firstY, double secondY) =>
        (ParameterAtHeight(region, firstY) + ParameterAtHeight(region, secondY)) / 2;

    /// <summary>
    /// Where a height sits along a region's axis. Every axis has a non-zero vertical component — the
    /// torso and neck run straight up, the limbs run downward and outward — so a height always maps
    /// to exactly one point along it.
    /// </summary>
    private static double ParameterAtHeight(BodyRegion region, double heightFraction) =>
        (heightFraction - region.Origin.Y) / (region.Direction.Y * region.Length);

    private static Ramp[] ScaleCurves(HumanBaseMesh mesh, FigureMeasurements m)
    {
        var curves = new Ramp[mesh.Regions.Length];
        var height = m.HeightInches;

        // Each factor is a recorded circumference over the base mesh's own circumference at the same
        // landmark, which is why the base asset stores real girths rather than raw radii.
        double Factor(int regionIndex, double t, double measured)
        {
            var baseInches = mesh.Regions[regionIndex].GirthAt(t) * height;
            return baseInches <= 0 ? 1 : Math.Clamp(measured / baseInches, MinScale, MaxScale);
        }

        var torso = mesh.Regions[BodyRegionKind.Torso];
        double tHips = ParameterAtHeight(torso, HipsY),
               tWaist = ParameterAtHeight(torso, WaistY),
               tChest = ParameterAtHeight(torso, ChestY);

        var hipsFactor = Factor(BodyRegionKind.Torso, tHips, m.Hips);
        var chestFactor = Factor(BodyRegionKind.Torso, tChest, m.Chest);

        // Above the chest the factor eases off rather than carrying through at full strength. A broader
        // chest does come with a broader upper back, but shoulder width is skeletal and does not track
        // chest circumference — applying the full factor at the shoulders visibly overshoots.
        curves[BodyRegionKind.Torso] = new Ramp(
        [
            (0, hipsFactor),
            (tHips, hipsFactor),
            (tWaist, Factor(BodyRegionKind.Torso, tWaist, m.Waist)),
            (tChest, chestFactor),
            (1, 1 + ((chestFactor - 1) * 0.45)),
        ]);

        var neck = mesh.Regions[BodyRegionKind.Neck];
        var neckFactor = Factor(BodyRegionKind.Neck, ParameterAtHeight(neck, NeckY), m.Neck);
        curves[BodyRegionKind.Neck] = new Ramp([(0, neckFactor), (1, neckFactor)]);

        curves[BodyRegionKind.ArmRight] = ArmCurve(BodyRegionKind.ArmRight, m.BicepRight);
        curves[BodyRegionKind.ArmLeft] = ArmCurve(BodyRegionKind.ArmLeft, m.BicepLeft);
        curves[BodyRegionKind.LegRight] = LegCurve(BodyRegionKind.LegRight, m.ThighRight, m.CalfRight);
        curves[BodyRegionKind.LegLeft] = LegCurve(BodyRegionKind.LegLeft, m.ThighLeft, m.CalfLeft);

        return curves;

        // Only the bicep is measured, but an arm that thickens at the bicep and stays average at the
        // forearm reads as a cartoon. The factor carries down the arm at a decreasing share and
        // reaches average at the hand, which is never scaled.
        Ramp ArmCurve(int regionIndex, double bicep)
        {
            var f = Factor(regionIndex, BicepT, bicep);
            return new Ramp(
            [
                (0, f),
                (0.25, f),
                (0.55, 1 + ((f - 1) * 0.60)),
                (0.85, 1 + ((f - 1) * 0.25)),
                (1, 1),
            ]);
        }

        // Thigh and calf are both measured, so the leg interpolates between them through the knee and
        // returns to average at the ankle — a heavier calf must not also inflate the foot.
        Ramp LegCurve(int regionIndex, double thigh, double calf)
        {
            var region = mesh.Regions[regionIndex];
            double tThigh = ParameterAtHeight(region, ThighY),
                   tCalf = ParameterAtHeight(region, CalfY),
                   tAnkle = ParameterAtHeight(region, AnkleY);

            var thighFactor = Factor(regionIndex, tThigh, thigh);
            return new Ramp(
            [
                (0, thighFactor),
                (tThigh, thighFactor),
                (tCalf, Factor(regionIndex, tCalf, calf)),
                (tAnkle, 1),
                (1, 1),
            ]);
        }
    }

    /// <summary>
    /// Area-weighted vertex normals. They have to be recomputed after deformation — reusing the base
    /// mesh's normals would light the figure as though it had never changed shape — and weighting by
    /// triangle area rather than averaging face normals evenly keeps the shading smooth where the
    /// mesh's triangle density changes, as it does sharply around the face and hands.
    /// </summary>
    private static Vector3D[] SmoothNormals(Point3D[] positions, int[] indices)
    {
        var normals = new Vector3D[positions.Length];

        for (var i = 0; i < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];

            // The cross product's magnitude is twice the triangle's area, so leaving it un-normalised
            // is what gives the area weighting.
            var face = Vector3D.CrossProduct(positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += face;
            normals[b] += face;
            normals[c] += face;
        }

        for (var i = 0; i < normals.Length; i++)
        {
            // A vertex on a degenerate triangle has no defined direction; pointing it outward from the
            // body's axis is wrong in general but never produces the black shading a zero normal does.
            if (normals[i].LengthSquared < 1e-12)
            {
                normals[i] = new Vector3D(positions[i].X, 0, positions[i].Z);
                if (normals[i].LengthSquared < 1e-12)
                {
                    normals[i] = new Vector3D(0, 1, 0);
                }
            }

            normals[i].Normalize();
        }

        return normals;
    }
}

/// <summary>A piecewise-linear curve over a region's axis, given as knots in increasing order.</summary>
public readonly struct Ramp((double T, double Value)[] knots)
{
    private readonly (double T, double Value)[] _knots = knots;

    public double At(double t)
    {
        // Hold at the first knot rather than extrapolating backwards from it. Positions along an axis
        // are clamped before they get here, but an unclamped value would otherwise run the scale
        // factor negative and turn that part of the mesh inside out.
        if (t <= _knots[0].T)
        {
            return _knots[0].Value;
        }

        for (var i = 1; i < _knots.Length; i++)
        {
            if (t > _knots[i].T)
            {
                continue;
            }

            var (t0, v0) = _knots[i - 1];
            var (t1, v1) = _knots[i];
            var span = t1 - t0;
            return span <= 0 ? v1 : v0 + ((v1 - v0) * (t - t0) / span);
        }

        return _knots[^1].Value;
    }
}

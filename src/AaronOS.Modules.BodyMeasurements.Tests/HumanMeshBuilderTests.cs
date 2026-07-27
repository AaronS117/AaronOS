using AaronOS.Modules.BodyMeasurements.Data;
using AaronOS.Modules.BodyMeasurements.Views.Body3D;
using System.Windows.Media.Media3D;

namespace AaronOS.Modules.BodyMeasurements.Tests;

/// <summary>
/// The figure is generated geometry, and geometry fails quietly: a NaN position or a zero-length
/// normal produces an invisible or black model rather than an exception, which is near-impossible to
/// diagnose by looking at a rendered window. These assertions cover the arithmetic directly.
/// </summary>
public class HumanMeshBuilderTests
{
    [Fact]
    public void FromCircumference_InvertsThePerimeterApproximation()
    {
        // A 40" chest is roughly 13" across, not 40" — the conversion is the whole point.
        var (halfWidth, halfDepth) = HumanMeshBuilder.FromCircumference(40, 0.72);

        Assert.InRange(halfWidth * 2, 12.0, 15.0);
        Assert.InRange(halfDepth / halfWidth, 0.71, 0.73);

        // pi * (a + b) should return the circumference it came from.
        Assert.Equal(40, Math.PI * (halfWidth + halfDepth), precision: 6);
    }

    [Fact]
    public void FromCircumference_ScalesLinearly()
    {
        var (small, _) = HumanMeshBuilder.FromCircumference(20, 0.9);
        var (large, _) = HumanMeshBuilder.FromCircumference(40, 0.9);

        Assert.Equal(2.0, large / small, precision: 9);
    }

    [Fact]
    public void BuildTube_ProducesFiniteGeometryWithUnitNormals()
    {
        var mesh = HumanMeshBuilder.BuildTube(
        [
            new Ring(0, 4, 3),
            new Ring(10, 6, 4),
            new Ring(20, 2, 1.5),
        ], segments: 16);

        Assert.NotEmpty(mesh.Positions);
        Assert.Equal(mesh.Positions.Count, mesh.Normals.Count);
        Assert.True(mesh.TriangleIndices.Count % 3 == 0);

        foreach (var p in mesh.Positions)
        {
            Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z), "position must be finite");
        }

        foreach (var n in mesh.Normals)
        {
            Assert.Equal(1.0, n.Length, precision: 6);
        }

        // Every index must address a real vertex, or WPF silently drops triangles.
        foreach (var index in mesh.TriangleIndices)
        {
            Assert.InRange(index, 0, mesh.Positions.Count - 1);
        }
    }

    [Fact]
    public void BuildTube_NormalsPointOutwardNotInward()
    {
        var mesh = HumanMeshBuilder.BuildTube(
            [new Ring(0, 5, 5), new Ring(10, 5, 5)], segments: 12, capEnds: false);

        // On a straight cylinder every side normal should agree with the outward radial direction.
        for (var i = 0; i < mesh.Positions.Count; i++)
        {
            var radial = new Vector3D(mesh.Positions[i].X, 0, mesh.Positions[i].Z);
            radial.Normalize();
            Assert.True(Vector3D.DotProduct(radial, mesh.Normals[i]) > 0.9, "normal should face outward");
        }
    }

    /// <summary>
    /// Triangle winding must face outward whichever way the ring stack runs. Limbs are built
    /// downward from a joint and the torso upward from the pelvis; when winding silently depended on
    /// that direction, the torso rendered its inside and appeared as a flat unlit slab while every
    /// limb looked correct. Checking the geometric normal (from the vertex order) rather than the
    /// supplied normals is what catches it.
    /// </summary>
    [Theory]
    [InlineData(0.0, 10.0)]   // ascending, as the torso is built
    [InlineData(0.0, -10.0)]  // descending, as limbs are built
    public void BuildTube_TriangleWindingFacesOutwardInBothDirections(double startY, double endY)
    {
        var mesh = HumanMeshBuilder.BuildTube(
            [new Ring(startY, 5, 5), new Ring(endY, 5, 5)], segments: 16, capEnds: false);

        for (var t = 0; t < mesh.TriangleIndices.Count; t += 3)
        {
            var p0 = mesh.Positions[mesh.TriangleIndices[t]];
            var p1 = mesh.Positions[mesh.TriangleIndices[t + 1]];
            var p2 = mesh.Positions[mesh.TriangleIndices[t + 2]];

            // Counter-clockwise vertex order, viewed from outside, is what WPF treats as front-facing.
            var geometric = Vector3D.CrossProduct(p1 - p0, p2 - p0);
            geometric.Normalize();

            var centroid = new Point3D((p0.X + p1.X + p2.X) / 3, 0, (p0.Z + p1.Z + p2.Z) / 3);
            var outward = new Vector3D(centroid.X, 0, centroid.Z);
            outward.Normalize();

            Assert.True(
                Vector3D.DotProduct(geometric, outward) > 0.5,
                $"triangle at index {t} faces inward for span {startY}->{endY}");
        }
    }

    [Fact]
    public void BuildTube_DegenerateRingDoesNotProduceNaNNormals()
    {
        // A pinched tip has zero radius, where the usual tangent cross product collapses.
        var mesh = HumanMeshBuilder.BuildTube(
            [new Ring(0, 4, 4), new Ring(10, 0, 0)], segments: 10);

        foreach (var n in mesh.Normals)
        {
            Assert.False(double.IsNaN(n.X) || double.IsNaN(n.Y) || double.IsNaN(n.Z), "normal must be finite");
        }
    }

    [Fact]
    public void BuildEllipsoid_IsClosedAndFinite()
    {
        var mesh = HumanMeshBuilder.BuildEllipsoid(3, 4, 2, segments: 12, stacks: 8);

        Assert.NotEmpty(mesh.Positions);
        Assert.Equal(mesh.Positions.Count, mesh.Normals.Count);

        foreach (var p in mesh.Positions)
        {
            // Every point must satisfy the ellipsoid equation, within rounding.
            var v = Math.Pow(p.X / 3, 2) + Math.Pow(p.Y / 4, 2) + Math.Pow(p.Z / 2, 2);
            Assert.InRange(v, 0.999, 1.001);
        }
    }

    [Fact]
    public void Figure_UsesRecordedMeasurementsAndFallsBackPerValue()
    {
        // Only a waist recorded: the waist must be honoured and everything else defaulted, rather
        // than the whole figure reverting to averages.
        var checkIn = new BodyCheckIn { WaistIn = 30m };
        var m = FigureMeasurements.FromCheckIn(checkIn, heightInches: 72m);

        Assert.Equal(30, m.Waist);
        Assert.Equal(72, m.HeightInches);
        Assert.Equal(40, m.Chest);
        Assert.True(FigureMeasurements.HasAnyMeasurement(checkIn));
    }

    [Fact]
    public void Figure_WithNoMeasurementsIsStillFullyDefined()
    {
        var m = FigureMeasurements.FromCheckIn(null, null);

        Assert.False(FigureMeasurements.HasAnyMeasurement(null));
        Assert.All(
            new[] { m.HeightInches, m.Neck, m.Chest, m.Waist, m.Hips, m.BicepLeft, m.ThighLeft, m.CalfLeft },
            value => Assert.True(value > 0, "every dimension needs a usable default so the model always renders"));
    }

    [Fact]
    public void Figure_WeightOnlyCheckInCountsAsNoMeasurements()
    {
        // A weigh-in with no tape measurements should still show the dimmed reference figure.
        Assert.False(FigureMeasurements.HasAnyMeasurement(new BodyCheckIn { WeightLb = 185m }));
    }

    [Fact]
    public void Build_TagsEveryClickablePartWithAMeasurement()
    {
        var m = FigureMeasurements.FromCheckIn(null, 70m);
        var parts = BodyFigureBuilder.Build(m, new DiffuseMaterial());

        Assert.All(parts, p => Assert.NotNull(p.Model));

        // Every limb measurement must be reachable by clicking, or the edit affordance is a dead end.
        var tagged = parts.Select(p => p.Metric).ToHashSet();
        Assert.Contains(GoalMetric.Neck, tagged);
        Assert.Contains(GoalMetric.BicepLeft, tagged);
        Assert.Contains(GoalMetric.BicepRight, tagged);
        Assert.Contains(GoalMetric.ThighLeft, tagged);
        Assert.Contains(GoalMetric.ThighRight, tagged);
    }

    [Theory]
    [InlineData(0.50, GoalMetric.Hips)]
    [InlineData(0.62, GoalMetric.Waist)]
    [InlineData(0.73, GoalMetric.Chest)]
    public void ResolveTorsoPart_MapsHeightToTheRightMeasurement(double heightFraction, GoalMetric expected)
    {
        // The torso is one mesh covering three measurements, so a click is resolved by height.
        Assert.Equal(expected, BodyFigureBuilder.ResolveTorsoPart(70 * heightFraction, 70));
    }

    [Fact]
    public void BuildLoft_SubdividesIntoASmootherSurfaceThanItsControlSections()
    {
        var sections = new List<Section>
        {
            new(0, 4, 3, 3),
            new(10, 6, 4, 4),
            new(20, 2, 1.5, 1.5),
        };

        var coarse = HumanMeshBuilder.BuildLoft(sections, segments: 12, subdivisions: 1, capEnds: false);
        var smooth = HumanMeshBuilder.BuildLoft(sections, segments: 12, subdivisions: 6, capEnds: false);

        Assert.True(smooth.Positions.Count > coarse.Positions.Count * 4, "subdivision should add slices");
        Assert.All(smooth.Normals, n => Assert.Equal(1.0, n.Length, precision: 6));
        Assert.All(smooth.Positions, p =>
            Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z)));
    }

    [Fact]
    public void BuildLoft_SquarenessFillsTheCornersOfTheSection()
    {
        // A superelliptical torso section must enclose more area than the ellipse through the same
        // semi-axes; that extra corner fill is what gives a ribcage its shape.
        var ellipse = HumanMeshBuilder.BuildLoft(
            [new Section(0, 5, 3, 3, 2.0), new Section(10, 5, 3, 3, 2.0)], segments: 64, subdivisions: 1, capEnds: false);
        var rounded = HumanMeshBuilder.BuildLoft(
            [new Section(0, 5, 3, 3, 3.0), new Section(10, 5, 3, 3, 3.0)], segments: 64, subdivisions: 1, capEnds: false);

        static double DiagonalReach(MeshGeometry3D mesh) =>
            mesh.Positions.Max(p => Math.Abs(p.X) + Math.Abs(p.Z));

        Assert.True(DiagonalReach(rounded) > DiagonalReach(ellipse), "squareness should push the corners out");
    }

    [Fact]
    public void BuildLoft_FrontDepthIsAppliedForwardAndBackDepthBehind()
    {
        // A belly projects further forward than the back does behind; the section must be able to
        // express that rather than being forced symmetric.
        var mesh = HumanMeshBuilder.BuildLoft(
            [new Section(0, 5, 6, 2), new Section(10, 5, 6, 2)], segments: 48, subdivisions: 1, capEnds: false);

        Assert.Equal(6, mesh.Positions.Max(p => p.Z), precision: 3);
        Assert.Equal(-2, mesh.Positions.Min(p => p.Z), precision: 3);
    }
}

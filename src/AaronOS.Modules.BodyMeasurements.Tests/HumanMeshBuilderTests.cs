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
    public void Build_AssemblesEveryBodyPart()
    {
        var m = FigureMeasurements.FromCheckIn(null, 70m);
        var figure = BodyFigureBuilder.Build(m, new DiffuseMaterial());

        // torso, neck, head, two arms, two legs
        Assert.Equal(7, figure.Children.Count);
        Assert.All(figure.Children, child => Assert.NotNull(child));
    }
}

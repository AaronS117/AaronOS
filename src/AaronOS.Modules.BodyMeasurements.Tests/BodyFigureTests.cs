using AaronOS.Modules.BodyMeasurements.Data;
using AaronOS.Modules.BodyMeasurements.Views.Body3D;
using System.Windows.Media.Media3D;

namespace AaronOS.Modules.BodyMeasurements.Tests;

/// <summary>
/// The figure is a mesh reshaped by arithmetic, and geometry fails quietly: a NaN position or a
/// zero-length normal produces an invisible or black model rather than an exception, which is
/// near-impossible to diagnose from a rendered window. These assertions cover the arithmetic directly,
/// and pin the behaviour of the two bugs that were hardest to see — girth being measured from a
/// clamped axis end (which lifted the shoulders into spikes) and clicks resolving to the wrong
/// measurement.
/// </summary>
public class BodyFigureTests
{
    private static readonly HumanBaseMesh Mesh = HumanBaseMesh.Instance;

    [Fact]
    public void Asset_LoadsWithEveryRegionPopulated()
    {
        Assert.Equal(BodyRegionKind.Names.Length, Mesh.Regions.Length);
        Assert.True(Mesh.Positions.Length > 10_000, "the base mesh should carry real anatomical detail");
        Assert.True(Mesh.Indices.Length % 3 == 0);

        // ushort indices are only safe below 65,536 vertices, which the build script asserts.
        Assert.True(Mesh.Positions.Length < 65_536);
        Assert.All(Mesh.Indices, i => Assert.InRange(i, 0, Mesh.Positions.Length - 1));

        for (var r = 0; r < Mesh.Regions.Length; r++)
        {
            var region = Mesh.Regions[r];
            Assert.Equal(BodyRegionKind.Names[r], region.Name);
            Assert.True(region.Length > 0, $"{region.Name} needs a real axis");
            Assert.Equal(1.0, region.Direction.Length, precision: 3);
            Assert.Equal(Mesh.Positions.Length, region.Weights.Length);
            Assert.All(region.Girth, g => Assert.True(g > 0, $"{region.Name} has an unmeasured band"));

            // Every axis must have a vertical component, or a landmark height cannot be mapped onto it.
            Assert.True(Math.Abs(region.Direction.Y) > 0.1, $"{region.Name} axis is too horizontal");
        }
    }

    [Fact]
    public void Asset_IsNormalisedToUnitHeightStandingOnTheOrigin()
    {
        Assert.Equal(0.0, Mesh.Positions.Min(p => p.Y), precision: 3);
        Assert.Equal(1.0, Mesh.Positions.Max(p => p.Y), precision: 3);

        // Centred left-to-right, so scaling by height does not drift the figure sideways.
        Assert.Equal(0.0, (Mesh.Positions.Min(p => p.X) + Mesh.Positions.Max(p => p.X)) / 2, precision: 2);
    }

    [Fact]
    public void Asset_RegionWeightsNeverOverdriveAVertex()
    {
        // Weights blend regions, so their total must not exceed 1: a vertex counted twice is displaced
        // twice, which tears the mesh at exactly the seams the feathering exists to hide. The tolerance
        // covers byte quantisation only — each region's weight is rounded to 1/255 independently, so a
        // total that already summed to 1 can round up by half a step per region.
        var tolerance = 1 + (Mesh.Regions.Length * 0.5 / 255);

        for (var i = 0; i < Mesh.Positions.Length; i++)
        {
            Assert.InRange(Mesh.Regions.Sum(r => r.WeightAt(i)), 0.0, tolerance);
        }
    }

    [Fact]
    public void Asset_BaseGirthsMatchARealAdultBody()
    {
        // The whole deformation is a ratio against these numbers, so if the base mesh's own
        // measurements were wrong, every figure would be silently distorted even with correct data
        // entered. Ranges are for an average slim adult male at 70 inches.
        Assert.InRange(GirthAtHeight(BodyRegionKind.Torso, 0.530) * 70, 35, 41);    // hips
        Assert.InRange(GirthAtHeight(BodyRegionKind.Torso, 0.615) * 70, 28, 34);    // waist
        Assert.InRange(GirthAtHeight(BodyRegionKind.Torso, 0.720) * 70, 35, 41);    // chest
        Assert.InRange(GirthAtHeight(BodyRegionKind.LegRight, 0.410) * 70, 18, 24); // thigh
        Assert.InRange(GirthAtHeight(BodyRegionKind.LegRight, 0.225) * 70, 12, 17); // calf
    }

    [Fact]
    public void Asset_IsBuiltFromTheMaleMorphNotTheNeutralBaseMesh()
    {
        // MakeHuman's raw base mesh is androgynous and reads as female. Its chest measures narrower
        // than its hips; applying the male targets brings the chest up to at least match them. This is
        // the cheapest signal that distinguishes the two, so it catches an asset rebuilt without the
        // morph — which would otherwise look wrong and pass every other test here.
        var chest = GirthAtHeight(BodyRegionKind.Torso, 0.720);
        var hips = GirthAtHeight(BodyRegionKind.Torso, 0.530);

        Assert.True(chest / hips > 0.95,
            $"chest/hips is {chest / hips:F2}; the neutral base mesh sits near 0.83, the male morph near 1.00");
    }

    [Fact]
    public void Build_ProducesFiniteGeometryWithUnitNormals()
    {
        var mesh = BodyMeshDeformer.Build(FigureMeasurements.FromCheckIn(null, 70m));

        Assert.Equal(mesh.Positions.Count, mesh.Normals.Count);
        Assert.Equal(Mesh.Indices.Length, mesh.TriangleIndices.Count);

        Assert.All(mesh.Positions, p =>
            Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z), "position must be finite"));
        Assert.All(mesh.Normals, n => Assert.Equal(1.0, n.Length, precision: 6));
    }

    [Theory]
    [InlineData(62)]
    [InlineData(70)]
    [InlineData(79)]
    public void Build_ScalesToTheRecordedHeightExactly(double heightInches)
    {
        var mesh = BodyMeshDeformer.Build(FigureMeasurements.FromCheckIn(null, (decimal)heightInches));

        Assert.Equal(0.0, mesh.Positions.Min(p => p.Y), precision: 3);
        Assert.Equal(heightInches, mesh.Positions.Max(p => p.Y), precision: 3);
    }

    [Fact]
    public void Build_LargerMeasurementsProduceAWiderBodyAtThatLandmarkOnly()
    {
        var slim = BodyMeshDeformer.Build(FigureMeasurements.FromCheckIn(
            new BodyCheckIn { WaistIn = 28m }, 70m));
        var wide = BodyMeshDeformer.Build(FigureMeasurements.FromCheckIn(
            new BodyCheckIn { WaistIn = 44m }, 70m));

        // The waist responds...
        Assert.True(SpanAtHeight(wide, 0.615, 70) > SpanAtHeight(slim, 0.615, 70) + 2,
            "a 16 inch larger waist must be plainly visible");

        // ...and the calves, which no recorded value changed, do not.
        Assert.Equal(
            SpanAtHeight(slim, 0.225, 70, BodyRegionKind.LegRight),
            SpanAtHeight(wide, 0.225, 70, BodyRegionKind.LegRight),
            precision: 3);
    }

    [Fact]
    public void Build_BicepOnlyLeavesTheLegsAlone()
    {
        var thin = BodyMeshDeformer.Build(FigureMeasurements.FromCheckIn(
            new BodyCheckIn { BicepLeftIn = 11m, BicepRightIn = 11m }, 70m));
        var big = BodyMeshDeformer.Build(FigureMeasurements.FromCheckIn(
            new BodyCheckIn { BicepLeftIn = 18m, BicepRightIn = 18m }, 70m));

        Assert.True(big.Positions.Max(p => Math.Abs(p.X)) > thin.Positions.Max(p => Math.Abs(p.X)),
            "bigger arms should reach further out");
        Assert.Equal(
            SpanAtHeight(thin, 0.410, 70, BodyRegionKind.LegRight),
            SpanAtHeight(big, 0.410, 70, BodyRegionKind.LegRight),
            precision: 3);
    }

    /// <summary>
    /// The shoulder is above the top of the torso's axis, so its position along that axis clamps to the
    /// end. Measuring girth from the clamped end point gives a radius pointing diagonally upward, and
    /// scaling it lifted the shoulders into visible spikes. Girth must be measured from the axis line.
    /// </summary>
    [Fact]
    public void Build_ALargeChestWidensTheTorsoWithoutRaisingIt()
    {
        var average = BodyMeshDeformer.Build(FigureMeasurements.FromCheckIn(null, 70m));
        var broad = BodyMeshDeformer.Build(FigureMeasurements.FromCheckIn(
            new BodyCheckIn { ChestIn = 52m }, 70m));

        // Measured at the chest rather than at the shoulder: by shoulder height the deltoids are the
        // widest thing in the slice, and they answer to the bicep measurement, not the chest.
        Assert.True(SpanAtHeight(broad, 0.720, 70) > SpanAtHeight(average, 0.720, 70) + 2,
            "a 52 inch chest should be plainly wider than the average figure");

        // Girth is horizontal, so changing a torso measurement must not move a single vertex
        // vertically. Asserting it across the whole mesh is stronger than probing the shoulder, and it
        // is exactly the invariant the spikes violated.
        for (var i = 0; i < average.Positions.Count; i++)
        {
            Assert.Equal(average.Positions[i].Y, broad.Positions[i].Y, precision: 6);
        }
    }

    [Fact]
    public void Build_AbsurdMeasurementsAreClampedRatherThanTurningTheMeshInsideOut()
    {
        // A fat-fingered entry — 400 instead of 40 — must not be able to destroy the figure.
        var mesh = BodyMeshDeformer.Build(FigureMeasurements.FromCheckIn(
            new BodyCheckIn { WaistIn = 400m, ChestIn = 0.1m }, 70m));

        Assert.All(mesh.Positions, p =>
            Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z)));
        Assert.All(mesh.Normals, n => Assert.Equal(1.0, n.Length, precision: 6));
        Assert.True(mesh.Positions.Max(p => Math.Abs(p.X)) < 70, "the figure must stay within human bounds");
    }

    [Theory]
    [InlineData(0.530, 0.00, GoalMetric.Hips)]
    [InlineData(0.615, 0.00, GoalMetric.Waist)]
    [InlineData(0.720, 0.00, GoalMetric.Chest)]
    [InlineData(0.857, 0.00, GoalMetric.Neck)]
    [InlineData(0.410, 0.09, GoalMetric.ThighRight)]
    [InlineData(0.410, -0.09, GoalMetric.ThighLeft)]
    [InlineData(0.225, 0.12, GoalMetric.CalfRight)]
    [InlineData(0.225, -0.12, GoalMetric.CalfLeft)]
    [InlineData(0.720, 0.17, GoalMetric.BicepRight)]
    [InlineData(0.720, -0.17, GoalMetric.BicepLeft)]
    public void ResolveMetric_RoutesAClickToTheMeasurementUnderIt(double y, double x, GoalMetric expected)
    {
        // Probing a real surface vertex, so a wrong answer is the resolver's fault rather than an
        // artefact of picking a point that is not on the body.
        Assert.Equal(expected, BodyMeshDeformer.ResolveMetric(NearestVertex(y, x, 70), 70));
    }

    [Fact]
    public void ResolveMetric_IgnoresTheHead()
    {
        // Nothing on the head is measured, so a click there must open no editor rather than guessing.
        Assert.Null(BodyMeshDeformer.ResolveMetric(NearestVertex(0.95, 0, 70), 70));
    }

    [Fact]
    public void ResolveMetric_ScalesWithHeightRatherThanAssumingSeventyInches()
    {
        // The same landmark on a shorter figure must resolve the same way.
        Assert.Equal(GoalMetric.Waist, BodyMeshDeformer.ResolveMetric(NearestVertex(0.615, 0, 60), 60));
        Assert.Equal(GoalMetric.Waist, BodyMeshDeformer.ResolveMetric(NearestVertex(0.615, 0, 80), 80));
    }

    [Fact]
    public void Figure_UsesRecordedMeasurementsAndFallsBackPerValue()
    {
        // Only a waist recorded: the waist must be honoured and everything else defaulted, rather than
        // the whole figure reverting to averages.
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

    /// <summary>
    /// The bug this pins actually happened. A height of 6 was entered meaning six feet and stored as
    /// six inches. Because every measurement is a ratio against the base mesh scaled to height, a
    /// six-inch body made every ratio enormous, all of them pinned at the upper clamp, and the figure
    /// rendered as an inflated blob with fins where the arms met the shoulders.
    /// </summary>
    [Theory]
    [InlineData(6)]      // feet typed into an inches field, the case that occurred
    [InlineData(0)]
    [InlineData(-70)]
    [InlineData(5000)]
    public void Figure_ImplausibleHeightFallsBackToTheAverageInsteadOfInflating(double badHeight)
    {
        var checkIn = new BodyCheckIn
        {
            NeckIn = 12m, ChestIn = 45m, WaistIn = 12m, HipsIn = 12m,
            BicepLeftIn = 12m, BicepRightIn = 12m,
            ThighLeftIn = 12m, ThighRightIn = 12m,
            CalfLeftIn = 12m, CalfRightIn = 12m,
        };

        var m = FigureMeasurements.FromCheckIn(checkIn, (decimal)badHeight);
        Assert.Equal(70, m.HeightInches);

        // With a sane height, those same measurements shrink the figure rather than inflating it. The
        // blob came from every factor pinning at the upper clamp at once.
        var figure = BodyMeshDeformer.Build(m);
        var average = BodyMeshDeformer.Build(FigureMeasurements.FromCheckIn(null, 70m));

        Assert.True(SpanAtHeight(figure, 0.615, 70) < SpanAtHeight(average, 0.615, 70),
            "a 12 inch waist should narrow the figure, never widen it");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(900)]
    public void Figure_ImplausibleCircumferenceIsTreatedAsMissing(decimal bad)
    {
        var m = FigureMeasurements.FromCheckIn(new BodyCheckIn { WaistIn = bad }, 70m);

        Assert.Equal(34, m.Waist);   // the average, same as if it had never been recorded
    }

    [Fact]
    public void Figure_WeightOnlyCheckInCountsAsNoMeasurements()
    {
        // A weigh-in with no tape measurements should still show the dimmed reference figure.
        Assert.False(FigureMeasurements.HasAnyMeasurement(new BodyCheckIn { WeightLb = 185m }));
    }

    [Fact]
    public void Bmi_IsCorrectForAKnownPairAndRefusesAnImpossibleHeight()
    {
        // 240 lb at 6 ft: 703 * 240 / 72^2 = 32.55, rounded to 32.5.
        Assert.Equal(32.5m, BmiCalculator.Calculate(240m, 72m));

        // The same weight against the height that was actually stored produced a BMI near 4,700.
        // Height is squared in the denominator, so a wrong height does not give a slightly wrong BMI.
        Assert.Null(BmiCalculator.Calculate(240m, 6m));
        Assert.Null(BmiCalculator.Calculate(240m, null));
        Assert.Null(BmiCalculator.Calculate(null, 72m));
    }

    [Fact]
    public void Ramp_InterpolatesBetweenKnotsAndHoldsAtBothEnds()
    {
        var ramp = new Ramp([(0.0, 1.0), (0.5, 2.0), (1.0, 2.0)]);

        Assert.Equal(1.0, ramp.At(-1), precision: 6);   // before the first knot
        Assert.Equal(1.5, ramp.At(0.25), precision: 6);
        Assert.Equal(2.0, ramp.At(0.5), precision: 6);
        Assert.Equal(2.0, ramp.At(5), precision: 6);    // past the last knot
    }

    private static double GirthAtHeight(int regionIndex, double heightFraction)
    {
        var region = Mesh.Regions[regionIndex];
        var t = (heightFraction - region.Origin.Y) / (region.Direction.Y * region.Length);
        return region.GirthAt(Math.Clamp(t, 0, 1));
    }

    /// <summary>
    /// Widest left-to-right measurement across a thin horizontal slice of one region of the built
    /// figure.
    ///
    /// The region has to be selected by its own weights rather than by distance from the centre. The
    /// arms hang forward and down across the whole torso, so at waist height the hands are the widest
    /// thing in the slice, and at chest height the inner arm sits closer to the midline than the ribs
    /// do — any distance threshold measures an arm sooner or later.
    /// </summary>
    private static double SpanAtHeight(MeshGeometry3D mesh, double heightFraction, double height,
        int regionIndex = BodyRegionKind.Torso)
    {
        var region = Mesh.Regions[regionIndex];
        var y = heightFraction * height;
        var slice = new List<double>();

        for (var i = 0; i < mesh.Positions.Count; i++)
        {
            if (region.WeightAt(i) > 0.85 && Math.Abs(mesh.Positions[i].Y - y) < height * 0.01)
            {
                slice.Add(mesh.Positions[i].X);
            }
        }

        return slice.Count == 0 ? 0 : slice.Max() - slice.Min();
    }

    private static Point3D NearestVertex(double y, double x, double height)
    {
        var v = Mesh.Positions
            .OrderBy(p => ((p.Y - y) * (p.Y - y)) + ((p.X - x) * (p.X - x)))
            .ThenByDescending(p => p.Z)
            .First();
        return new Point3D(v.X * height, v.Y * height, v.Z * height);
    }
}

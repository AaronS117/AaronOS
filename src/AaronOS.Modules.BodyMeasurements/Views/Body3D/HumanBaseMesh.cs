using System.IO;
using System.Windows.Media.Media3D;

namespace AaronOS.Modules.BodyMeasurements.Views.Body3D;

/// <summary>
/// One deformable region of the base mesh — the torso, the neck, or a single limb.
///
/// <paramref name="Girth"/> is the region's own circumference sampled along its axis, in units of
/// body height, measured off the base mesh when the asset was built. Dividing a recorded measurement
/// by the corresponding entry gives the scale factor that turns the average figure into this one.
/// <paramref name="Weights"/> is per-vertex and feathered, so regions blend into each other instead
/// of creasing at a seam.
/// </summary>
public sealed record BodyRegion(
    string Name,
    Point3D Origin,
    Vector3D Direction,
    double Length,
    double[] Girth,
    byte[] Weights)
{
    /// <summary>How far along the axis a point falls, 0 at the origin end and 1 at the far end.</summary>
    public double Parameter(Point3D p) => Math.Clamp(RawParameter(p), 0, 1);

    /// <summary>
    /// The same position along the axis, but not clamped to the region's extent.
    ///
    /// Girth has to be measured from the axis <em>line</em>, not from its end points. A vertex just
    /// above the torso's top — the trapezius, say — projects past the end, and measuring it from the
    /// clamped end point yields a radius pointing diagonally upward. Scaling that lifts the shoulders
    /// into spikes instead of widening them, which is exactly what it did before this existed.
    /// </summary>
    public double RawParameter(Point3D p) => Vector3D.DotProduct(p - Origin, Direction) / Length;

    /// <summary>The point on the axis that <paramref name="p"/> is measured out from.</summary>
    public Point3D AxisPoint(double t) => Origin + Direction * (t * Length);

    /// <summary>The vector from the axis line out to <paramref name="p"/>, always perpendicular to the
    /// axis, which is the direction girth grows in.</summary>
    public Vector3D Radial(Point3D p) => p - AxisPoint(RawParameter(p));

    /// <summary>Base circumference at <paramref name="t"/>, in units of body height.</summary>
    public double GirthAt(double t) => Girth[Math.Clamp((int)(t * Girth.Length), 0, Girth.Length - 1)];

    public double WeightAt(int vertex) => Weights[vertex] / 255.0;
}

/// <summary>
/// The base human mesh the figure is built from, loaded once and shared.
///
/// The mesh is the MakeHuman community base mesh, released as CC0 (see Assets/README.md). It is
/// stored pre-processed: triangulated, stripped to the body group, normalised to unit height with the
/// feet at Y=0, and annotated with the region axes and blend weights that drive deformation. All of
/// that analysis happens in the build script, so start-up only has to read numbers.
/// </summary>
public sealed class HumanBaseMesh
{
    private const string ResourceName = "AaronOS.BodyMeasurements.HumanBase";
    private const int FormatVersion = 3;

    private static readonly Lazy<HumanBaseMesh> Shared = new(Load);

    public static HumanBaseMesh Instance => Shared.Value;

    public required Point3D[] Positions { get; init; }
    public required int[] Indices { get; init; }
    public required BodyRegion[] Regions { get; init; }

    private static HumanBaseMesh Load()
    {
        using var stream = typeof(HumanBaseMesh).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded mesh '{ResourceName}' is missing. Available: " +
                string.Join(", ", typeof(HumanBaseMesh).Assembly.GetManifestResourceNames()));
        using var reader = new BinaryReader(stream);

        if (new string(reader.ReadChars(4)) is not "AOSM")
        {
            throw new InvalidDataException("Not an AaronOS mesh asset.");
        }

        var version = reader.ReadInt32();
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Mesh asset is version {version}; this build reads {FormatVersion}.");
        }

        var vertexCount = reader.ReadInt32();
        var triangleCount = reader.ReadInt32();
        var regionCount = reader.ReadInt32();
        var bandCount = reader.ReadInt32();

        var positions = new Point3D[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            positions[i] = new Point3D(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        // Stored as ushort — the mesh is deliberately kept under 65,536 vertices, which halves the
        // asset's index data and is checked when it is built.
        var indices = new int[triangleCount * 3];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = reader.ReadUInt16();
        }

        var regions = new BodyRegion[regionCount];
        for (var r = 0; r < regionCount; r++)
        {
            var origin = new Point3D(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var direction = new Vector3D(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var length = reader.ReadSingle();

            var girth = new double[bandCount];
            for (var b = 0; b < bandCount; b++)
            {
                girth[b] = reader.ReadSingle();
            }

            regions[r] = new BodyRegion(
                BodyRegionKind.Names[r], origin, direction, length, girth, reader.ReadBytes(vertexCount));
        }

        return new HumanBaseMesh { Positions = positions, Indices = indices, Regions = regions };
    }
}

/// <summary>
/// Region order in the asset. It is a fixed contract with the build script rather than a lookup by
/// name, so the two must be changed together.
/// </summary>
public static class BodyRegionKind
{
    public const int Torso = 0, Neck = 1, ArmRight = 2, ArmLeft = 3, LegRight = 4, LegLeft = 5;

    public static readonly string[] Names =
        ["Torso", "Neck", "ArmRight", "ArmLeft", "LegRight", "LegLeft"];
}

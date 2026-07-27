using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AaronOS.Modules.BodyMeasurements.Views.Body3D;

/// <summary>
/// One horizontal slice through a body part.
///
/// Not a plain ellipse: a torso is closer to a rounded rectangle than an oval, and it is deeper in
/// front (belly, chest) than behind, so <paramref name="Squareness"/> and the separate front/back
/// depths are what stop the figure looking like welded pipe.
/// </summary>
/// <param name="Y">Height of the slice along the part's own axis.</param>
/// <param name="HalfWidth">Half the side-to-side width.</param>
/// <param name="DepthFront">Half-depth toward +Z (front of the body).</param>
/// <param name="DepthBack">Half-depth toward -Z (back).</param>
/// <param name="Squareness">2 is an ellipse; higher fills the corners toward a rounded rectangle.</param>
public readonly record struct Section(double Y, double HalfWidth, double DepthFront, double DepthBack, double Squareness = 2.0)
{
    public Section(double y, double halfWidth, double halfDepth)
        : this(y, halfWidth, halfDepth, halfDepth) { }
}

/// <summary>Backwards-compatible symmetric elliptical slice.</summary>
public readonly record struct Ring(double Y, double HalfWidth, double HalfDepth);

/// <summary>
/// Builds triangle meshes for the body model. Pure geometry — no controls, no data access — so the
/// maths is unit testable without standing up a window, which matters because geometry fails
/// silently: a bad normal or reversed winding yields a dark or invisible model, never an exception.
/// </summary>
public static class HumanMeshBuilder
{
    /// <summary>
    /// Converts a tape measurement to the half-width and half-depth of an ellipse.
    ///
    /// A measurement is a circumference, not a width. For an ellipse with semi-axes a and b,
    /// perimeter ≈ π(a + b), so with a fixed depth-to-width ratio k = b/a this inverts to
    /// a = C / (π(1 + k)). Treating the circumference as a width directly — the obvious mistake —
    /// draws a figure roughly three times too wide.
    /// </summary>
    public static (double HalfWidth, double HalfDepth) FromCircumference(double circumference, double depthRatio)
    {
        var halfWidth = circumference / (Math.PI * (1 + depthRatio));
        return (halfWidth, halfWidth * depthRatio);
    }

    /// <summary>Symmetric elliptical loft, kept as the simple case over <see cref="BuildLoft"/>.</summary>
    public static MeshGeometry3D BuildTube(IReadOnlyList<Ring> rings, int segments = 28, bool capEnds = true) =>
        BuildLoft(
            rings.Select(r => new Section(r.Y, r.HalfWidth, r.HalfDepth)).ToList(),
            segments,
            subdivisions: 1,
            capEnds);

    /// <summary>
    /// Lofts a surface through <paramref name="sections"/>, inserting <paramref name="subdivisions"/>
    /// spline-interpolated slices between each pair so the silhouette curves instead of showing a
    /// crease at every measured landmark. Normals are taken from the finished vertex grid, which
    /// keeps them correct for any cross-section shape rather than only for ellipses.
    /// </summary>
    public static MeshGeometry3D BuildLoft(
        IReadOnlyList<Section> sections,
        int segments = 36,
        int subdivisions = 6,
        bool capEnds = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sections.Count, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(subdivisions, 1);

        var dense = Resample(sections, subdivisions);

        // Vertex grid first, normals second: differencing neighbouring rows and columns of the
        // finished grid gives smooth normals for any profile, with no per-shape calculus.
        var grid = new Point3D[dense.Count][];
        for (var i = 0; i < dense.Count; i++)
        {
            grid[i] = new Point3D[segments];
            for (var j = 0; j < segments; j++)
            {
                grid[i][j] = SurfacePoint(dense[i], 2 * Math.PI * j / segments);
            }
        }

        var positions = new Point3DCollection(dense.Count * segments);
        var normals = new Vector3DCollection(dense.Count * segments);
        for (var i = 0; i < dense.Count; i++)
        {
            for (var j = 0; j < segments; j++)
            {
                positions.Add(grid[i][j]);
                normals.Add(GridNormal(grid, i, j, segments));
            }
        }

        var indices = new Int32Collection();

        // Winding depends on which way the slices run along the axis: a stack ordered bottom-to-top
        // produces the opposite face orientation to one ordered top-to-bottom. Limbs are built
        // downward from a joint while the torso is built upward, so without this the two disagree and
        // one renders its interior — which shows up as a part that is flat and unlit rather than as
        // anything obviously broken.
        var ascending = dense[^1].Y >= dense[0].Y;

        for (var i = 0; i < dense.Count - 1; i++)
        {
            for (var j = 0; j < segments; j++)
            {
                var next = (j + 1) % segments;
                var lowerHere = i * segments + j;
                var lowerNext = i * segments + next;
                var upperNext = (i + 1) * segments + next;
                var upperHere = (i + 1) * segments + j;

                if (ascending)
                {
                    indices.Add(lowerHere); indices.Add(upperNext); indices.Add(lowerNext);
                    indices.Add(lowerHere); indices.Add(upperHere); indices.Add(upperNext);
                }
                else
                {
                    indices.Add(lowerHere); indices.Add(lowerNext); indices.Add(upperNext);
                    indices.Add(lowerHere); indices.Add(upperNext); indices.Add(upperHere);
                }
            }
        }

        if (capEnds)
        {
            AddCap(positions, normals, indices, dense[0], 0, segments, ascending ? new Vector3D(0, -1, 0) : new Vector3D(0, 1, 0), ascending);
            AddCap(positions, normals, indices, dense[^1], (dense.Count - 1) * segments, segments, ascending ? new Vector3D(0, 1, 0) : new Vector3D(0, -1, 0), !ascending);
        }

        return new MeshGeometry3D { Positions = positions, Normals = normals, TriangleIndices = indices };
    }

    /// <summary>
    /// A superellipse, with the front and back half-depths blended so the profile stays continuous
    /// through the sides rather than stepping between two different depths.
    /// </summary>
    private static Point3D SurfacePoint(Section s, double theta)
    {
        var cos = Math.Cos(theta);
        var sin = Math.Sin(theta);
        var exponent = 2.0 / Math.Max(1.2, s.Squareness);

        var unitX = Math.Sign(cos) * Math.Pow(Math.Abs(cos), exponent);
        var unitZ = Math.Sign(sin) * Math.Pow(Math.Abs(sin), exponent);

        var midDepth = (s.DepthFront + s.DepthBack) / 2;
        var depthBias = (s.DepthFront - s.DepthBack) / 2;

        return new Point3D(
            s.HalfWidth * unitX,
            s.Y,
            midDepth * unitZ + depthBias * Math.Abs(unitZ));
    }

    private static Vector3D GridNormal(Point3D[][] grid, int i, int j, int segments)
    {
        var previousColumn = grid[i][(j - 1 + segments) % segments];
        var nextColumn = grid[i][(j + 1) % segments];
        var previousRow = grid[Math.Max(0, i - 1)][j];
        var nextRow = grid[Math.Min(grid.Length - 1, i + 1)][j];

        var normal = Vector3D.CrossProduct(nextRow - previousRow, nextColumn - previousColumn);

        // Degenerate slice (a pinched tip, or two identical rows): fall back to the radial direction.
        if (normal.Length < 1e-9)
        {
            var point = grid[i][j];
            normal = new Vector3D(point.X, 0, point.Z);
            if (normal.Length < 1e-9)
            {
                return new Vector3D(0, 1, 0);
            }
        }

        normal.Normalize();

        var radial = new Vector3D(grid[i][j].X, 0, grid[i][j].Z);
        if (radial.Length > 1e-9 && Vector3D.DotProduct(normal, radial) < 0)
        {
            normal.Negate();
        }

        return normal;
    }

    /// <summary>
    /// Catmull-Rom through the supplied slices. Interpolating each property independently keeps the
    /// spline honest about the measured landmarks while smoothing everything between them.
    /// </summary>
    private static List<Section> Resample(IReadOnlyList<Section> sections, int subdivisions)
    {
        if (subdivisions == 1)
        {
            return [.. sections];
        }

        var dense = new List<Section>((sections.Count - 1) * subdivisions + 1);

        for (var i = 0; i < sections.Count - 1; i++)
        {
            var p0 = sections[Math.Max(0, i - 1)];
            var p1 = sections[i];
            var p2 = sections[i + 1];
            var p3 = sections[Math.Min(sections.Count - 1, i + 2)];

            for (var step = 0; step < subdivisions; step++)
            {
                var t = (double)step / subdivisions;
                dense.Add(new Section(
                    CatmullRom(p0.Y, p1.Y, p2.Y, p3.Y, t),
                    Math.Max(0, CatmullRom(p0.HalfWidth, p1.HalfWidth, p2.HalfWidth, p3.HalfWidth, t)),
                    Math.Max(0, CatmullRom(p0.DepthFront, p1.DepthFront, p2.DepthFront, p3.DepthFront, t)),
                    Math.Max(0, CatmullRom(p0.DepthBack, p1.DepthBack, p2.DepthBack, p3.DepthBack, t)),
                    CatmullRom(p0.Squareness, p1.Squareness, p2.Squareness, p3.Squareness, t)));
            }
        }

        dense.Add(sections[^1]);
        return dense;
    }

    private static double CatmullRom(double p0, double p1, double p2, double p3, double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5 * ((2 * p1)
            + (-p0 + p2) * t
            + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
            + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
    }

    private static void AddCap(
        Point3DCollection positions,
        Vector3DCollection normals,
        Int32Collection indices,
        Section section,
        int ringStart,
        int segments,
        Vector3D facing,
        bool reverse)
    {
        var centre = positions.Count;
        positions.Add(new Point3D(0, section.Y, 0));
        normals.Add(facing);

        for (var j = 0; j < segments; j++)
        {
            var next = (j + 1) % segments;
            indices.Add(centre);
            if (reverse)
            {
                indices.Add(ringStart + next);
                indices.Add(ringStart + j);
            }
            else
            {
                indices.Add(ringStart + j);
                indices.Add(ringStart + next);
            }
        }
    }

    /// <summary>An ellipsoid, kept for anything genuinely round.</summary>
    public static MeshGeometry3D BuildEllipsoid(double radiusX, double radiusY, double radiusZ, int segments = 28, int stacks = 18)
    {
        var positions = new Point3DCollection();
        var normals = new Vector3DCollection();
        var indices = new Int32Collection();

        for (var i = 0; i <= stacks; i++)
        {
            var phi = Math.PI * i / stacks;
            var y = Math.Cos(phi);
            var ringRadius = Math.Sin(phi);

            for (var j = 0; j <= segments; j++)
            {
                var theta = 2 * Math.PI * j / segments;
                var x = ringRadius * Math.Cos(theta);
                var z = ringRadius * Math.Sin(theta);

                positions.Add(new Point3D(x * radiusX, y * radiusY, z * radiusZ));

                // Normal of an ellipsoid is the gradient of its implicit form, not the position.
                var normal = new Vector3D(x / radiusX, y / radiusY, z / radiusZ);
                normal.Normalize();
                normals.Add(normal);
            }
        }

        var perRow = segments + 1;
        for (var i = 0; i < stacks; i++)
        {
            for (var j = 0; j < segments; j++)
            {
                var a = i * perRow + j;
                var b = a + 1;
                var c = (i + 1) * perRow + j + 1;
                var d = (i + 1) * perRow + j;

                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(a); indices.Add(c); indices.Add(d);
            }
        }

        return new MeshGeometry3D { Positions = positions, Normals = normals, TriangleIndices = indices };
    }
}

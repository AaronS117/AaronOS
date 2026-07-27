using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AaronOS.Modules.BodyMeasurements.Views.Body3D;

/// <summary>One horizontal slice through a body part: an ellipse at height <paramref name="Y"/>.</summary>
public readonly record struct Ring(double Y, double HalfWidth, double HalfDepth);

/// <summary>
/// Builds triangle meshes for the body model. Pure geometry — no controls, no data access — so the
/// maths can be unit tested without standing up a window.
///
/// Everything is a lofted elliptical tube: a stack of rings joined side to side. Bodies are not
/// circular in cross-section (a waist is far wider than it is deep), so ellipses rather than circles
/// are what make the result read as a torso instead of a pipe.
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

    /// <summary>
    /// Lofts a closed surface through <paramref name="rings"/>. Normals are computed analytically
    /// from the surface derivatives, including the taper between rings, so shading stays smooth
    /// where a limb narrows instead of showing faceted bands.
    /// </summary>
    public static MeshGeometry3D BuildTube(IReadOnlyList<Ring> rings, int segments = 28, bool capEnds = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rings.Count, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);

        var positions = new Point3DCollection();
        var normals = new Vector3DCollection();
        var indices = new Int32Collection();

        for (var i = 0; i < rings.Count; i++)
        {
            var ring = rings[i];
            var previous = rings[Math.Max(0, i - 1)];
            var next = rings[Math.Min(rings.Count - 1, i + 1)];

            var span = next.Y - previous.Y;
            var widthSlope = Math.Abs(span) < 1e-9 ? 0 : (next.HalfWidth - previous.HalfWidth) / span;
            var depthSlope = Math.Abs(span) < 1e-9 ? 0 : (next.HalfDepth - previous.HalfDepth) / span;

            for (var j = 0; j < segments; j++)
            {
                var angle = 2 * Math.PI * j / segments;
                var cos = Math.Cos(angle);
                var sin = Math.Sin(angle);

                positions.Add(new Point3D(ring.HalfWidth * cos, ring.Y, ring.HalfDepth * sin));
                normals.Add(SurfaceNormal(ring, widthSlope, depthSlope, cos, sin));
            }
        }

        for (var i = 0; i < rings.Count - 1; i++)
        {
            for (var j = 0; j < segments; j++)
            {
                var next = (j + 1) % segments;
                var a = i * segments + j;
                var b = i * segments + next;
                var c = (i + 1) * segments + next;
                var d = (i + 1) * segments + j;

                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(a); indices.Add(c); indices.Add(d);
            }
        }

        if (capEnds)
        {
            AddCap(positions, normals, indices, rings[0], 0, segments, new Vector3D(0, -1, 0));
            AddCap(positions, normals, indices, rings[^1], (rings.Count - 1) * segments, segments, new Vector3D(0, 1, 0));
        }

        return new MeshGeometry3D { Positions = positions, Normals = normals, TriangleIndices = indices };
    }

    /// <summary>Outward normal from the cross product of the surface's two tangents.</summary>
    private static Vector3D SurfaceNormal(Ring ring, double widthSlope, double depthSlope, double cos, double sin)
    {
        var alongRing = new Vector3D(-ring.HalfWidth * sin, 0, ring.HalfDepth * cos);
        var alongLength = new Vector3D(widthSlope * cos, 1, depthSlope * sin);
        var normal = Vector3D.CrossProduct(alongLength, alongRing);

        // Degenerate ring (zero radius, e.g. a pinched tip): fall back to the radial direction.
        if (normal.Length < 1e-9)
        {
            normal = new Vector3D(cos, 0, sin);
        }

        normal.Normalize();

        // Force outward, so lighting never renders a part inside-out.
        if (normal.X * cos + normal.Z * sin < 0)
        {
            normal.Negate();
        }

        return normal;
    }

    private static void AddCap(
        Point3DCollection positions,
        Vector3DCollection normals,
        Int32Collection indices,
        Ring ring,
        int ringStart,
        int segments,
        Vector3D facing)
    {
        var centre = positions.Count;
        positions.Add(new Point3D(0, ring.Y, 0));
        normals.Add(facing);

        for (var j = 0; j < segments; j++)
        {
            var next = (j + 1) % segments;
            indices.Add(centre);
            indices.Add(ringStart + j);
            indices.Add(ringStart + next);
        }
    }

    /// <summary>An ellipsoid, used for the head, hands and feet.</summary>
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

using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>
/// Material tags stored in the vertex color ALPHA channel of unit meshes; the unit shader decodes them:
/// Metal = plain albedo (rgb), Team = army color modulated by rgb brightness, Glow = emissive rgb.
/// </summary>
public static class Mat
{
    public const byte Metal = 255;
    public const byte Team = 0;
    public const byte Glow = 128;

    public static Color M(byte r, byte g, byte b) => new(r, g, b, Metal);
    public static Color T(byte k) => new(k, k, k, Team);
    public static Color G(byte r, byte g, byte b) => new(r, g, b, Glow);
}

/// <summary>A cross-section of a loft: profile point p maps to (Off.X + p.X*Scale.X, Off.Y + p.Y*Scale.Y, Z).</summary>
public readonly record struct Station(float Z, float Sx, float Sy, float Ox = 0, float Oy = 0)
{
    public Station(float z, float s) : this(z, s, s) { }
}

public enum Axis { X, Y, Z }

/// <summary>
/// Builds low-poly, flat-shaded meshes from convex primitives (lofts of a 2D profile through stations).
/// Face winding is fixed automatically using the part centroid, so mirrored/rotated parts are always outward.
/// Coordinates: model forward +Z, up +Y.
/// </summary>
public sealed class MeshBuilder
{
    private readonly List<Vector3> _pos = new();
    private readonly List<Vector3> _nrm = new();
    private readonly List<Color> _col = new();
    private readonly List<ushort> _idx = new();

    /// <summary>Extra transform applied to every part (row-vector, applied after the part transform).</summary>
    public Matrix4x4 Transform = Matrix4x4.Identity;

    public int VertexCount => _pos.Count;

    // ---------------------------------------------------------------- core

    private void Face(ReadOnlySpan<Vector3> pts, Color c, Vector3 centroid)
    {
        if (pts.Length < 3) return;
        var center = Vector3.Zero;
        foreach (var p in pts) center += p;
        center /= pts.Length;
        // Newell normal (robust for slightly non-planar quads).
        var n = Vector3.Zero;
        for (int i = 0; i < pts.Length; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Length];
            n.X += (a.Y - b.Y) * (a.Z + b.Z);
            n.Y += (a.Z - b.Z) * (a.X + b.X);
            n.Z += (a.X - b.X) * (a.Y + b.Y);
        }
        float len = n.Length();
        if (len < 1e-7f) return;
        n /= len;
        bool flip = Vector3.Dot(n, center - centroid) < 0;
        if (flip) n = -n;
        if (_pos.Count + pts.Length > 65535) throw new InvalidOperationException("mesh too large for 16-bit indices");
        int baseIdx = _pos.Count;
        foreach (var p in pts)
        {
            _pos.Add(p);
            _nrm.Add(n);
            _col.Add(c);
        }
        for (int i = 1; i + 1 < pts.Length; i++)
        {
            if (!flip)
            {
                _idx.Add((ushort)baseIdx); _idx.Add((ushort)(baseIdx + i)); _idx.Add((ushort)(baseIdx + i + 1));
            }
            else
            {
                _idx.Add((ushort)baseIdx); _idx.Add((ushort)(baseIdx + i + 1)); _idx.Add((ushort)(baseIdx + i));
            }
        }
    }

    private static Vector3 MapAxis(Axis axis, float u, float v, float w) => axis switch
    {
        Axis.Z => new Vector3(u, v, w),
        Axis.X => new Vector3(w, v, u),
        _ => new Vector3(u, w, v),
    };

    /// <summary>
    /// Lofts a convex 2D profile through stations along an axis. For Axis.Z the profile is (x, y);
    /// Axis.Y: (x, z); Axis.X: (z, y). Ends are capped (capColor defaults to the side color).
    /// </summary>
    public void Loft(ReadOnlySpan<Vector2> profile, ReadOnlySpan<Station> stations, Axis axis, Color side, Color? cap = null, Matrix4x4? xf = null, bool capStart = true, bool capEnd = true)
    {
        var m = (xf ?? Matrix4x4.Identity) * Transform;
        int n = profile.Length;
        var rings = new Vector3[stations.Length][];
        var centroid = Vector3.Zero;
        int cnt = 0;
        for (int s = 0; s < stations.Length; s++)
        {
            var st = stations[s];
            rings[s] = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var p = MapAxis(axis, st.Ox + profile[i].X * st.Sx, st.Oy + profile[i].Y * st.Sy, st.Z);
                rings[s][i] = Vector3.Transform(p, m);
                centroid += rings[s][i];
                cnt++;
            }
        }
        centroid /= cnt;
        Span<Vector3> quad = stackalloc Vector3[4];
        for (int s = 0; s + 1 < stations.Length; s++)
        {
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var a = rings[s][i]; var b = rings[s][j]; var c = rings[s + 1][j]; var d = rings[s + 1][i];
                // Collapse degenerate edges (tapered to a point) into triangles.
                if (Vector3.DistanceSquared(a, b) < 1e-10f) { quad[0] = a; quad[1] = c; quad[2] = d; Face(quad[..3], side, centroid); }
                else if (Vector3.DistanceSquared(c, d) < 1e-10f) { quad[0] = a; quad[1] = b; quad[2] = c; Face(quad[..3], side, centroid); }
                else { quad[0] = a; quad[1] = b; quad[2] = c; quad[3] = d; Face(quad, side, centroid); }
            }
        }
        var cc = cap ?? side;
        if (capStart) Face(rings[0], cc, centroid);
        if (capEnd) Face(rings[^1], cc, centroid);
    }

    // ---------------------------------------------------------------- primitives

    public static Vector2[] Rect(float w, float h) => [new(-w / 2, -h / 2), new(w / 2, -h / 2), new(w / 2, h / 2), new(-w / 2, h / 2)];

    /// <summary>Rectangle with chamfered corners (octagon), chamfer in absolute units.</summary>
    public static Vector2[] Chamfered(float w, float h, float c)
    {
        float x = w / 2, y = h / 2;
        c = MathF.Min(c, MathF.Min(x, y) * 0.95f);
        return [new(-x + c, -y), new(x - c, -y), new(x, -y + c), new(x, y - c), new(x - c, y), new(-x + c, y), new(-x, y - c), new(-x, -y + c)];
    }

    public static Vector2[] NGon(int sides, float r, float phase = 0)
    {
        var p = new Vector2[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = phase + i * MathF.Tau / sides;
            p[i] = new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
        }
        return p;
    }

    /// <summary>Axis-aligned box: center, size (x, y, z).</summary>
    public void Box(Vector3 c, Vector3 size, Color col, Color? top = null, Matrix4x4? xf = null)
    {
        Loft(Rect(size.X, size.Y), [new Station(c.Z - size.Z / 2, 1, 1, c.X, c.Y), new Station(c.Z + size.Z / 2, 1, 1, c.X, c.Y)], Axis.Z, col, null, xf);
        if (top is Color t)
        {
            // Thin colored plate on top.
            var plate = new Vector3(size.X * 0.999f, 0.002f, size.Z * 0.999f);
            Loft(Rect(plate.X, plate.Z), [new Station(c.Y + size.Y / 2, 1, 1, c.X, c.Z), new Station(c.Y + size.Y / 2 + 0.004f, 1, 1, c.X, c.Z)], Axis.Y, t, null, xf);
        }
    }

    /// <summary>Box from min corner to max corner.</summary>
    public void BoxMinMax(Vector3 min, Vector3 max, Color col, Matrix4x4? xf = null) => Box((min + max) * 0.5f, max - min, col, null, xf);

    /// <summary>Box with chamfered long edges (along Z).</summary>
    public void BevelBox(Vector3 c, Vector3 size, float bevel, Color col, Color? cap = null, Matrix4x4? xf = null)
    {
        Loft(Chamfered(size.X, size.Y, bevel), [new Station(c.Z - size.Z / 2, 1, 1, c.X, c.Y), new Station(c.Z + size.Z / 2, 1, 1, c.X, c.Y)], Axis.Z, col, cap, xf);
    }

    /// <summary>Vertical tapered block: bottom footprint (bx, bz) at y0, top footprint (tx, tz) at y1.</summary>
    public void Frustum(Vector3 bottomCenter, float bx, float bz, float tx, float tz, float height, Color col, Color? top = null, Matrix4x4? xf = null, float chamfer = 0)
    {
        var prof = chamfer > 0 ? Chamfered(1, 1, chamfer) : Rect(1, 1);
        Loft(prof, [new Station(bottomCenter.Y, bx, bz, bottomCenter.X, bottomCenter.Z), new Station(bottomCenter.Y + height, tx, tz, bottomCenter.X, bottomCenter.Z)], Axis.Y, col, top, xf);
    }

    /// <summary>Cylinder/cone along an axis, from 'from' center with radii r0..r1 over 'length'.</summary>
    public void Cylinder(Vector3 from, Axis axis, float length, float r0, float r1, int sides, Color col, Color? cap = null, Matrix4x4? xf = null, float phase = -1)
    {
        if (phase < 0) phase = MathF.PI / sides;
        var prof = NGon(sides, 1, phase);
        (float u, float v, float w) = axis switch
        {
            Axis.Z => (from.X, from.Y, from.Z),
            Axis.X => (from.Z, from.Y, from.X),
            _ => (from.X, from.Z, from.Y),
        };
        Loft(prof, [new Station(w, r0, r0, u, v), new Station(w + length, r1, r1, u, v)], axis, col, cap, xf);
    }

    /// <summary>Low-poly sphere (or ellipsoid with scale).</summary>
    public void Sphere(Vector3 c, Vector3 radii, int rings, int sides, Color col, Matrix4x4? xf = null, float minLat = 0f)
    {
        var prof = NGon(sides, 1, MathF.PI / sides);
        var st = new List<Station>();
        for (int k = 0; k <= rings; k++)
        {
            float t = k / (float)rings;
            if (t < minLat) continue;
            float a = MathF.PI * t;
            float y = -MathF.Cos(a);
            float s = MathF.Sin(a);
            st.Add(new Station(c.Y + y * radii.Y, s * radii.X, s * radii.Z, c.X, c.Z));
        }
        Loft(prof, st.ToArray(), Axis.Y, col, null, xf);
    }

    /// <summary>Flat convex polygon in the XZ plane extruded vertically from y0 to y1 (e.g. wings).</summary>
    public void Slab(ReadOnlySpan<Vector2> xz, float y0, float y1, Color col, Color? cap = null, Matrix4x4? xf = null)
    {
        Loft(xz, [new Station(y0, 1, 1), new Station(y1, 1, 1)], Axis.Y, col, cap, xf);
    }

    /// <summary>Flat convex polygon in the ZY plane extruded along X (e.g. tail fins, track sides).</summary>
    public void SideSlab(ReadOnlySpan<Vector2> zy, float x0, float x1, Color col, Color? cap = null, Matrix4x4? xf = null)
    {
        Loft(zy, [new Station(x0, 1, 1), new Station(x1, 1, 1)], Axis.X, col, cap, xf);
    }

    // ---------------------------------------------------------------- output

    public (float minY, float maxY, float radius) Bounds()
    {
        float minY = float.MaxValue, maxY = float.MinValue, r2 = 0;
        foreach (var p in _pos)
        {
            minY = MathF.Min(minY, p.Y);
            maxY = MathF.Max(maxY, p.Y);
            r2 = MathF.Max(r2, p.LengthSquared());
        }
        return _pos.Count == 0 ? (0, 0, 0) : (minY, maxY, MathF.Sqrt(r2));
    }

    public unsafe Mesh Upload()
    {
        if (_pos.Count == 0) Box(new Vector3(0, 0.5f, 0), Vector3.One, Mat.M(128, 128, 128));
        var mesh = new Mesh { VertexCount = _pos.Count, TriangleCount = _idx.Count / 3 };
        mesh.AllocVertices();
        mesh.AllocNormals();
        mesh.AllocColors();
        mesh.AllocIndices();
        var v = mesh.VerticesAs<Vector3>();
        var n = mesh.NormalsAs<Vector3>();
        var c = mesh.ColorsAs<Color>();
        var ix = mesh.IndicesAs<ushort>();
        for (int i = 0; i < _pos.Count; i++) { v[i] = _pos[i]; n[i] = _nrm[i]; c[i] = _col[i]; }
        for (int i = 0; i < _idx.Count; i++) ix[i] = _idx[i];
        Raylib.UploadMesh(ref mesh, false);
        return mesh;
    }
}

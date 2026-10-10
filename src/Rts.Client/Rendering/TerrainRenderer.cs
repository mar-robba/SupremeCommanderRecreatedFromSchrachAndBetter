using System.Numerics;
using Raylib_cs;
using Rts.Sim.Map;

namespace Rts.Client.Rendering;

/// <summary>
/// Terrain as 64x64-cell chunk meshes (16-bit indices) with height/slope-based colors and baked sun lighting,
/// a translucent water plane, and resource deposit markers.
/// </summary>
public sealed class TerrainRenderer : IDisposable
{
    public const int ChunkSize = 64;
    public static readonly Vector3 SunDir = Vector3.Normalize(new Vector3(-0.45f, 0.8f, -0.35f));

    private readonly Terrain _t;
    private readonly List<(Mesh mesh, BoundingBox box)> _chunks = new();
    private Material _material;

    public TerrainRenderer(Terrain terrain)
    {
        _t = terrain;
        _material = Raylib.LoadMaterialDefault();
        for (int cz = 0; cz < terrain.Height; cz += ChunkSize)
        for (int cx = 0; cx < terrain.Width; cx += ChunkSize)
            _chunks.Add(BuildChunk(cx, cz, Math.Min(ChunkSize, terrain.Width - cx), Math.Min(ChunkSize, terrain.Height - cz)));
    }

    private unsafe (Mesh, BoundingBox) BuildChunk(int x0, int z0, int w, int h)
    {
        int vw = w + 1, vh = h + 1;
        var mesh = new Mesh { VertexCount = vw * vh, TriangleCount = w * h * 2 };
        mesh.AllocVertices();
        mesh.AllocNormals();
        mesh.AllocColors();
        mesh.AllocIndices();
        var verts = mesh.VerticesAs<Vector3>();
        var norms = mesh.NormalsAs<Vector3>();
        var cols = mesh.ColorsAs<Color>();
        var idx = mesh.IndicesAs<ushort>();
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int z = 0; z < vh; z++)
        for (int x = 0; x < vw; x++)
        {
            int wx = x0 + x, wz = z0 + z;
            float y = _t.VertexHeight(wx, wz);
            minY = MathF.Min(minY, y);
            maxY = MathF.Max(maxY, y);
            int v = z * vw + x;
            verts[v] = new Vector3(wx, y, wz);
            var n = _t.GetNormal(wx, wz);
            norms[v] = n;
            cols[v] = ShadeColor(wx, wz, y, n);
        }
        int k = 0;
        for (int z = 0; z < h; z++)
        for (int x = 0; x < w; x++)
        {
            ushort a = (ushort)(z * vw + x), b = (ushort)(a + 1), c = (ushort)(a + vw), d = (ushort)(c + 1);
            // Counter-clockwise when seen from above (+Y).
            idx[k++] = a; idx[k++] = c; idx[k++] = b;
            idx[k++] = b; idx[k++] = c; idx[k++] = d;
        }
        Raylib.UploadMesh(ref mesh, false);
        var box = new BoundingBox(new Vector3(x0, minY, z0), new Vector3(x0 + w, maxY, z0 + h));
        return (mesh, box);
    }

    private Color ShadeColor(int x, int z, float y, Vector3 n)
    {
        float slope = 1f - n.Y;
        float hRel = (y - _t.WaterLevel);
        Vector3 c;
        if (hRel < 0.4f) c = new Vector3(0.62f, 0.58f, 0.42f);                // wet sand / seabed
        else if (hRel < 1.2f) c = new Vector3(0.70f, 0.66f, 0.48f);           // beach
        else c = Vector3.Lerp(new Vector3(0.33f, 0.45f, 0.22f), new Vector3(0.45f, 0.48f, 0.28f), Math.Clamp(hRel / 22f, 0, 1));
        // Rocky slopes.
        c = Vector3.Lerp(c, new Vector3(0.45f, 0.40f, 0.35f), Math.Clamp((slope - 0.12f) * 4f, 0, 1));
        // Subtle procedural variation.
        float noise = ((x * 73856093 ^ z * 19349663) & 255) / 255f;
        c *= 0.94f + noise * 0.08f;
        float light = 0.38f + 0.72f * MathF.Max(0, Vector3.Dot(n, SunDir));
        c *= light;
        return new Color((byte)Math.Clamp(c.X * 255, 0, 255), (byte)Math.Clamp(c.Y * 255, 0, 255), (byte)Math.Clamp(c.Z * 255, 0, 255), (byte)255);
    }

    public void Draw(Camera3D cam)
    {
        foreach (var (mesh, _) in _chunks) Raylib.DrawMesh(mesh, _material, Matrix4x4.Identity);
    }

    public void DrawDeposits()
    {
        foreach (var d in _t.Deposits)
        {
            var p = new Vector3(d.Pos.X, _t.GetHeight(d.Pos) + 0.08f, d.Pos.Y);
            if (d.Type == DepositType.Mass)
            {
                Raylib.DrawCube(p, 1.6f, 0.1f, 1.6f, new Color(60, 200, 90, 255));
                Raylib.DrawCubeWires(p, 2.0f, 0.12f, 2.0f, new Color(20, 90, 30, 255));
            }
            else
            {
                Raylib.DrawCylinder(p, 1.8f, 1.8f, 0.1f, 16, new Color(40, 40, 40, 255));
                Raylib.DrawCylinderWires(p, 2.0f, 2.0f, 0.12f, 16, new Color(200, 160, 40, 255));
            }
        }
    }

    public void DrawWater()
    {
        if (_t.WaterLevel <= 0) return;
        Rlgl.DisableBackfaceCulling();
        Raylib.DrawPlane(new Vector3(_t.Width * 0.5f, _t.WaterLevel, _t.Height * 0.5f), new Vector2(_t.Width, _t.Height), new Color(40, 90, 150, 150));
        Rlgl.EnableBackfaceCulling();
    }

    /// <summary>Ray vs heightmap: marches along the ray then refines by bisection. Returns null if no hit.</summary>
    public Vector3? Pick(Ray ray, float maxDist)
    {
        float step = 1.0f;
        float prevT = 0;
        bool prevAbove = Above(ray.Position);
        for (float t = step; t < maxDist; t += step)
        {
            var p = ray.Position + ray.Direction * t;
            bool above = Above(p);
            if (prevAbove && !above)
            {
                float lo = prevT, hi = t;
                for (int i = 0; i < 12; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (Above(ray.Position + ray.Direction * mid)) lo = mid; else hi = mid;
                }
                var hit = ray.Position + ray.Direction * hi;
                if (hit.X < 0 || hit.Z < 0 || hit.X > _t.Width || hit.Z > _t.Height) return null;
                return hit;
            }
            prevAbove = above;
            prevT = t;
            if (t > 50) step = 2f;
        }
        return null;
    }

    private bool Above(Vector3 p) => p.Y > MathF.Max(_t.GetHeight(p.X, p.Z), _t.WaterLevel);

    public void Dispose()
    {
        foreach (var (mesh, _) in _chunks) Raylib.UnloadMesh(mesh);
        _chunks.Clear();
    }
}

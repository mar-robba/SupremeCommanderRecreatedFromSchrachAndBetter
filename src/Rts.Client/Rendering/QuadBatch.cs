using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>
/// Dynamic quad buffer drawn with one DrawMesh per 16k-quad page. Used for particles, projectiles, decals
/// (world space, inside BeginMode3D) and strategic icons (screen space, outside 3D mode).
/// Vertex layout: position, texcoord (quad uv), texcoord2 (shape id, seed), color.
/// </summary>
public sealed unsafe class QuadBatch : IDisposable
{
    public const int QuadsPerPage = 16384;
    private readonly List<Mesh> _pages = new();
    private int _count;
    public Material Material;

    public int Count => _count;

    public QuadBatch(Shader shader)
    {
        Material = Raylib.LoadMaterialDefault();
        Material.Shader = shader;
    }

    private Mesh NewPage()
    {
        var m = new Mesh { VertexCount = QuadsPerPage * 4, TriangleCount = QuadsPerPage * 2 };
        m.AllocVertices();
        m.AllocTexCoords();
        m.AllocTexCoords2();
        m.AllocColors();
        m.AllocIndices();
        var ix = m.IndicesAs<ushort>();
        for (int q = 0; q < QuadsPerPage; q++)
        {
            int v = q * 4, i = q * 6;
            ix[i] = (ushort)v; ix[i + 1] = (ushort)(v + 1); ix[i + 2] = (ushort)(v + 2);
            ix[i + 3] = (ushort)v; ix[i + 4] = (ushort)(v + 2); ix[i + 5] = (ushort)(v + 3);
        }
        Raylib.UploadMesh(ref m, true);
        return m;
    }

    public void Begin() => _count = 0;

    /// <summary>Adds a quad a-b-c-d with uv (0,0),(1,0),(1,1),(0,1) or the given uv rectangle.</summary>
    public void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, float shape, float seed, Vector4? uvRect = null)
    {
        int page = _count / QuadsPerPage;
        if (page >= 8) return; // hard cap: 131k quads
        if (page == _pages.Count) _pages.Add(NewPage());
        var m = _pages[page];
        int v = (_count % QuadsPerPage) * 4;
        var pos = (Vector3*)m.Vertices;
        var uv = (Vector2*)m.TexCoords;
        var uv2 = (Vector2*)m.TexCoords2;
        var cl = (Color*)m.Colors;
        pos[v] = a; pos[v + 1] = b; pos[v + 2] = c; pos[v + 3] = d;
        var r = uvRect ?? new Vector4(0, 0, 1, 1);
        uv[v] = new Vector2(r.X, r.Y); uv[v + 1] = new Vector2(r.Z, r.Y); uv[v + 2] = new Vector2(r.Z, r.W); uv[v + 3] = new Vector2(r.X, r.W);
        var p = new Vector2(shape, seed);
        uv2[v] = p; uv2[v + 1] = p; uv2[v + 2] = p; uv2[v + 3] = p;
        cl[v] = col; cl[v + 1] = col; cl[v + 2] = col; cl[v + 3] = col;
        _count++;
    }

    /// <summary>Camera-facing square billboard.</summary>
    public void Billboard(Vector3 c, float size, float rot, Vector3 right, Vector3 up, Color col, float shape, float seed)
    {
        float cs = MathF.Cos(rot) * size * 0.5f, sn = MathF.Sin(rot) * size * 0.5f;
        var r = right * cs + up * sn;
        var u = up * cs - right * sn;
        Add(c - r - u, c + r - u, c + r + u, c - r + u, col, shape, seed);
    }

    /// <summary>Quad stretched along a world axis (v along the axis, head at +v), facing the camera.</summary>
    public void Streak(Vector3 head, Vector3 dir, float length, float width, Vector3 camPos, Color col, float shape, float seed)
    {
        var toCam = camPos - head;
        var side = Vector3.Cross(dir, toCam);
        float l = side.Length();
        if (l < 1e-5f) return;
        side *= width * 0.5f / l;
        var tail = head - dir * length;
        Add(tail - side, tail + side, head + side, head - side, col, shape, seed);
    }

    /// <summary>Horizontal (ground-aligned) quad.</summary>
    public void Flat(Vector3 c, float size, float rot, Color col, float shape, float seed)
    {
        float cs = MathF.Cos(rot) * size * 0.5f, sn = MathF.Sin(rot) * size * 0.5f;
        var r = new Vector3(cs, 0, sn);
        var u = new Vector3(-sn, 0, cs);
        Add(c - r - u, c + r - u, c + r + u, c - r + u, col, shape, seed);
    }

    /// <summary>Screen-space quad (pixels), for use outside BeginMode3D.</summary>
    public void Screen(Vector2 center, float size, Color col, Vector4 uvRect)
    {
        float h = size * 0.5f;
        Add(new Vector3(center.X - h, center.Y - h, 0), new Vector3(center.X + h, center.Y - h, 0),
            new Vector3(center.X + h, center.Y + h, 0), new Vector3(center.X - h, center.Y + h, 0), col, 0, 0, uvRect);
    }

    /// <summary>Uploads and draws everything added since Begin. Caller sets blend/depth state.</summary>
    public void Draw()
    {
        int remaining = _count;
        for (int p = 0; p < _pages.Count && remaining > 0; p++)
        {
            int n = Math.Min(remaining, QuadsPerPage);
            remaining -= n;
            var m = _pages[p];
            Raylib.UpdateMeshBuffer(m, Mesh.VboIdIndexVertices, m.Vertices, n * 4 * sizeof(Vector3), 0);
            Raylib.UpdateMeshBuffer(m, Mesh.VboIdIndexTexCoords, m.TexCoords, n * 4 * sizeof(Vector2), 0);
            Raylib.UpdateMeshBuffer(m, Mesh.VboIdIndexTexCoords2, m.TexCoords2, n * 4 * sizeof(Vector2), 0);
            Raylib.UpdateMeshBuffer(m, Mesh.VboIdIndexColors, m.Colors, n * 4 * sizeof(Color), 0);
            var draw = m;
            draw.TriangleCount = n * 2;
            Raylib.DrawMesh(draw, Material, Matrix4x4.Identity);
        }
    }

    public void Dispose()
    {
        foreach (var m in _pages) Raylib.UnloadMesh(m);
        _pages.Clear();
    }
}

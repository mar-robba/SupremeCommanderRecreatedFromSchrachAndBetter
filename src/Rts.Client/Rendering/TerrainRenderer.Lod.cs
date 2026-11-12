using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>
/// Terrain chunk LOD (wave 5m): every 64×64 chunk has meshes at vertex strides 1, 2, 4 and 8. Each frame a chunk uses
/// the coarsest stride whose cells still project to at most <see cref="LodCellPixels"/> pixels, so a whole 1024 map at
/// strategic zoom draws ~1/16 of the triangles (tiny triangles were most of the GPU frame). Cracks between chunks
/// of different strides are hidden by skirts: every inner chunk edge hangs a vertical strip below the surface, as
/// deep as twice the largest height error of any stride along that edge.
/// </summary>
public sealed unsafe partial class TerrainRenderer
{
    private static readonly int[] LodStrides = [1, 2, 4, 8];

    /// <summary>A chunk uses the coarsest stride whose cell is at most this many pixels on screen.</summary>
    public static float LodCellPixels = 5f;

    /// <summary>Debug/bench: force one LOD level (-1 = automatic).</summary>
    public static int ForceLod = -1;

    /// <summary>Triangles drawn in the last frame (perf overlay).</summary>
    public static int TrianglesDrawn;

    private readonly struct TerrainChunk(Mesh[] lods, BoundingBox box)
    {
        public readonly Mesh[] Lods = lods;
        public readonly BoundingBox Box = box;
    }

    private readonly List<TerrainChunk> _lodChunks = new();

    private void BuildLodChunks(float[] ao)
    {
        for (int cz = 0; cz < _t.Height; cz += ChunkSize)
        for (int cx = 0; cx < _t.Width; cx += ChunkSize)
        {
            int w = Math.Min(ChunkSize, _t.Width - cx), h = Math.Min(ChunkSize, _t.Height - cz);
            // Skirt depth per edge (W, E, N=z0, S=z0+h); 0 on the map border.
            var depth = new float[4];
            if (cx > 0) depth[0] = SkirtDepth(cx, cz, 0, 1, h);
            if (cx + w < _t.Width) depth[1] = SkirtDepth(cx + w, cz, 0, 1, h);
            if (cz > 0) depth[2] = SkirtDepth(cx, cz, 1, 0, w);
            if (cz + h < _t.Height) depth[3] = SkirtDepth(cx, cz + h, 1, 0, w);
            var lods = new Mesh[LodStrides.Length];
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int l = 0; l < lods.Length; l++)
                lods[l] = BuildLodMesh(cx, cz, w, h, LodStrides[l], ao, depth, ref minY, ref maxY);
            _lodChunks.Add(new TerrainChunk(lods, new BoundingBox(new Vector3(cx, minY, cz), new Vector3(cx + w, maxY, cz + h))));
        }
    }

    /// <summary>Twice the largest |fine − coarse| height along an edge of n cells starting at (x, z), over every stride.</summary>
    private float SkirtDepth(int x, int z, int dx, int dz, int n)
    {
        float err = 0;
        foreach (int s in LodStrides)
        {
            if (s == 1) continue;
            for (int i = 0; i <= n; i++)
            {
                int a = Math.Min(i / s * s, n), b = Math.Min(a + s, n);
                float ha = _t.VertexHeight(x + dx * a, z + dz * a), hb = _t.VertexHeight(x + dx * b, z + dz * b);
                float f = b > a ? (i - a) / (float)(b - a) : 0;
                err = MathF.Max(err, MathF.Abs(_t.VertexHeight(x + dx * i, z + dz * i) - (ha + (hb - ha) * f)));
            }
        }
        return err * 2f + 0.2f;
    }

    /// <summary>Vertex offsets 0, s, 2s, … and always n (chunks whose size is not a multiple of the stride).</summary>
    private static int[] Steps(int n, int s)
    {
        var l = new List<int>();
        for (int i = 0; i < n; i += s) l.Add(i);
        l.Add(n);
        return l.ToArray();
    }

    private Mesh BuildLodMesh(int x0, int z0, int w, int h, int s, float[] ao, float[] skirt, ref float minY, ref float maxY)
    {
        var xs = Steps(w, s);
        var zs = Steps(h, s);
        int vw = xs.Length, vh = zs.Length;
        // Skirt vertex rings: one per edge with depth > 0.
        int skirtVerts = 0, skirtTris = 0;
        for (int e = 0; e < 4; e++)
        {
            if (skirt[e] <= 0) continue;
            int len = e < 2 ? vh : vw;
            skirtVerts += len * 2;
            skirtTris += (len - 1) * 4;
        }
        var mesh = new Mesh { VertexCount = vw * vh + skirtVerts, TriangleCount = (vw - 1) * (vh - 1) * 2 + skirtTris };
        mesh.AllocVertices();
        mesh.AllocNormals();
        mesh.AllocColors();
        mesh.AllocIndices();
        var verts = mesh.VerticesAs<Vector3>();
        var norms = mesh.NormalsAs<Vector3>();
        var cols = mesh.ColorsAs<Color>();
        var idx = mesh.IndicesAs<ushort>();

        for (int z = 0; z < vh; z++)
        for (int x = 0; x < vw; x++)
        {
            int wx = x0 + xs[x], wz = z0 + zs[z];
            SetVertex(verts, norms, cols, ao, z * vw + x, wx, wz, 0, s);
            float y = verts[z * vw + x].Y;
            minY = MathF.Min(minY, y);
            maxY = MathF.Max(maxY, y);
        }
        int k = 0;
        for (int z = 0; z < vh - 1; z++)
        for (int x = 0; x < vw - 1; x++)
        {
            ushort a = (ushort)(z * vw + x), b = (ushort)(a + 1), c = (ushort)(a + vw), d = (ushort)(c + 1);
            // Counter-clockwise when seen from above (+Y).
            idx[k++] = a; idx[k++] = c; idx[k++] = b;
            idx[k++] = b; idx[k++] = c; idx[k++] = d;
        }

        int next = vw * vh;
        for (int e = 0; e < 4; e++)
        {
            if (skirt[e] <= 0) continue;
            int len = e < 2 ? vh : vw;
            int baseTop = next, baseBot = next + len;
            for (int i = 0; i < len; i++)
            {
                int gx = e == 0 ? 0 : e == 1 ? vw - 1 : i;
                int gz = e < 2 ? i : e == 2 ? 0 : vh - 1;
                SetVertex(verts, norms, cols, ao, baseTop + i, x0 + xs[gx], z0 + zs[gz], 0, s);
                SetVertex(verts, norms, cols, ao, baseBot + i, x0 + xs[gx], z0 + zs[gz], skirt[e], s);
            }
            minY = MathF.Min(minY, verts[baseBot].Y);
            for (int i = 0; i < len - 1; i++)
            {
                ushort t0 = (ushort)(baseTop + i), t1 = (ushort)(t0 + 1), b0 = (ushort)(baseBot + i), b1 = (ushort)(b0 + 1);
                // Both windings: the strip is seen from either side.
                idx[k++] = t0; idx[k++] = b0; idx[k++] = t1;
                idx[k++] = t1; idx[k++] = b0; idx[k++] = b1;
                idx[k++] = t0; idx[k++] = t1; idx[k++] = b0;
                idx[k++] = t1; idx[k++] = b1; idx[k++] = b0;
            }
            next += len * 2;
        }
        Raylib.UploadMesh(ref mesh, false);
        return mesh;
    }

    /// <summary>Vertex colour: R = cavity AO, G = (9b-2) ground lit factor of the terrain self-shadows, B = AO.</summary>
    private void SetVertex(Span<Vector3> verts, Span<Vector3> norms, Span<Color> cols, float[] ao, int v, int wx, int wz, float drop, int stride)
    {
        float y = _t.VertexHeight(wx, wz);
        verts[v] = new Vector3(wx, y - drop, wz);
        norms[v] = _t.GetNormal(wx, wz);
        byte a = (byte)Math.Clamp(ao[wz * (_t.Width + 1) + wx] / 1.06f * 255f, 0, 255);
        cols[v] = new Color(a, GroundLitByte(wx, wz, stride), a, (byte)255);
    }

    /// <summary>LOD level for a chunk: the coarsest stride whose cell projects to ≤ <see cref="LodCellPixels"/>.</summary>
    private static int ChooseLod(FrameView fv, in BoundingBox box)
    {
        if (ForceLod >= 0) return Math.Min(ForceLod, LodStrides.Length - 1);
        float d = MathF.Max(1f, Vector3.Distance(fv.CamPos, Vector3.Clamp(fv.CamPos, box.Min, box.Max)));
        float pixelsPerUnit = fv.ScreenH * MathF.Abs(fv.Proj.M22) / (2f * d);
        int lod = 0;
        while (lod + 1 < LodStrides.Length && LodStrides[lod + 1] * pixelsPerUnit <= LodCellPixels) lod++;
        return lod;
    }

    private void DrawLodChunks(FrameView fv)
    {
        int tris = 0;
        foreach (var ch in _lodChunks)
        {
            var box = ch.Box;
            var c = (box.Min + box.Max) * 0.5f;
            if (!fv.SphereVisible(c, Vector3.Distance(box.Min, box.Max) * 0.5f)) continue;
            var mesh = ch.Lods[ChooseLod(fv, box)];
            tris += mesh.TriangleCount;
            Raylib.DrawMesh(mesh, _material, Matrix4x4.Identity);
        }
        TrianglesDrawn = tris;
    }

    private void DisposeLodChunks()
    {
        foreach (var ch in _lodChunks)
            foreach (var m in ch.Lods) Raylib.UnloadMesh(m);
        _lodChunks.Clear();
    }
}

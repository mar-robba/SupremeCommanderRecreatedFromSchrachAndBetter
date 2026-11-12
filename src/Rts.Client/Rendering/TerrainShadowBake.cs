using System.Numerics;

namespace Rts.Client.Rendering;

/// <summary>
/// (9b-2) Terrain self-shadows baked from the heightmap (pure, no GL; <c>TerrainShadowBakeTests</c>).
/// For every height vertex the bake marches towards the sun and stores the <b>shadow line</b>
/// S = max over t of (h(p + t·sunXZ) − t·tan(sun elevation)): a point at height y above that vertex is in the
/// terrain's shadow iff y &lt; S. The ground's lit factor (<see cref="GroundLit"/>, from S − h) goes into the terrain
/// vertex colours; units, props and aircraft compare their own height with S, so a tank in a canyon's shadow is dark
/// and an aircraft above it is lit. Flat or sun-facing ground gets S ≈ h − 1 (lit with margin).
/// </summary>
public static class TerrainShadowBake
{
    /// <summary>First sample distance (about one cell: closer samples only see the vertex's own slope).</summary>
    public const float FirstStep = 0.75f;
    /// <summary>Lowest stored value relative to the vertex height (no occluder at all).</summary>
    public const float NoOccluder = -2f;

    /// <summary>Bakes the shadow line for a (vw × vh) vertex grid (index z·vw + x, 1 world unit per cell).
    /// <paramref name="sunDir"/> points towards the sun (y up). Parallel over rows; the result does not depend on
    /// the thread count (every vertex is independent).</summary>
    public static float[] Bake(float[] heights, int vw, int vh, Vector3 sunDir, bool parallel = true)
    {
        if (heights.Length != vw * vh) throw new ArgumentException("heights size mismatch");
        var result = new float[vw * vh];
        float horiz = MathF.Sqrt(sunDir.X * sunDir.X + sunDir.Z * sunDir.Z);
        if (horiz < 1e-4f || sunDir.Y <= 0)
        {
            // Sun at the zenith (nothing casts a shadow) or below the horizon (everything is dark: S far above).
            for (int i = 0; i < result.Length; i++) result[i] = heights[i] + (sunDir.Y <= 0 ? 1e4f : NoOccluder);
            return result;
        }
        float dx = sunDir.X / horiz, dz = sunDir.Z / horiz, tan = sunDir.Y / horiz;
        float maxH = float.MinValue;
        foreach (float h in heights) maxH = MathF.Max(maxH, h);
        void Row(int z)
        {
            for (int x = 0; x < vw; x++)
                result[z * vw + x] = ShadowLine(heights, vw, vh, x, z, dx, dz, tan, maxH);
        }
        if (parallel) Parallel.For(0, vh, Row);
        else for (int z = 0; z < vh; z++) Row(z);
        return result;
    }

    /// <summary>Shadow line of one vertex: max of h(t) − t·tan along the sun direction, never below
    /// h + <see cref="NoOccluder"/>. Stops at the map edge (nothing outside casts shadows) or once no height left on
    /// the map can rise above the current value.</summary>
    public static float ShadowLine(float[] heights, int vw, int vh, int x, int z, float dx, float dz, float tan, float maxH)
    {
        float h0 = heights[z * vw + x];
        float s = h0 + NoOccluder;
        float maxX = vw - 1, maxZ = vh - 1;
        float t = FirstStep;
        while (true)
        {
            // Beyond this distance even the highest vertex of the map stays below the current shadow line.
            if (maxH - t * tan <= s) break;
            float px = x + dx * t, pz = z + dz * t;
            if (px < 0 || pz < 0 || px > maxX || pz > maxZ) break;
            float v = Sample(heights, vw, vh, px, pz) - t * tan;
            if (v > s) s = v;
            t += MathF.Max(0.5f, t * 0.04f);
        }
        return s;
    }

    /// <summary>Bilinear height at a point inside the grid.</summary>
    public static float Sample(float[] h, int vw, int vh, float x, float z)
    {
        int x0 = Math.Min((int)x, vw - 2), z0 = Math.Min((int)z, vh - 2);
        if (x0 < 0) x0 = 0;
        if (z0 < 0) z0 = 0;
        float fx = x - x0, fz = z - z0;
        int i = z0 * vw + x0;
        float a = h[i] + (h[i + 1] - h[i]) * fx;
        float b = h[i + vw] + (h[i + vw + 1] - h[i + vw]) * fx;
        return a + (b - a) * fz;
    }

    /// <summary>Lit fraction (0 = full terrain shadow, 1 = lit) of a point at height <paramref name="y"/> under a
    /// shadow line <paramref name="s"/>; the same soft edge as the shader's <c>terrainShadow</c>.</summary>
    public static float Lit(float y, float s, float softness = Softness)
    {
        float d = Math.Clamp((s - y) / (2 * softness), 0f, 1f);
        return 1f - d * d * (3f - 2f * d);
    }

    /// <summary>Lit fraction of the ground at every vertex: <see cref="Lit"/>(h, S). The terrain meshes carry it
    /// in their vertex colours (G), so the ground pays no texture fetch (TerrainRenderer.Shadow.cs).</summary>
    public static float[] GroundLit(float[] heights, float[] line)
    {
        var lit = new float[line.Length];
        for (int i = 0; i < lit.Length; i++) lit[i] = Lit(heights[i], line[i]);
        return lit;
    }

    /// <summary>Mean of a per-vertex grid over the (2r+1)² vertices around (x, z), clamped to the grid. A coarse
    /// terrain LOD vertex (stride s, r = s/2) takes the mean of the footprint it stands for, like a mip level, so
    /// far shadows neither alias nor pop when the LOD changes.</summary>
    public static float BoxMean(float[] v, int vw, int vh, int x, int z, int r)
    {
        if (r <= 0) return v[z * vw + x];
        int x0 = Math.Max(x - r, 0), x1 = Math.Min(x + r, vw - 1), z0 = Math.Max(z - r, 0), z1 = Math.Min(z + r, vh - 1);
        float sum = 0;
        for (int zz = z0; zz <= z1; zz++)
        for (int xx = x0; xx <= x1; xx++) sum += v[zz * vw + xx];
        return sum / ((x1 - x0 + 1) * (z1 - z0 + 1));
    }

    /// <summary>Half width (world height units) of the shadow edge in the shaders. The edge starts at the shadow
    /// line itself (d = 0), not around it: flat ground under a low sun has a line FirstStep·tan(elev) below the
    /// ground (the march's first sample) and must stay fully lit.</summary>
    public const float Softness = 0.6f;

    /// <summary>Separable 3×3 box blur of the shadow line (<paramref name="passes"/> times): the ray march samples
    /// far ridges every few cells, so the raw line has stair steps along the shadow edge; the blur turns them into
    /// a soft penumbra of about a cell.</summary>
    public static void Blur(float[] s, int w, int h, int passes = 2)
    {
        var tmp = new float[s.Length];
        for (int p = 0; p < passes; p++)
        {
            for (int z = 0; z < h; z++)
            {
                int row = z * w;
                for (int x = 0; x < w; x++)
                    tmp[row + x] = (s[row + Math.Max(x - 1, 0)] + s[row + x] + s[row + Math.Min(x + 1, w - 1)]) * (1f / 3f);
            }
            for (int z = 0; z < h; z++)
            {
                int up = Math.Max(z - 1, 0) * w, row = z * w, dn = Math.Min(z + 1, h - 1) * w;
                for (int x = 0; x < w; x++)
                    s[row + x] = (tmp[up + x] + tmp[row + x] + tmp[dn + x]) * (1f / 3f);
            }
        }
    }
}

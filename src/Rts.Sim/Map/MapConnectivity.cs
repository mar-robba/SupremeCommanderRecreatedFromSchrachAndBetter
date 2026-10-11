using System.Numerics;

namespace Rts.Sim.Map;

/// <summary>
/// Land connectivity analysis and repair for the map generator, working directly on the vertex height array
/// ((n+1)² vertices, n² cells). The land rule is identical to <see cref="Terrain"/>'s passability
/// (slope ≤ <see cref="Terrain.DefaultMaxSlope"/> and above water).
/// <list type="bullet">
/// <item><see cref="ComputeLand"/> + <see cref="Erode"/>: cells where a unit needing a given clearance fits.</item>
/// <item><see cref="Label"/>: 4-connected components.</item>
/// <item><see cref="FindPaths"/>: multi-source Dijkstra on a coarse grid (cost grows with water/cliffs) from the
///   main component to every target component: where a corridor is cheapest to carve.</item>
/// <item><see cref="CarveCorridor"/>: raises/lowers a band along a polyline to a slope-limited profile above water
///   (a ramp up a cliff, a causeway or ford across water, a pass through a ridge).</item>
/// </list>
/// </summary>
internal static class MapConnectivity
{
    /// <summary>Slope (height per unit) of carved corridors; well below the passability limit even diagonally.</summary>
    public const float CorridorSlope = 0.3f;

    public static void ComputeLand(float[] h, int n, float water, byte[] land)
    {
        int stride = n + 1;
        Parallel.For(0, n, cz =>
        {
            int row = cz * stride;
            for (int cx = 0; cx < n; cx++)
            {
                float h00 = h[row + cx], h10 = h[row + cx + 1];
                float h01 = h[row + stride + cx], h11 = h[row + stride + cx + 1];
                float min = MathF.Min(MathF.Min(h00, h10), MathF.Min(h01, h11));
                float max = MathF.Max(MathF.Max(h00, h10), MathF.Max(h01, h11));
                land[cz * n + cx] = max - min <= Terrain.DefaultMaxSlope && min >= water ? (byte)1 : (byte)0;
            }
        });
    }

    /// <summary>dst[c] = 1 if every cell within Chebyshev radius r of c is 1 in src (outside the map counts as 0).</summary>
    public static void Erode(byte[] src, int n, int r, byte[] dst)
    {
        if (r <= 0)
        {
            Array.Copy(src, dst, src.Length);
            return;
        }
        var tmp = new byte[src.Length];
        // Horizontal pass.
        Parallel.For(0, n, z =>
        {
            int row = z * n;
            int run = 0; // length of the run of 1s ending at x
            var runs = new int[n];
            for (int x = 0; x < n; x++) { run = src[row + x] != 0 ? run + 1 : 0; runs[x] = run; }
            for (int x = 0; x < n; x++)
            {
                int x1 = x + r;
                tmp[row + x] = x - r >= 0 && x1 < n && runs[x1] >= 2 * r + 1 ? (byte)1 : (byte)0;
            }
        });
        // Vertical pass.
        Parallel.For(0, n, x =>
        {
            var runs = new int[n];
            int run = 0;
            for (int z = 0; z < n; z++) { run = tmp[z * n + x] != 0 ? run + 1 : 0; runs[z] = run; }
            for (int z = 0; z < n; z++)
            {
                int z1 = z + r;
                dst[z * n + x] = z - r >= 0 && z1 < n && runs[z1] >= 2 * r + 1 ? (byte)1 : (byte)0;
            }
        });
    }

    /// <summary>4-connected components of mask (labels -1 where 0). Returns the component count; sizes[i] = cells.</summary>
    public static int Label(byte[] mask, int n, int[] labels, List<int> sizes)
    {
        Array.Fill(labels, -1);
        sizes.Clear();
        var queue = new int[n * n];
        int count = 0;
        for (int start = 0; start < mask.Length; start++)
        {
            if (mask[start] == 0 || labels[start] >= 0) continue;
            int head = 0, tail = 0;
            queue[tail++] = start;
            labels[start] = count;
            while (head < tail)
            {
                int c = queue[head++];
                int x = c % n, z = c / n;
                if (x > 0) Visit(c - 1);
                if (x < n - 1) Visit(c + 1);
                if (z > 0) Visit(c - n);
                if (z < n - 1) Visit(c + n);
            }
            sizes.Add(tail);
            count++;

            void Visit(int nb)
            {
                if (mask[nb] == 0 || labels[nb] >= 0) return;
                labels[nb] = count;
                queue[tail++] = nb;
            }
        }
        return count;
    }

    /// <summary>Label of the component at (or nearest to, within radius) a world position; -1 if none.</summary>
    public static int LabelNear(int[] labels, int n, Vector2 p, int radius)
    {
        int cx = Math.Clamp((int)MathF.Floor(p.X), 0, n - 1), cz = Math.Clamp((int)MathF.Floor(p.Y), 0, n - 1);
        if (labels[cz * n + cx] >= 0) return labels[cz * n + cx];
        int best = -1, bestD = int.MaxValue;
        for (int dz = -radius; dz <= radius; dz++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int x = cx + dx, z = cz + dz;
            if ((uint)x >= (uint)n || (uint)z >= (uint)n) continue;
            int l = labels[z * n + x];
            int d = dx * dx + dz * dz;
            if (l >= 0 && d < bestD) { bestD = d; best = l; }
        }
        return best;
    }

    /// <summary>
    /// Coarse-grid multi-source Dijkstra from every cell of component <paramref name="main"/>. For each target
    /// component returns a polyline (world coordinates) from a main-component cell to a target-component cell,
    /// through the cheapest coarse cells (land is cheap, cliffs and water are expensive), or null if unreachable.
    /// </summary>
    public static Dictionary<int, List<Vector2>> FindPaths(float[] h, int n, float water, int[] labels, byte[] okMask,
        int main, IReadOnlyCollection<int> targets)
    {
        int f = Math.Max(2, n / 128);
        int cw = (n + f - 1) / f;
        int cn = cw * cw;
        var cost = new float[cn];
        var avgH = new float[cn];
        var hasMain = new bool[cn];
        int stride = n + 1;
        for (int bz = 0; bz < cw; bz++)
        for (int bx = 0; bx < cw; bx++)
        {
            int ok = 0, wet = 0, total = 0;
            float sumH = 0;
            bool m = false;
            for (int z = bz * f; z < Math.Min(n, bz * f + f); z++)
            for (int x = bx * f; x < Math.Min(n, bx * f + f); x++)
            {
                int c = z * n + x;
                total++;
                if (okMask[c] != 0) ok++;
                float hv = h[z * stride + x];
                if (hv < water) wet++;
                sumH += MathF.Max(hv, water);
                if (labels[c] == main) m = true;
            }
            float landFrac = ok / (float)Math.Max(1, total), wetFrac = wet / (float)Math.Max(1, total);
            cost[bz * cw + bx] = 1f + 14f * (1f - landFrac) + 8f * wetFrac;
            avgH[bz * cw + bx] = sumH / Math.Max(1, total);
            hasMain[bz * cw + bx] = m;
        }

        var dist = new float[cn];
        var parent = new int[cn];
        Array.Fill(dist, float.MaxValue);
        Array.Fill(parent, -1);
        var pq = new PriorityQueue<int, float>();
        for (int c = 0; c < cn; c++)
            if (hasMain[c]) { dist[c] = 0; pq.Enqueue(c, 0); }
        while (pq.TryDequeue(out int c, out float d))
        {
            if (d > dist[c]) continue;
            int x = c % cw, z = c / cw;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = x + dx, nz = z + dz;
                if ((uint)nx >= (uint)cw || (uint)nz >= (uint)cw) continue;
                int nb = nz * cw + nx;
                // Climbing is expensive too (ramps are long): prefer level routes over hills and mesas.
                float step = (cost[c] + cost[nb]) * 0.5f * (dx != 0 && dz != 0 ? 1.4142f : 1f)
                             + 1.5f * MathF.Abs(avgH[nb] - avgH[c]);
                float nd = d + step;
                if (nd < dist[nb]) { dist[nb] = nd; parent[nb] = c; pq.Enqueue(nb, nd); }
            }
        }

        // Best coarse cell per target component.
        var bestCell = new Dictionary<int, int>();
        var targetSet = new HashSet<int>(targets);
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int l = labels[z * n + x];
            if (l < 0 || !targetSet.Contains(l)) continue;
            int cc = (z / f) * cw + x / f;
            if (!bestCell.TryGetValue(l, out int b) || dist[cc] < dist[b] || (dist[cc] == dist[b] && cc < b)) bestCell[l] = cc;
        }

        var result = new Dictionary<int, List<Vector2>>();
        foreach (int t in targets)
        {
            if (!bestCell.TryGetValue(t, out int cell) || dist[cell] == float.MaxValue) continue;
            var coarse = new List<int>();
            for (int c = cell; c >= 0; c = parent[c]) coarse.Add(c);
            coarse.Reverse(); // main -> target
            var pts = new List<Vector2>(coarse.Count + 2);
            Vector2 Center(int c) => new((c % cw) * f + f * 0.5f, (c / cw) * f + f * 0.5f);
            pts.Add(NearestCellOf(labels, n, main, coarse[0], cw, f, Center(coarse[0])));
            for (int i = 1; i < coarse.Count - 1; i++) pts.Add(Center(coarse[i]));
            pts.Add(NearestCellOf(labels, n, t, coarse[^1], cw, f, Center(coarse[^1])));
            result[t] = pts;
        }
        return result;
    }

    private static Vector2 NearestCellOf(int[] labels, int n, int label, int coarseCell, int cw, int f, Vector2 fallback)
    {
        int bx = coarseCell % cw, bz = coarseCell / cw;
        var best = fallback;
        float bestD = float.MaxValue;
        for (int z = bz * f; z < Math.Min(n, bz * f + f); z++)
        for (int x = bx * f; x < Math.Min(n, bx * f + f); x++)
        {
            if (labels[z * n + x] != label) continue;
            var p = new Vector2(x + 0.5f, z + 0.5f);
            float d = Vector2.DistanceSquared(p, fallback);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    /// <summary>Chaikin corner cutting (2 rounds), endpoints kept.</summary>
    public static List<Vector2> Smooth(List<Vector2> pts)
    {
        var p = new List<Vector2>(pts);
        if (p.Count == 1) p.Add(p[0] + new Vector2(0.01f, 0));
        for (int it = 0; it < 2; it++)
        {
            var q = new List<Vector2>(p.Count * 2) { p[0] };
            for (int i = 0; i < p.Count - 1; i++)
            {
                q.Add(Vector2.Lerp(p[i], p[i + 1], 0.25f));
                q.Add(Vector2.Lerp(p[i], p[i + 1], 0.75f));
            }
            q.Add(p[^1]);
            p = q;
        }
        return p;
    }

    /// <summary>Bilinear height at a world position from the vertex array.</summary>
    public static float HeightAt(float[] h, int n, float x, float z)
    {
        x = Math.Clamp(x, 0, n);
        z = Math.Clamp(z, 0, n);
        int x0 = Math.Min((int)x, n - 1), z0 = Math.Min((int)z, n - 1);
        float fx = x - x0, fz = z - z0;
        int s = n + 1;
        float h00 = h[z0 * s + x0], h10 = h[z0 * s + x0 + 1], h01 = h[(z0 + 1) * s + x0], h11 = h[(z0 + 1) * s + x0 + 1];
        return (h00 * (1 - fx) + h10 * fx) * (1 - fz) + (h01 * (1 - fx) + h11 * fx) * fz;
    }

    /// <summary>Polyline extended straight past both ends, resampled at ~1 unit spacing.</summary>
    private static List<Vector2> ExtendAndResample(List<Vector2> p, float extA, float extB, int n)
    {
        var dA = p[0] - p[1];
        var dB = p[^1] - p[^2];
        if (dA.LengthSquared() < 1e-6f) dA = new Vector2(1, 0);
        if (dB.LengthSquared() < 1e-6f) dB = new Vector2(1, 0);
        var lo = new Vector2(1.5f);
        var hi = new Vector2(n - 1.5f);
        var pts = new List<Vector2>(p.Count + 2) { Vector2.Clamp(p[0] + Vector2.Normalize(dA) * extA, lo, hi) };
        pts.AddRange(p);
        pts.Add(Vector2.Clamp(p[^1] + Vector2.Normalize(dB) * extB, lo, hi));
        var s = new List<Vector2>();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            float len = Vector2.Distance(pts[i], pts[i + 1]);
            int steps = Math.Max(1, (int)MathF.Ceiling(len));
            for (int k = 0; k < steps; k++) s.Add(Vector2.Lerp(pts[i], pts[i + 1], k / (float)steps));
        }
        s.Add(pts[^1]);
        return s;
    }

    /// <summary>
    /// Carves a passable band along the polyline. The ground profile is the average of the lower and upper
    /// slope-limited envelopes of the current heights (raised at least <paramref name="minAboveWater"/> above water):
    /// where the terrain is already gentle both envelopes equal it and nothing changes; across a cliff it makes a ramp
    /// centered on the cliff (half cut into the high side, half built out from the low side); across water a causeway.
    /// The profile changes at most <see cref="CorridorSlope"/> per unit along the path. Each end is extended straight
    /// until the profile meets the terrain again (so a ramp lands on the ground instead of ending in a step). Within
    /// <paramref name="coreR"/> the ground is set to the profile; it blends back to the terrain by
    /// <paramref name="blendR"/>. Heights are sampled from the current array, so crossing corridors agree.
    /// </summary>
    public static void CarveCorridor(float[] h, int n, float water, List<Vector2> poly, float coreR, float blendR,
        CarveScratch scratch, float minAboveWater = 1.2f, float slope = CorridorSlope)
    {
        var smooth = Smooth(poly);
        float extA = 6f, extB = 6f;
        List<Vector2> s = null!;
        float[] target = [];
        for (int iter = 0; iter < 6; iter++)
        {
            s = ExtendAndResample(smooth, extA, extB, n);
            int m = s.Count;
            var baseH = new float[m];
            for (int j = 0; j < m; j++)
            {
                // Average over a small cross so single bumps don't dominate the profile.
                float c = HeightAt(h, n, s[j].X, s[j].Y);
                float avg = (c * 2f + HeightAt(h, n, s[j].X + 1.5f, s[j].Y) + HeightAt(h, n, s[j].X - 1.5f, s[j].Y)
                             + HeightAt(h, n, s[j].X, s[j].Y + 1.5f) + HeightAt(h, n, s[j].X, s[j].Y - 1.5f)) / 6f;
                baseH[j] = MathF.Max(avg, water + minAboveWater);
            }
            var lo = (float[])baseH.Clone();
            var hi = (float[])baseH.Clone();
            for (int j = 1; j < m; j++)
            {
                float st = slope * Vector2.Distance(s[j], s[j - 1]);
                lo[j] = MathF.Min(lo[j], lo[j - 1] + st);
                hi[j] = MathF.Max(hi[j], hi[j - 1] - st);
            }
            for (int j = m - 2; j >= 0; j--)
            {
                float st = slope * Vector2.Distance(s[j], s[j + 1]);
                lo[j] = MathF.Min(lo[j], lo[j + 1] + st);
                hi[j] = MathF.Max(hi[j], hi[j + 1] - st);
            }
            target = new float[m];
            for (int j = 0; j < m; j++) target[j] = (lo[j] + hi[j]) * 0.5f;
            float mA = MathF.Abs(target[0] - baseH[0]), mB = MathF.Abs(target[^1] - baseH[^1]);
            bool grow = false;
            if (mA > 0.3f && extA < 90f) { extA += mA / slope + 4f; grow = true; }
            if (mB > 0.3f && extB < 90f) { extB += mB / slope + 4f; grow = true; }
            if (!grow) break;
        }

        // Nearest-sample rasterization into the touched vertices.
        int stride = n + 1;
        var dist = scratch.Dist;
        var tgt = scratch.Target;
        var touched = scratch.Touched;
        touched.Clear();
        int r = (int)MathF.Ceiling(blendR) + 1;
        for (int j = 0; j < s.Count; j++)
        {
            var c = s[j];
            int x0 = Math.Max(0, (int)(c.X - r)), x1 = Math.Min(n, (int)(c.X + r) + 1);
            int z0 = Math.Max(0, (int)(c.Y - r)), z1 = Math.Min(n, (int)(c.Y + r) + 1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Vector2.Distance(new Vector2(x, z), c);
                if (d > blendR) continue;
                int v = z * stride + x;
                if (dist[v] == float.MaxValue) touched.Add(v);
                if (d < dist[v]) { dist[v] = d; tgt[v] = target[j]; }
            }
        }
        foreach (int v in touched)
        {
            float d = dist[v];
            float w = d <= coreR ? 1f : 1f - MapMath.SmoothStep((d - coreR) / (blendR - coreR));
            h[v] += (tgt[v] - h[v]) * w;
            dist[v] = float.MaxValue;
        }
        touched.Clear();
    }
}

/// <summary>Reusable per-generation buffers for <see cref="MapConnectivity.CarveCorridor"/>.</summary>
internal sealed class CarveScratch
{
    public readonly float[] Dist;
    public readonly float[] Target;
    public readonly List<int> Touched = new();

    public CarveScratch(int vertexCount)
    {
        Dist = new float[vertexCount];
        Target = new float[vertexCount];
        Array.Fill(Dist, float.MaxValue);
    }
}

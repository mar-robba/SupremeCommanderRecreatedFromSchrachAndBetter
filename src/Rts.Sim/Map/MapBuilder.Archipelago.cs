using System.Numerics;

namespace Rts.Sim.Map;

/// <summary>
/// Archipelago sea lanes: makes the preset playable with navies. After the start islands are flattened, the deep
/// water (ships' rule: <see cref="Terrain.MinShipDepth"/> under every cell corner) eroded to the clearance of the
/// biggest ships (<see cref="SeaClearance"/>) is labelled; every start must be served by the same big sea
/// component within <see cref="CoastMax"/> of it. Otherwise channels are dug (in all N rotated copies, so the map
/// stays symmetric) along the cheapest coarse route between the seas (water cheap, land expensive, start islands
/// avoided), and inlets are dug towards starts whose sea is too far away. Repeated until connected.
/// Only the Archipelago preset runs this (land presets use the land connectivity repair instead).
/// </summary>
internal sealed partial class MapBuilder
{
    /// <summary>Chebyshev erosion radius of the guaranteed lanes: 3 = 7x7 deep cells (clearance class 4, battleships).</summary>
    private const int SeaClearance = 3;
    /// <summary>Depth of dug channels below the water level.</summary>
    private const float ChannelDepth = 3.5f;

    /// <summary>Max distance from a start to its sea before an inlet is dug.</summary>
    private float CoastMax => 70f * MathF.Sqrt(_sc);

    private void EnsureSeaLanes()
    {
        int cells = _n * _n;
        var sea = new byte[cells];
        var ok = new byte[cells];
        var labels = new int[cells];
        var sizes = new List<int>();
        int minSize = Math.Max(400, cells / 100);
        const int maxRounds = 5;
        for (int round = 0; round <= maxRounds; round++)
        {
            ComputeSea(sea);
            MapConnectivity.Erode(sea, _n, SeaClearance, ok);
            MapConnectivity.Label(ok, _n, labels, sizes);
            var comp = new int[_starts.Length];
            var dist = new float[_starts.Length];
            var near = new Vector2[_starts.Length];
            for (int s = 0; s < _starts.Length; s++)
                (comp[s], dist[s], near[s]) = NearestSea(labels, sizes, minSize, _starts[s]);

            bool connected = comp[0] >= 0 && comp.All(c => c == comp[0]);
            bool coastOk = dist.All(d => d <= CoastMax);
            if ((connected && coastOk) || round == maxRounds) return;

            // Inlet towards start 0 if its sea is too far (symmetric copies serve the other starts).
            if (comp[0] >= 0 && dist[0] > CoastMax)
            {
                var dir = Vector2.Normalize(near[0] - _starts[0]);
                CarveChannelSymmetric([near[0], _starts[0] + dir * (_flatR + 12f)]);
                continue;
            }
            if (comp[0] < 0)
            {
                // No big sea at all near start 0 (extreme settings): dig towards the map center.
                CarveChannelSymmetric([_starts[0] + Vector2.Normalize(_center - _starts[0]) * (_flatR + 12f), _center]);
                continue;
            }

            var targets = new List<int>();
            foreach (int c in comp)
                if (c >= 0 && c != comp[0] && !targets.Contains(c)) targets.Add(c);
            var paths = FindSeaPaths(labels, comp[0], targets);
            if (paths.Count == 0)
            {
                CarveChannelSymmetric([near[0], _center]);
                continue;
            }
            foreach (var t in targets)
                if (paths.TryGetValue(t, out var pts)) CarveChannelSymmetric(pts);
        }
    }

    /// <summary>1 where ships can sail (deepest-corner rule of <see cref="PassFlags.DeepWater"/>, small margin).</summary>
    private void ComputeSea(byte[] sea)
    {
        float limit = _w - Terrain.MinShipDepth - 0.1f;
        Parallel.For(0, _n, cz =>
        {
            int row = cz * _stride;
            for (int cx = 0; cx < _n; cx++)
            {
                float m = MathF.Max(MathF.Max(_h[row + cx], _h[row + cx + 1]), MathF.Max(_h[row + _stride + cx], _h[row + _stride + cx + 1]));
                sea[cz * _n + cx] = m < limit ? (byte)1 : (byte)0;
            }
        });
    }

    /// <summary>Nearest cell of a big (≥ minSize) sea component to p: (label, distance, cell center); (-1, ∞, p) if none.</summary>
    private (int label, float dist, Vector2 at) NearestSea(int[] labels, List<int> sizes, int minSize, Vector2 p)
    {
        int best = -1;
        float bestD = float.MaxValue;
        var at = p;
        for (int z = 0; z < _n; z++)
        for (int x = 0; x < _n; x++)
        {
            int l = labels[z * _n + x];
            if (l < 0 || sizes[l] < minSize) continue;
            var c = new Vector2(x + 0.5f, z + 0.5f);
            float d = Vector2.DistanceSquared(c, p);
            if (d < bestD) { bestD = d; best = l; at = c; }
        }
        return (best, best >= 0 ? MathF.Sqrt(bestD) : float.MaxValue, at);
    }

    /// <summary>
    /// Coarse multi-source Dijkstra from the main sea component to each target component: deep water is cheap,
    /// shallows cost more, land a lot (digging), start islands are avoided. Returns world-space polylines.
    /// </summary>
    private Dictionary<int, List<Vector2>> FindSeaPaths(int[] labels, int main, List<int> targets)
    {
        int f = Math.Max(2, _n / 128);
        int cw = (_n + f - 1) / f;
        int cn = cw * cw;
        var cost = new float[cn];
        var hasMain = new bool[cn];
        float avoid = _blendR + 8f;
        for (int bz = 0; bz < cw; bz++)
        for (int bx = 0; bx < cw; bx++)
        {
            int dry = 0, shallow = 0, total = 0;
            bool m = false;
            for (int z = bz * f; z < Math.Min(_n, bz * f + f); z++)
            for (int x = bx * f; x < Math.Min(_n, bx * f + f); x++)
            {
                total++;
                float hv = _h[V(x, z)];
                if (hv >= _w) dry++;
                else if (hv > _w - Terrain.MinShipDepth - 0.5f) shallow++;
                if (labels[z * _n + x] == main) m = true;
            }
            var center = new Vector2(bx * f + f * 0.5f, bz * f + f * 0.5f);
            float c = 1f + 12f * dry / Math.Max(1, total) + 3f * shallow / Math.Max(1, total);
            if (DistToNearestStart(center) < avoid) c += 400f;
            cost[bz * cw + bx] = c;
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
                float nd = d + (cost[c] + cost[nb]) * 0.5f * (dx != 0 && dz != 0 ? 1.4142f : 1f);
                if (nd < dist[nb]) { dist[nb] = nd; parent[nb] = c; pq.Enqueue(nb, nd); }
            }
        }

        var result = new Dictionary<int, List<Vector2>>();
        foreach (int t in targets)
        {
            int bestCell = -1;
            var bestPt = Vector2.Zero;
            for (int z = 0; z < _n; z++)
            for (int x = 0; x < _n; x++)
            {
                if (labels[z * _n + x] != t) continue;
                int cc = (z / f) * cw + x / f;
                if (dist[cc] == float.MaxValue) continue;
                if (bestCell < 0 || dist[cc] < dist[bestCell] || (dist[cc] == dist[bestCell] && cc < bestCell))
                {
                    bestCell = cc;
                    bestPt = new Vector2(x + 0.5f, z + 0.5f);
                }
            }
            if (bestCell < 0) continue;
            var pts = new List<Vector2>();
            for (int c = bestCell; c >= 0; c = parent[c]) pts.Add(new Vector2((c % cw) * f + f * 0.5f, (c / cw) * f + f * 0.5f));
            pts.Reverse(); // main -> target
            pts.Add(bestPt);
            result[t] = pts;
        }
        return result;
    }

    private void CarveChannelSymmetric(List<Vector2> poly)
    {
        float k = MathF.Sqrt(_sc);
        for (int r = 0; r < _sym.N; r++)
        {
            var rp = new List<Vector2>(poly.Count);
            foreach (var p in poly) rp.Add(Vector2.Clamp(_sym.Rotate(p, r), new Vector2(1.5f), new Vector2(_n - 1.5f)));
            CarveChannel(rp, 6f, 6f + 9f * k, _w - ChannelDepth);
        }
    }

    /// <summary>Lowers the terrain along the polyline to <paramref name="bottom"/> within coreR, blending back to the
    /// original ground by blendR. Only ever lowers, so crossing channels agree.</summary>
    private void CarveChannel(List<Vector2> poly, float coreR, float blendR, float bottom)
    {
        var s = new List<Vector2>();
        var smooth = MapConnectivity.Smooth(poly);
        for (int i = 0; i < smooth.Count - 1; i++)
        {
            float len = Vector2.Distance(smooth[i], smooth[i + 1]);
            int steps = Math.Max(1, (int)MathF.Ceiling(len));
            for (int j = 0; j < steps; j++) s.Add(Vector2.Lerp(smooth[i], smooth[i + 1], j / (float)steps));
        }
        s.Add(smooth[^1]);

        var dist = _scratch.Dist;
        var touched = _scratch.Touched;
        touched.Clear();
        int r = (int)MathF.Ceiling(blendR) + 1;
        foreach (var c in s)
        {
            int x0 = Math.Max(0, (int)(c.X - r)), x1 = Math.Min(_n, (int)(c.X + r) + 1);
            int z0 = Math.Max(0, (int)(c.Y - r)), z1 = Math.Min(_n, (int)(c.Y + r) + 1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Vector2.Distance(new Vector2(x, z), c);
                if (d > blendR) continue;
                int v = V(x, z);
                if (dist[v] == float.MaxValue) touched.Add(v);
                if (d < dist[v]) dist[v] = d;
            }
        }
        foreach (int v in touched)
        {
            float d = dist[v];
            float wgt = d <= coreR ? 1f : 1f - MapMath.SmoothStep((d - coreR) / (blendR - coreR));
            float target = _h[v] + (bottom - _h[v]) * wgt;
            if (target < _h[v]) _h[v] = target;
            dist[v] = float.MaxValue;
        }
        touched.Clear();
    }
}

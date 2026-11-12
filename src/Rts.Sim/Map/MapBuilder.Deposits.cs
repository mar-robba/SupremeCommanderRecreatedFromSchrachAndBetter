using System.Numerics;
using Rts.Sim.Core;

namespace Rts.Sim.Map;

/// <summary>
/// Deposit placement: per player a base cluster (mass ring + one hydrocarbon inside the flat start area), expansion
/// clusters (2–4 mass, sometimes a hydrocarbon) and scattered single mass deposits. Candidates are drawn in player 0's
/// sector and accepted only if all N rotated copies sit on flat, dry land of the main land component (any big
/// island on Archipelago), away from starts and other deposits. Positions are whole numbers so structures snap
/// exactly onto them.
/// </summary>
internal sealed partial class MapBuilder
{
    private byte[] _depLand = [];
    private int[] _depLabels = [];
    private bool[] _depAllowed = [];

    private List<Deposit> PlaceDeposits()
    {
        int cells = _n * _n;
        _depLand = new byte[cells];
        var ok = new byte[cells];
        _depLabels = new int[cells];
        var sizes = new List<int>();
        MapConnectivity.ComputeLand(_h, _n, _w, _depLand);
        MapConnectivity.Erode(_depLand, _n, 1, ok);
        int count = MapConnectivity.Label(ok, _n, _depLabels, sizes);
        _depAllowed = new bool[count];
        if (LandPreset)
        {
            int main = MapConnectivity.LabelNear(_depLabels, _n, _starts[0], 8);
            if (main >= 0) _depAllowed[main] = true;
        }
        else
        {
            int minIsland = Math.Max(120, (int)(cells * 0.002f));
            for (int c = 0; c < count; c++) _depAllowed[c] = sizes[c] >= minIsland;
        }

        var deps = new List<Deposit>();
        var rng = new DetRandom(_seed ^ 0xD3B0517UL);
        int N = _sym.N;
        var s0 = _starts[0];

        // Base: same layout for everybody (rotated copies of player 0's).
        float baseAng = DMath.HeadingOf(_center - s0) - DMath.Pi * 0.25f;
        Vector2 Base(Vector2 off) => s0 + Rot(off, baseAng);
        int baseMass = Math.Max(0, _s.MassDepositsPerPlayerBase);
        for (int i = 0; i < baseMass; i++)
        {
            var p = Base(DMath.Dir(i * DMath.TwoPi / baseMass + 0.4f) * 12f);
            for (int k = 0; k < N; k++) deps.Add(new Deposit(Round(_sym.Rotate(p, k)), DepositType.Mass));
        }
        var hp = Base(new Vector2(-16f, 9f));
        for (int k = 0; k < N; k++) deps.Add(new Deposit(Round(_sym.Rotate(hp, k)), DepositType.Hydrocarbon));

        float areaPerPlayer = _n * (float)_n / N;
        int autoClusters = Math.Clamp((int)MathF.Round(MathF.Sqrt(areaPerPlayer) / 70f), 2, 7);
        int clusters = _s.ExpansionsPerPlayer >= 0 ? _s.ExpansionsPerPlayer : autoClusters;
        int autoSingles = Math.Clamp((int)MathF.Round(MathF.Sqrt(areaPerPlayer) / 60f), 1, 8);
        int singles = _s.ExtraMassDepositPairs >= 0 ? _s.ExtraMassDepositPairs : autoSingles;

        // Expansion clusters.
        int placed = 0;
        var offs = new List<(Vector2 off, DepositType type)>();
        for (int attempt = 0; placed < clusters && attempt < 400 * Math.Max(1, clusters); attempt++)
        {
            var q = SectorSample(ref rng, 0.10f, 0.62f);
            int m = rng.Range(2, 5);
            bool hydro = placed == 0 || rng.Chance(0.35f);
            float rot = rng.Range(0f, DMath.TwoPi);
            offs.Clear();
            for (int j = 0; j < m; j++) offs.Add((DMath.Dir(rot + j * DMath.TwoPi / m) * rng.Range(4.5f, 6f), DepositType.Mass));
            if (hydro) offs.Add((DMath.Dir(rot + DMath.Pi / m) * 11f, DepositType.Hydrocarbon));
            if (TryPlaceGroup(deps, q, offs, minStartDist: _flatR + 28f, minDepositDist: 26f, copySpacing: 40f)) placed++;
        }

        // Scattered single mass deposits.
        placed = 0;
        for (int attempt = 0; placed < singles && attempt < 300 * Math.Max(1, singles); attempt++)
        {
            var q = SectorSample(ref rng, 0.06f, 0.66f);
            offs.Clear();
            offs.Add((Vector2.Zero, DepositType.Mass));
            if (TryPlaceGroup(deps, q, offs, minStartDist: _flatR + 16f, minDepositDist: 18f, copySpacing: 24f)) placed++;
        }
        return deps;
    }

    private static Vector2 Rot(Vector2 v, float ang)
    {
        float c = DMath.Cos(ang), s = DMath.Sin(ang);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    private static Vector2 Round(Vector2 p) => new(MathF.Round(p.X), MathF.Round(p.Y));

    /// <summary>Random point in player 0's sector between radius fractions lo..hi of the map size.</summary>
    private Vector2 SectorSample(ref DetRandom rng, float lo, float hi)
    {
        float half = DMath.Pi / _sym.N;
        float ang = _sym.Phi0 + rng.Range(-half, half) * 0.95f;
        float rad = rng.Range(lo, hi) * _n;
        return _center + DMath.Dir(ang) * rad;
    }

    private bool TryPlaceGroup(List<Deposit> deps, Vector2 q, List<(Vector2 off, DepositType type)> offs,
        float minStartDist, float minDepositDist, float copySpacing)
    {
        int N = _sym.N;
        var add = new List<Deposit>(N * offs.Count);
        for (int k = 0; k < N; k++)
        {
            var c = _sym.Rotate(q, k);
            if (DistToNearestStart(c) < minStartDist) return false;
            foreach (var d in deps)
                if (Vector2.DistanceSquared(d.Pos, c) < minDepositDist * minDepositDist) return false;
            for (int j = 0; j < k; j++)
                if (Vector2.DistanceSquared(_sym.Rotate(q, j), c) < copySpacing * copySpacing) return false;
            foreach (var (off, type) in offs)
            {
                var p = Round(_sym.Rotate(q + off, k));
                if (!SpotOk(p, type == DepositType.Mass ? 2 : 3)) return false;
                foreach (var d in add)
                    if (Vector2.DistanceSquared(d.Pos, p) < 6.5f * 6.5f) return false;
                add.Add(new Deposit(p, type));
            }
        }
        deps.AddRange(add);
        return true;
    }

    /// <summary>Flat (height range ≤ 1.2), dry, allowed-component land in the square of radius r around p.</summary>
    private bool SpotOk(Vector2 p, int r)
    {
        int cx = (int)MathF.Floor(p.X), cz = (int)MathF.Floor(p.Y);
        const int margin = 5;
        if (cx - r < margin || cz - r < margin || cx + r >= _n - margin || cz + r >= _n - margin) return false;
        for (int z = cz - r; z <= cz + r; z++)
        for (int x = cx - r; x <= cx + r; x++)
        {
            int c = z * _n + x;
            if (_depLand[c] == 0) return false;
            int l = _depLabels[c];
            if (l < 0 || !_depAllowed[l]) return false;
        }
        float mn = float.MaxValue, mx = float.MinValue;
        for (int z = cz - r; z <= cz + r + 1; z++)
        for (int x = cx - r; x <= cx + r + 1; x++)
        {
            float v = _h[V(x, z)];
            mn = MathF.Min(mn, v);
            mx = MathF.Max(mx, v);
        }
        return mx - mn <= 1.2f;
    }

    /// <summary>Flattens a small pad under each deposit (blended) so extractors sit level.</summary>
    private void FlattenPads(List<Deposit> deps)
    {
        foreach (var d in deps)
        {
            float r = d.Type == DepositType.Mass ? 2.2f : 3.2f;
            float R = r + 2.5f;
            float sum = 0;
            int cnt = 0;
            for (float dz = -r; dz <= r; dz += 1f)
            for (float dx = -r; dx <= r; dx += 1f)
            {
                sum += MapConnectivity.HeightAt(_h, _n, d.Pos.X + dx, d.Pos.Y + dz);
                cnt++;
            }
            float target = MathF.Max(sum / cnt, _w + 0.6f);
            int x0 = Math.Max(0, (int)(d.Pos.X - R)), x1 = Math.Min(_n, (int)(d.Pos.X + R) + 1);
            int z0 = Math.Max(0, (int)(d.Pos.Y - R)), z1 = Math.Min(_n, (int)(d.Pos.Y + R) + 1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, z), d.Pos);
                if (dist >= R) continue;
                float t = dist <= r ? 1f : 1f - MapMath.SmoothStep((dist - r) / (R - r));
                int v = V(x, z);
                _h[v] += (target - _h[v]) * t;
            }
        }
    }

    /// <summary>True if the extractor footprint at the deposit is dry, gentle land.</summary>
    private static bool DepositFits(Terrain t, Deposit d)
    {
        int size = d.Type == DepositType.Mass ? 2 : 4;
        int x0 = (int)MathF.Round(d.Pos.X - size * 0.5f), z0 = (int)MathF.Round(d.Pos.Y - size * 0.5f);
        for (int z = z0; z < z0 + size; z++)
        for (int x = x0; x < x0 + size; x++)
            if ((t.PassAt(x, z) & PassFlags.Land) == 0) return false;
        return true;
    }
}

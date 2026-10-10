using System.Numerics;
using Rts.Sim.Core;

namespace Rts.Sim.Map;

public sealed class MapGenSettings
{
    /// <summary>Map size in world units (SupCom: 256 = 5 km, 512 = 10 km, 1024 = 20 km).</summary>
    public int Size = 256;
    public int Players = 2;
    public int Seed = 1;
    public float MaxHeight = 28f;
    public float WaterLevel = 6f;
    /// <summary>Feature size of hills in world units.</summary>
    public float NoiseScale = 70f;
    public int MassDepositsPerPlayerBase = 4;
    public int ExtraMassDepositPairs = 8;
}

/// <summary>
/// Deterministic procedural map generator: point-symmetric terrain (fair for 2 players), flattened start
/// plateaus, mass deposits around each start plus scattered symmetric pairs, hydrocarbon deposits.
/// </summary>
public static class MapGenerator
{
    public static Terrain Generate(MapGenSettings s)
    {
        int n = s.Size;
        int stride = n + 1;
        var h = new float[stride * stride];
        ulong seed = (ulong)s.Seed * 0x2545F4914F6CDD1DUL + 17;

        var starts = StartPositions(n, s.Players);

        for (int z = 0; z <= n; z++)
        for (int x = 0; x <= n; x++)
        {
            // Point symmetry: average the field with its 180-degree rotation.
            float a = Field(seed, x, z, s);
            float b = Field(seed, n - x, n - z, s);
            h[z * stride + x] = 0.5f * (a + b);
        }

        // Flatten start areas to a buildable plateau above water.
        float plateau = MathF.Max(s.WaterLevel + 4f, s.MaxHeight * 0.45f);
        const float flatR = 22f, blendR = 40f;
        foreach (var sp in starts)
        {
            for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, z), sp);
                if (d >= blendR) continue;
                float t = d <= flatR ? 1f : 1f - SmoothStep((d - flatR) / (blendR - flatR));
                ref float hv = ref h[z * stride + x];
                hv = hv + (plateau - hv) * t;
            }
        }

        var deposits = new List<Deposit>();
        var rng = new DetRandom(seed ^ 0xD3B0517UL);
        // Base mass deposits around each start (same pattern rotated per player).
        for (int i = 0; i < s.MassDepositsPerPlayerBase; i++)
        {
            float ang = i * DMath.TwoPi / s.MassDepositsPerPlayerBase + 0.4f;
            var off = DMath.Dir(ang) * 12f;
            foreach (var sp in starts) deposits.Add(new Deposit(sp + Rotate(off, sp, n), DepositType.Mass));
        }
        // One hydrocarbon per player near base.
        foreach (var sp in starts) deposits.Add(new Deposit(sp + Rotate(new Vector2(-16f, 9f), sp, n), DepositType.Hydrocarbon));

        // Extra symmetric mass deposit pairs on dry, gentle land.
        int tries = 0, placed = 0;
        var tmp = new Terrain(n, n, h, s.WaterLevel, [], starts, s.Seed);
        while (placed < s.ExtraMassDepositPairs && tries++ < 2000)
        {
            var p = new Vector2(rng.Range(16f, n - 16f), rng.Range(16f, n - 16f));
            var q = new Vector2(n - p.X, n - p.Y);
            if (!IsGoodSpot(tmp, p) || !IsGoodSpot(tmp, q)) continue;
            bool tooClose = false;
            foreach (var d in deposits)
                if (Vector2.DistanceSquared(d.Pos, p) < 18f * 18f || Vector2.DistanceSquared(d.Pos, q) < 18f * 18f) { tooClose = true; break; }
            foreach (var sp in starts)
                if (Vector2.DistanceSquared(sp, p) < 45f * 45f || Vector2.DistanceSquared(sp, q) < 45f * 45f) { tooClose = true; break; }
            if (tooClose) continue;
            deposits.Add(new Deposit(p, DepositType.Mass));
            deposits.Add(new Deposit(q, DepositType.Mass));
            placed++;
        }

        // Flatten a small pad under each deposit so structures fit.
        foreach (var d in deposits)
        {
            int cx = (int)d.Pos.X, cz = (int)d.Pos.Y;
            float target = MathF.Max(tmp.GetHeight(d.Pos), s.WaterLevel + 0.5f);
            for (int z = cz - 3; z <= cz + 3; z++)
            for (int x = cx - 3; x <= cx + 3; x++)
                if (x >= 0 && z >= 0 && x <= n && z <= n) h[z * stride + x] = target;
        }

        return new Terrain(n, n, h, s.WaterLevel, deposits.ToArray(), starts, s.Seed);
    }

    private static bool IsGoodSpot(Terrain t, Vector2 p)
    {
        int cx = (int)p.X, cz = (int)p.Y;
        for (int z = cz - 2; z <= cz + 2; z++)
        for (int x = cx - 2; x <= cx + 2; x++)
            if ((t.PassAt(x, z) & PassFlags.Land) == 0) return false;
        return true;
    }

    /// <summary>Rotates an offset so each player's base layout mirrors the others (towards map center).</summary>
    private static Vector2 Rotate(Vector2 off, Vector2 start, int n)
    {
        var toCenter = new Vector2(n * 0.5f, n * 0.5f) - start;
        float ang = DMath.HeadingOf(toCenter) - DMath.Pi * 0.25f;
        float c = MathF.Cos(ang), s = MathF.Sin(ang);
        return new Vector2(off.X * c - off.Y * s, off.X * s + off.Y * c);
    }

    public static Vector2[] StartPositions(int n, int players)
    {
        float lo = n * 0.15f, hi = n * 0.85f;
        return players switch
        {
            <= 2 => [new(lo, lo), new(hi, hi)],
            3 => [new(lo, lo), new(hi, hi), new(lo, hi)],
            4 => [new(lo, lo), new(hi, hi), new(lo, hi), new(hi, lo)],
            _ => Enumerable.Range(0, players).Select(i =>
            {
                var d = DMath.Dir(i * DMath.TwoPi / players + DMath.Pi * 1.25f);
                return new Vector2(n * 0.5f, n * 0.5f) + d * (n * 0.38f);
            }).ToArray(),
        };
    }

    private static float Field(ulong seed, float x, float z, MapGenSettings s)
    {
        float f = 0, amp = 1, freq = 1f / s.NoiseScale, norm = 0;
        for (int o = 0; o < 5; o++)
        {
            f += amp * ValueNoise(seed + (ulong)o * 1013, x * freq, z * freq);
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        f /= norm; // ~[0,1]
        // Soft terraces give SupCom-like plateaus and ramps.
        float terr = f * 5f;
        float fl = MathF.Floor(terr);
        float fr = terr - fl;
        terr = fl + SmoothStep(DMath.Clamp01((fr - 0.35f) / 0.3f));
        f = 0.55f * f + 0.45f * (terr / 5f);
        return f * s.MaxHeight;
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);

    private static float Hash01(ulong seed, int x, int z) =>
        (DetRandom.Mix(seed ^ ((ulong)(uint)x * 0x9E3779B1UL) ^ ((ulong)(uint)z << 32)) >> 40) * (1f / (1 << 24));

    private static float ValueNoise(ulong seed, float x, float z)
    {
        int x0 = (int)MathF.Floor(x), z0 = (int)MathF.Floor(z);
        float fx = SmoothStep(x - x0), fz = SmoothStep(z - z0);
        float a = Hash01(seed, x0, z0), b = Hash01(seed, x0 + 1, z0);
        float c = Hash01(seed, x0, z0 + 1), d = Hash01(seed, x0 + 1, z0 + 1);
        return (a + (b - a) * fx) * (1 - fz) + (c + (d - c) * fx) * fz;
    }
}

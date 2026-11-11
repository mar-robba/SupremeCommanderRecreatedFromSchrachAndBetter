using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Sim.Map;

/// <summary>A tree or rock placed by the map generator (spawned into <see cref="PropStore"/> at game start).</summary>
public readonly record struct MapProp(PropKind Kind, Vector2 Pos, float Heading, byte Variant, float Radius, float Mass, float Energy);

/// <summary>
/// Map props (wave 8b): forests (trees: energy and a little mass) and rock fields (rocks: mass). Placement is a pure
/// function of the final terrain + seed: candidates on a jittered grid inside player 0's sector (the
/// <see cref="MapSymmetry"/> template), kept by noise-shaped forest / rock-field densities, then replicated in all N
/// rotations and accepted only if EVERY copy is valid (dry, walkable land; off the start plateaus, deposits and map
/// border; spaced from accepted props), so every player gets the same props. Density is per area, so a 1024 map has
/// ~16× the props of a 256 map. Single-threaded and deterministic (DetRandom, fixed iteration order).
/// </summary>
public static class MapProps
{
    // Reclaim values (SupCom-like: trees are mostly energy, rocks are mass).
    public const float TreeMassPerSize = 0.4f;
    public const float TreeEnergyPerSize = 10f;
    public const float RockMassPerArea = 5f;
    public const int TreeVariants = 4; // 0,1 conifers, 2,3 broadleaf
    public const int RockVariants = 4;

    private const float Step = 1.7f;          // candidate grid spacing (densest forest)
    private const float TreeSpacing = 1.25f;  // min distance between trees
    private const float BorderMargin = 3f;

    /// <summary>Per-preset multipliers of tree and rock density.</summary>
    private static (float trees, float rocks) PresetFactors(MapPreset p) => p switch
    {
        MapPreset.Lakes => (1.2f, 0.6f),
        MapPreset.Highlands => (0.85f, 1.5f),
        MapPreset.Canyon => (0.35f, 2.0f),
        MapPreset.Archipelago => (1.1f, 0.8f),
        _ => (1f, 0.8f),
    };

    internal static MapProp[] Place(Terrain t, MapSymmetry sym, ulong seed, MapPreset preset, float startClear)
    {
        int n = t.Width;
        var (treeF, rockF) = PresetFactors(preset);
        var forest = new MapNoise(seed ^ 0x7EE5_F0E5_7000_0001UL);
        var forest2 = new MapNoise(seed ^ 0x7EE5_F0E5_7000_0002UL);
        var rockField = new MapNoise(seed ^ 0x40C4_F1E1_D000_0003UL);
        var rng = new DetRandom(DetRandom.Mix(seed ^ 0x9A0B_5EED_0000_0004UL));
        float clear = startClear + 6f;
        int N = sym.N;

        var result = new List<MapProp>();
        // Spatial hash of accepted props (cell 2) for spacing checks.
        int hw = n / 2 + 2;
        var hash = new List<int>?[hw * hw];
        var copies = new Vector2[N];

        int steps = (int)(n / Step);
        for (int gz = 0; gz < steps; gz++)
        for (int gx = 0; gx < steps; gx++)
        {
            // Jitter first: the random stream advances the same way for every grid point (order-independent of terrain).
            float jx = rng.NextFloat(), jz = rng.NextFloat(), roll = rng.NextFloat(), r1 = rng.NextFloat(), r2 = rng.NextFloat();
            var p = new Vector2((gx + jx) * Step, (gz + jz) * Step);
            if (sym.SectorOf(p, out _, out _) != 0) continue;

            // Densities in the template frame (symmetric by construction).
            float f = forest.Fbm(p.X / 48f, p.Y / 48f, 3) + 0.35f * forest2.Fbm(p.X / 14f, p.Y / 14f, 2);
            float treeD = (0.42f * MapMath.SmoothStep(0.17f, 0.31f, f) + 0.004f) * treeF;
            float g = rockField.Fbm(p.X / 30f, p.Y / 30f, 3);
            float rockD = (0.12f * MapMath.SmoothStep(0.20f, 0.30f, g) + 0.002f) * rockF;
            PropKind kind;
            if (roll < treeD) kind = PropKind.Tree;
            else if (roll < treeD + rockD) kind = PropKind.Rock;
            else continue;

            float size, radius, mass, energy;
            byte variant;
            if (kind == PropKind.Tree)
            {
                size = 0.75f + 0.6f * r1;
                radius = 0.45f * size;
                mass = TreeMassPerSize * size;
                energy = TreeEnergyPerSize * size;
                // Conifers in the cool / high parts, broadleaf elsewhere; mixed at the edges.
                float hn = (t.GetHeight(p) - t.WaterLevel) / 30f + (forest2.Fbm(p.X / 60f, p.Y / 60f, 2) * 2f);
                variant = (byte)((hn + (r2 - 0.5f) * 0.6f > 0.35f ? 0 : 2) + (r2 < 0.5f ? 0 : 1));
            }
            else
            {
                size = 0.7f + 1.5f * r1 * r1;
                radius = size;
                mass = RockMassPerArea * size * size;
                energy = 0;
                variant = (byte)(r2 * RockVariants);
            }
            float heading = (r1 * 7.31f + r2 * 3.17f) % MathF.Tau;

            bool ok = true;
            for (int k = 0; k < N && ok; k++)
            {
                var c = copies[k] = k == 0 ? p : sym.Rotate(p, k);
                ok = Fits(t, c, kind, radius, clear) && Spaced(hash, hw, result, c, kind, radius);
                for (int j = 0; j < k && ok; j++) ok = Vector2.DistanceSquared(copies[j], c) > 4f * radius * radius + 1f;
            }
            if (!ok) continue;
            for (int k = 0; k < N; k++)
            {
                var c = copies[k];
                int cell = (int)(c.Y * 0.5f) * hw + (int)(c.X * 0.5f);
                (hash[cell] ??= new List<int>(4)).Add(result.Count);
                float hk = DMath.WrapAngle(heading + k * MathF.Tau / N);
                result.Add(new MapProp(kind, c, hk, variant, radius, mass, energy));
            }
        }
        return result.ToArray();
    }

    /// <summary>Dry walkable land (whole footprint), away from the border, the start plateaus and deposits.</summary>
    private static bool Fits(Terrain t, Vector2 c, PropKind kind, float radius, float clear)
    {
        int n = t.Width;
        if (c.X < BorderMargin || c.Y < BorderMargin || c.X > n - BorderMargin || c.Y > t.Height - BorderMargin) return false;
        float r = kind == PropKind.Rock ? radius * 0.7f : 0.3f;
        for (int s = 0; s < 5; s++)
        {
            var q = s switch { 0 => c, 1 => c + new Vector2(r, 0), 2 => c - new Vector2(r, 0), 3 => c + new Vector2(0, r), _ => c - new Vector2(0, r) };
            int cx = (int)q.X, cz = (int)q.Y;
            if ((t.PassAt(cx, cz) & PassFlags.Land) == 0) return false;
            if (t.GetHeight(q) < t.WaterLevel + 0.4f) return false;
        }
        foreach (var sp in t.StartPositions)
            if (Vector2.DistanceSquared(sp, c) < clear * clear) return false;
        foreach (var d in t.Deposits)
        {
            float keep = (d.Type == DepositType.Mass ? 3.5f : 5f) + radius;
            if (Vector2.DistanceSquared(d.Pos, c) < keep * keep) return false;
        }
        return true;
    }

    private static bool Spaced(List<int>?[] hash, int hw, List<MapProp> placed, Vector2 c, PropKind kind, float radius)
    {
        int cx = (int)(c.X * 0.5f), cz = (int)(c.Y * 0.5f);
        for (int z = cz - 2; z <= cz + 2; z++)
        for (int x = cx - 2; x <= cx + 2; x++)
        {
            if ((uint)x >= (uint)hw || (uint)z >= (uint)hw) continue;
            var list = hash[z * hw + x];
            if (list == null) continue;
            foreach (int idx in list)
            {
                var o = placed[idx];
                float min = kind == PropKind.Tree && o.Kind == PropKind.Tree ? TreeSpacing : radius + o.Radius + 0.3f;
                if (Vector2.DistanceSquared(o.Pos, c) < min * min) return false;
            }
        }
        return true;
    }

    /// <summary>Spawns the terrain's generator props into the world (game start, sequential). Returns how many.</summary>
    public static int SpawnInto(SimWorld w)
    {
        int count = 0;
        foreach (var p in w.Terrain.Props)
            if (w.Props.SpawnNature(p.Kind, p.Pos, p.Heading, p.Variant, p.Radius, p.Mass, p.Energy) >= 0) count++;
        return count;
    }
}

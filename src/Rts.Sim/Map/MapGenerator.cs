using System.Numerics;
using Rts.Sim.Core;

namespace Rts.Sim.Map;

/// <summary>Map generator presets (terrain style).</summary>
public enum MapPreset
{
    /// <summary>Open rolling land, a few ponds.</summary>
    Plains,
    /// <summary>Hills with many lakes and ponds; fords and land bridges between them.</summary>
    Lakes,
    /// <summary>Terraced plateaus with cliffs; ramps connect the levels.</summary>
    Highlands,
    /// <summary>High impassable mesas cut by valleys: chokepoints between bases.</summary>
    Canyon,
    /// <summary>Islands in a sea. Bases are NOT connected by land (needs naval/air units).</summary>
    Archipelago,
}

/// <summary>Display information about the presets (UI) and per-preset defaults.</summary>
public static class MapPresets
{
    public static readonly MapPreset[] All = [MapPreset.Plains, MapPreset.Lakes, MapPreset.Highlands, MapPreset.Canyon, MapPreset.Archipelago];

    public static string Name(MapPreset p) => p switch
    {
        MapPreset.Plains => "Plains",
        MapPreset.Lakes => "Lakes",
        MapPreset.Highlands => "Highlands",
        MapPreset.Canyon => "Canyon",
        MapPreset.Archipelago => "Archipelago",
        _ => p.ToString(),
    };

    public static string Description(MapPreset p) => p switch
    {
        MapPreset.Plains => "Open rolling land with a few ponds. Fast, direct attacks.",
        MapPreset.Lakes => "Hills and many lakes; armies funnel through land bridges and fords.",
        MapPreset.Highlands => "Terraced plateaus ringed by cliffs. Ramps are the only way up.",
        MapPreset.Canyon => "Impassable mesas cut by valleys: narrow chokepoints between bases.",
        MapPreset.Archipelago => "Islands in open sea. Bases are not connected by land: needs naval units (not available yet).",
        _ => "",
    };

    /// <summary>True if the bases are not guaranteed to be connected by land.</summary>
    public static bool NeedsNaval(MapPreset p) => p == MapPreset.Archipelago;

    /// <summary>Default <see cref="MapGenSettings.WaterAmount"/> of the preset (0..1).</summary>
    public static float DefaultWater(MapPreset p) => p switch
    {
        MapPreset.Plains => 0.15f,
        MapPreset.Lakes => 0.5f,
        MapPreset.Highlands => 0.2f,
        MapPreset.Canyon => 0.1f,
        MapPreset.Archipelago => 0.55f,
        _ => 0.2f,
    };

    public static bool TryParse(string s, out MapPreset preset)
    {
        foreach (var p in All)
            if (string.Equals(Name(p), s, StringComparison.OrdinalIgnoreCase)) { preset = p; return true; }
        preset = MapPreset.Plains;
        return false;
    }
}

public sealed class MapGenSettings
{
    /// <summary>Map size in world units (SupCom: 256 = 5 km, 512 = 10 km, 1024 = 20 km).</summary>
    public int Size = 256;
    /// <summary>Number of start positions (2..8). The terrain is N-fold rotationally symmetric.</summary>
    public int Players = 2;
    public int Seed = 1;
    public MapPreset Preset = MapPreset.Plains;
    /// <summary>0 (dry) .. 1 (very wet): lake coverage, or sea coverage on Archipelago. Negative = preset default.</summary>
    public float WaterAmount = -1;
    /// <summary>Relief scale: 28 = normal; heights of hills/plateaus/mesas scale with MaxHeight / 28.</summary>
    public float MaxHeight = 28f;
    public float WaterLevel = 6f;
    /// <summary>Feature size of hills in world units at 256 (grows with sqrt of the map size).</summary>
    public float NoiseScale = 70f;
    /// <summary>Mass deposits in each start area.</summary>
    public int MassDepositsPerPlayerBase = 4;
    /// <summary>Scattered single mass deposits per player (outside the base and expansion clusters); -1 = auto (by map area).</summary>
    public int ExtraMassDepositPairs = -1;
    /// <summary>Expansion clusters (2–4 mass, sometimes a hydrocarbon) per player; -1 = auto (by map area).</summary>
    public int ExpansionsPerPlayer = -1;
}

/// <summary>
/// Deterministic procedural map generator with presets (<see cref="MapPreset"/>). Terrain, features and deposits are
/// N-fold rotationally symmetric for N players (fair starts), start areas are flat and buildable, and on every preset
/// except Archipelago every start position is connected by land to every other one (checked with a flood fill over
/// <see cref="Terrain.CanTraverse"/> and repaired by carving ramps/causeways if needed). See <c>MapBuilder</c>.
/// </summary>
public static class MapGenerator
{
    public static Terrain Generate(MapGenSettings s) => new MapBuilder(s).Build();

    /// <summary>
    /// Start positions: 2 players on opposite corners, 4 on the four corners (0/1 and 2/3 opposite), any other
    /// count evenly on a circle, the first one towards the lower-left corner. Always rotationally symmetric.
    /// </summary>
    public static Vector2[] StartPositions(int n, int players)
    {
        float lo = n * 0.15f, hi = n * 0.85f;
        return players switch
        {
            <= 2 => [new(lo, lo), new(hi, hi)],
            4 => [new(lo, lo), new(hi, hi), new(lo, hi), new(hi, lo)],
            _ => Enumerable.Range(0, players).Select(i =>
            {
                var d = DMath.Dir(i * DMath.TwoPi / players + DMath.Pi * 1.25f);
                return new Vector2(n * 0.5f, n * 0.5f) + d * (n * (players >= 7 ? 0.37f : 0.38f));
            }).ToArray(),
        };
    }

    /// <summary>
    /// True if every start position is reachable from the first one over land cells (4-connected flood fill over
    /// <see cref="Terrain.CanTraverse"/> with <see cref="Data.MotionLayer.Land"/>).
    /// </summary>
    public static bool StartsConnectedByLand(Terrain t)
    {
        var seen = FloodLand(t, t.StartPositions[0]);
        foreach (var sp in t.StartPositions)
        {
            int cx = Math.Clamp((int)sp.X, 0, t.Width - 1), cz = Math.Clamp((int)sp.Y, 0, t.Height - 1);
            if (!seen[cz * t.Width + cx]) return false;
        }
        return true;
    }

    /// <summary>Cells reachable over land from a position (4-connected).</summary>
    public static bool[] FloodLand(Terrain t, Vector2 from)
    {
        int w = t.Width, h = t.Height;
        var seen = new bool[w * h];
        int sx = Math.Clamp((int)from.X, 0, w - 1), sz = Math.Clamp((int)from.Y, 0, h - 1);
        if (!t.CanTraverse(Data.MotionLayer.Land, sx, sz)) return seen;
        var q = new int[w * h];
        int head = 0, tail = 0;
        q[tail++] = sz * w + sx;
        seen[sz * w + sx] = true;
        while (head < tail)
        {
            int c = q[head++];
            int x = c % w, z = c / w;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
                if (!t.CanTraverse(Data.MotionLayer.Land, nx, nz)) continue;
                int nc = nz * w + nx;
                if (seen[nc]) continue;
                seen[nc] = true;
                q[tail++] = nc;
            }
        }
        return seen;
    }
}

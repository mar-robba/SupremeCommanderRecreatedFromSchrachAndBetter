namespace Rts.Sim.Map;

/// <summary>
/// (8b-3) Visual climate of a map: terrain palette, water tint, sky and tree/rock looks (client only). It never changes
/// the simulation: a game on any biome has the same state hash as on Temperate. Append-only (map files, lobby, saves).
/// </summary>
public enum Biome : byte { Temperate, Desert, Tundra, Lava, Tropical }

public static class Biomes
{
    public static readonly Biome[] All = [Biome.Temperate, Biome.Desert, Biome.Tundra, Biome.Lava, Biome.Tropical];

    /// <summary>Lower-case id (CLI <c>--biome</c>, JSON): "temperate", "desert", ...</summary>
    public static string Id(Biome b) => b.ToString().ToLowerInvariant();

    public static string Name(Biome b) => b.ToString();

    /// <summary>Parses an id or name (case-insensitive); false for anything else.</summary>
    public static bool TryParse(string? text, out Biome biome)
    {
        biome = Biome.Temperate;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var b in All)
            if (string.Equals(Id(b), text.Trim(), StringComparison.OrdinalIgnoreCase)) { biome = b; return true; }
        return false;
    }

    /// <summary>Clamps a stored byte to a known biome (unknown = Temperate).</summary>
    public static Biome FromByte(int v) => v >= 0 && v < All.Length ? (Biome)v : Biome.Temperate;

    /// <summary>The next/previous biome (wrapping).</summary>
    public static Biome Step(Biome b, int dir) => All[(((int)b + dir) % All.Length + All.Length) % All.Length];
}

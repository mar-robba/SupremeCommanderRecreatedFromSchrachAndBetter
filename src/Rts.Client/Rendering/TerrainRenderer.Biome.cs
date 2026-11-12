using System.Numerics;
using Rts.Sim.Map;

namespace Rts.Client.Rendering;

/// <summary>
/// (8b-3) Look of one <see cref="Biome"/> (client only): the 8 ground colours of the terrain shader (grass1, grass2,
/// dry, dirt, rock1, rock2, sand, wet), an emissive glow near the water line (lava shores), the water tint (shallow,
/// deep, foam, <see cref="WaterGlow"/>: 0 = water, 1 = glowing flowing lava with little sky reflection) and the
/// atmosphere (<see cref="Atmosphere"/> sun, ambient, haze, sky gradient).
/// </summary>
public sealed record BiomePalette(
    Vector3 Grass1, Vector3 Grass2, Vector3 Dry, Vector3 Dirt, Vector3 Rock1, Vector3 Rock2, Vector3 Sand, Vector3 Wet,
    Vector3 ShoreGlow,
    Vector3 Shallow, Vector3 Deep, Vector3 Foam, float WaterGlow,
    Vector3 SunColor, Vector3 SkyAmbient, Vector3 GroundAmbient, Vector3 FogColor, Vector3 SkyTop, Vector3 Abyss);

/// <summary>(8b-3) The palette table: one entry per <see cref="Biome"/>. Temperate = the pre-8b-3 shader constants.</summary>
public static class BiomePalettes
{
    private static Vector3 V(float r, float g, float b) => new(r, g, b);

    public static readonly BiomePalette Temperate = new(
        Grass1: V(0.27f, 0.38f, 0.17f), Grass2: V(0.39f, 0.44f, 0.22f), Dry: V(0.52f, 0.49f, 0.32f), Dirt: V(0.43f, 0.36f, 0.26f),
        Rock1: V(0.47f, 0.45f, 0.42f), Rock2: V(0.34f, 0.33f, 0.31f), Sand: V(0.74f, 0.68f, 0.50f), Wet: V(0.44f, 0.41f, 0.32f),
        ShoreGlow: Vector3.Zero,
        Shallow: V(0.17f, 0.52f, 0.54f), Deep: V(0.03f, 0.13f, 0.25f), Foam: V(0.93f, 0.97f, 1.0f), WaterGlow: 0f,
        SunColor: V(1.05f, 0.98f, 0.88f), SkyAmbient: V(0.40f, 0.46f, 0.56f), GroundAmbient: V(0.26f, 0.24f, 0.21f),
        FogColor: V(0.63f, 0.71f, 0.79f), SkyTop: V(0.27f, 0.45f, 0.74f), Abyss: V(0.11f, 0.13f, 0.17f));

    /// <summary>Sand plains, red sandstone highlands, turquoise oasis water, warm sun, dusty haze.</summary>
    public static readonly BiomePalette Desert = new(
        Grass1: V(0.70f, 0.55f, 0.36f), Grass2: V(0.77f, 0.63f, 0.42f), Dry: V(0.64f, 0.45f, 0.29f), Dirt: V(0.60f, 0.42f, 0.28f),
        Rock1: V(0.64f, 0.47f, 0.34f), Rock2: V(0.47f, 0.33f, 0.24f), Sand: V(0.82f, 0.74f, 0.55f), Wet: V(0.52f, 0.43f, 0.31f),
        ShoreGlow: Vector3.Zero,
        Shallow: V(0.20f, 0.56f, 0.52f), Deep: V(0.04f, 0.20f, 0.28f), Foam: V(0.95f, 0.95f, 0.90f), WaterGlow: 0f,
        SunColor: V(1.10f, 0.97f, 0.80f), SkyAmbient: V(0.44f, 0.45f, 0.50f), GroundAmbient: V(0.33f, 0.26f, 0.19f),
        FogColor: V(0.80f, 0.73f, 0.62f), SkyTop: V(0.34f, 0.51f, 0.74f), Abyss: V(0.16f, 0.12f, 0.09f));

    /// <summary>Snow fields with grey tundra patches, blue-grey rock, cold steel water, pale cool sun.</summary>
    public static readonly BiomePalette Tundra = new(
        Grass1: V(0.74f, 0.77f, 0.81f), Grass2: V(0.62f, 0.65f, 0.67f), Dry: V(0.82f, 0.85f, 0.89f), Dirt: V(0.40f, 0.40f, 0.36f),
        Rock1: V(0.45f, 0.47f, 0.51f), Rock2: V(0.30f, 0.32f, 0.36f), Sand: V(0.52f, 0.51f, 0.47f), Wet: V(0.36f, 0.38f, 0.40f),
        ShoreGlow: Vector3.Zero,
        Shallow: V(0.22f, 0.44f, 0.52f), Deep: V(0.04f, 0.12f, 0.22f), Foam: V(0.92f, 0.96f, 1.0f), WaterGlow: 0f,
        SunColor: V(0.95f, 0.96f, 1.0f), SkyAmbient: V(0.45f, 0.51f, 0.61f), GroundAmbient: V(0.30f, 0.31f, 0.34f),
        FogColor: V(0.73f, 0.78f, 0.85f), SkyTop: V(0.38f, 0.52f, 0.72f), Abyss: V(0.13f, 0.15f, 0.19f));

    /// <summary>Black basalt and ash, glowing lava instead of water (hot shores), smoky red sky.</summary>
    public static readonly BiomePalette Lava = new(
        Grass1: V(0.17f, 0.15f, 0.14f), Grass2: V(0.23f, 0.20f, 0.18f), Dry: V(0.32f, 0.29f, 0.26f), Dirt: V(0.30f, 0.16f, 0.10f),
        Rock1: V(0.22f, 0.20f, 0.19f), Rock2: V(0.11f, 0.10f, 0.10f), Sand: V(0.13f, 0.11f, 0.10f), Wet: V(0.24f, 0.11f, 0.06f),
        ShoreGlow: V(1.15f, 0.40f, 0.10f),
        Shallow: V(1.0f, 0.60f, 0.16f), Deep: V(0.88f, 0.22f, 0.04f), Foam: V(1.0f, 0.82f, 0.42f), WaterGlow: 1f,
        SunColor: V(1.05f, 0.84f, 0.70f), SkyAmbient: V(0.40f, 0.32f, 0.31f), GroundAmbient: V(0.40f, 0.21f, 0.13f),
        FogColor: V(0.38f, 0.27f, 0.23f), SkyTop: V(0.22f, 0.16f, 0.16f), Abyss: V(0.10f, 0.04f, 0.03f));

    /// <summary>Lush green, red laterite, white beaches, turquoise lagoons, bright humid sky.</summary>
    public static readonly BiomePalette Tropical = new(
        Grass1: V(0.17f, 0.40f, 0.13f), Grass2: V(0.30f, 0.48f, 0.15f), Dry: V(0.38f, 0.44f, 0.20f), Dirt: V(0.47f, 0.29f, 0.19f),
        Rock1: V(0.42f, 0.42f, 0.38f), Rock2: V(0.27f, 0.29f, 0.26f), Sand: V(0.88f, 0.82f, 0.64f), Wet: V(0.60f, 0.55f, 0.42f),
        ShoreGlow: Vector3.Zero,
        Shallow: V(0.10f, 0.66f, 0.62f), Deep: V(0.02f, 0.16f, 0.32f), Foam: V(0.95f, 0.98f, 1.0f), WaterGlow: 0f,
        SunColor: V(1.08f, 1.00f, 0.88f), SkyAmbient: V(0.40f, 0.50f, 0.58f), GroundAmbient: V(0.24f, 0.27f, 0.18f),
        FogColor: V(0.66f, 0.78f, 0.82f), SkyTop: V(0.20f, 0.48f, 0.80f), Abyss: V(0.08f, 0.13f, 0.16f));

    public static BiomePalette For(Biome b) => b switch
    {
        Biome.Desert => Desert,
        Biome.Tundra => Tundra,
        Biome.Lava => Lava,
        Biome.Tropical => Tropical,
        _ => Temperate,
    };
}

public sealed partial class TerrainRenderer
{
    /// <summary>(8b-3) The visual biome of this map (palette, water, sky, trees/rocks; never the sim).</summary>
    public Biome Biome { get; }
    public BiomePalette Palette { get; }

    /// <summary>Sets the shared <see cref="Atmosphere"/> to this biome (constructor and every <see cref="Draw"/>, so a
    /// Temperate menu backdrop resets what a lava game left behind).</summary>
    private void ApplyBiome()
    {
        var p = Palette;
        Atmosphere.SunColor = p.SunColor;
        Atmosphere.SkyAmbient = p.SkyAmbient;
        Atmosphere.GroundAmbient = p.GroundAmbient;
        Atmosphere.FogColor = p.FogColor;
        Atmosphere.SkyTop = p.SkyTop;
        Atmosphere.Abyss = p.Abyss;
    }

    private void BindGroundPalette(ShaderProgram sp)
    {
        var p = Palette;
        sp.Set("bGrass1", p.Grass1); sp.Set("bGrass2", p.Grass2); sp.Set("bDry", p.Dry); sp.Set("bDirt", p.Dirt);
        sp.Set("bRock1", p.Rock1); sp.Set("bRock2", p.Rock2); sp.Set("bSand", p.Sand); sp.Set("bWet", p.Wet);
        sp.Set("bShoreGlow", p.ShoreGlow);
    }

    private void BindWaterPalette(ShaderProgram sp)
    {
        var p = Palette;
        sp.Set("shallowCol", p.Shallow); sp.Set("deepCol", p.Deep); sp.Set("foamCol", p.Foam);
        sp.Set("waterGlow", p.WaterGlow);
    }
}

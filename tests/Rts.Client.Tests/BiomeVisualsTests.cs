using System.Numerics;
using Rts.Client.Menus;
using Rts.Client.Rendering;
using Rts.Sim.Map;

namespace Rts.Client.Tests;

/// <summary>(8b-3) Biome visuals: palette/look tables (pure) and the --biome plumbing.</summary>
public class BiomeVisualsTests
{
    [Fact]
    public void EveryBiomeHasItsOwnPaletteAndPropLook()
    {
        foreach (var b in Biomes.All)
        {
            var p = BiomePalettes.For(b);
            var look = NatureLook.For(b);
            Assert.NotNull(p);
            Assert.Equal(4, look.Rocks.Length);
            Assert.All(look.Rocks, c => Assert.Equal(0, c.A)); // vertex alpha 0 = rock material
            if (b != Biome.Temperate)
            {
                Assert.NotSame(BiomePalettes.Temperate, p);
                Assert.NotSame(NatureLook.Temperate, look);
            }
            Assert.Equal(b == Biome.Lava, p.WaterGlow > 0);
        }
    }

    [Fact]
    public void TemperateKeepsThePre8b3Constants()
    {
        var p = BiomePalettes.Temperate;
        Assert.Equal(new Vector3(0.27f, 0.38f, 0.17f), p.Grass1);
        Assert.Equal(new Vector3(0.44f, 0.41f, 0.32f), p.Wet);
        Assert.Equal(new Vector3(0.17f, 0.52f, 0.54f), p.Shallow);
        Assert.Equal(new Vector3(0.93f, 0.97f, 1.0f), p.Foam);
        Assert.Equal(Vector3.Zero, p.ShoreGlow);
        Assert.Equal(new Vector3(1.05f, 0.98f, 0.88f), p.SunColor);
        Assert.Equal(new Vector3(0.11f, 0.13f, 0.17f), p.Abyss);
        var look = NatureLook.Temperate;
        Assert.Equal((118, 112, 104), (look.Rocks[0].R, look.Rocks[0].G, look.Rocks[0].B));
        Assert.Equal(new Vector3(0.30f, 0.36f, 0.22f), look.Moss);
        Assert.Equal(0.6f, look.MossAmount);
        Assert.Equal(0, look.RockSidesDelta);
        Assert.Equal(1f, look.RockHeight);
    }

    [Fact]
    public void MinimapPaletteFollowsTheBiome()
    {
        var t = BiomePalettes.Minimap(Biome.Temperate); // the pre-8b-3 minimap constants
        Assert.Equal(new Vector3(0.25f, 0.45f, 0.62f), t.WaterShallow);
        Assert.Equal(new Vector3(0.08f, 0.18f, 0.36f), t.WaterDeep);
        Assert.Equal(new Vector3(0.66f, 0.62f, 0.45f), t.Shore);
        Assert.Equal(new Vector3(0.30f, 0.44f, 0.22f), t.Low);
        Assert.Equal(new Vector3(0.55f, 0.53f, 0.38f), t.High);
        Assert.Equal(new Vector3(0.42f, 0.38f, 0.34f), t.Slope);
        var seen = new HashSet<MinimapPalette>();
        foreach (var b in Enum.GetValues<Biome>()) Assert.True(seen.Add(BiomePalettes.Minimap(b)), b + " shares a minimap palette");
        var lava = BiomePalettes.Minimap(Biome.Lava); // molten: red-orange, not blue
        Assert.True(lava.WaterShallow.X > lava.WaterShallow.Z && lava.WaterDeep.X > lava.WaterDeep.Z);
    }

    [Fact]
    public void BiomeOptionReachesConfigAndSkirmish()
    {
        var o = ClientOptions.Parse(["--demo", "--biome", "lava"]);
        Assert.Equal(Biome.Lava, o.Biome);
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--biome", "moon"]));
        var cfg = GameConfig.FromOptions(o);
        Assert.Equal(Biome.Lava, cfg.EffectiveBiome);
        Assert.Equal(Biome.Lava, cfg.Clone().Biome);
        Assert.Equal(Biome.Lava, cfg.ToSkirmish(out _).Biome);
        Assert.Equal(Biome.Temperate, new GameConfig().EffectiveBiome);
    }
}

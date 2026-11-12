using Rts.Sim;
using Rts.Sim.Map;
using Rts.Sim.Persistence;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>(8b-3) Biomes: map file header byte (optional, old files = temperate), WithHeader keeps the content hash,
/// GameInfo records the biome, and the simulation never depends on it.</summary>
public class BiomeMapTests
{
    private static string PackMap(string id) => Path.Combine(MapLibrary.DefaultDir, id + MapFile.Extension);

    [Fact]
    public void FilesWithoutBiomeByteLoadAsTemperate()
    {
        // A header written without the trailing biome byte (as before 8b-3) = a v1 file of 8b-2.
        var t = MapGenerator.Generate(new MapGenSettings { Size = 256, Players = 2, Seed = 3 });
        var info = new MapFileInfo { Name = "Old", Players = 2, Biome = Biome.Temperate };
        var bytes = MapFile.Encode(t, info);
        var back = MapFile.Decode(bytes);
        Assert.Equal(Biome.Temperate, back.Info.Biome);
        foreach (var f in Directory.GetFiles(MapLibrary.DefaultDir, "*" + MapFile.Extension))
            Assert.True(Enum.IsDefined(MapFile.ReadHeader(f).Biome));
    }

    [Fact]
    public void WithHeaderKeepsTheContentHashAndSetsTheBiome()
    {
        var bytes = File.ReadAllBytes(PackMap("twin_meadows"));
        var orig = MapFile.Decode(bytes);
        foreach (var b in Biomes.All)
        {
            var info = MapFile.Decode(bytes).Info;
            info.Biome = b;
            var back = MapFile.Decode(MapFile.WithHeader(bytes, info));
            Assert.Equal(orig.Info.Hash, back.Info.Hash);
            Assert.Equal(b, back.Info.Biome);
            Assert.Equal(orig.Info.Name, back.Info.Name);
        }
    }

    [Fact]
    public void GameInfoRecordsTheBiome()
    {
        var s = new SkirmishSettings { Seed = 5, MapSize = 256, Players = 2, AiPlayers = [false, false], Biome = Biome.Tundra };
        var sim = GameSetup.CreateSkirmish(TestUtil.Db, s);
        var info = GameInfo.FromJson(GameInfo.ForSkirmish(s, sim, 0, false).ToJson());
        Assert.Equal("tundra", info.Biome);
        s.Biome = null;
        Assert.Equal("temperate", GameInfo.ForSkirmish(s, sim, 0, false).Biome);
    }

    [Fact]
    public void SimulationIsTheSameOnEveryBiome()
    {
        var map = MapFile.Load(PackMap("twin_meadows"));
        ulong? first = null;
        foreach (var b in Biomes.All)
        {
            var s = new SkirmishSettings { Seed = 5, Players = 2, AiPlayers = [true, true], MapFile = map, Biome = b };
            var sim = GameSetup.CreateSkirmish(TestUtil.Db, s);
            TestUtil.Run(sim, 30);
            ulong h = sim.StateHash();
            first ??= h;
            Assert.Equal(first, h);
        }
    }
}

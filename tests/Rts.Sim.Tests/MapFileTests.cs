using System.Numerics;
using Rts.Sim;
using Rts.Sim.Map;
using Rts.Sim.Persistence;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>(8b-2) Map files: exact round trip, the pack in data/maps, games/saves/replays on a map file.</summary>
public class MapFileTests
{
    private static Terrain Gen(MapPreset p, int size, int players, int seed) =>
        MapGenerator.Generate(new MapGenSettings { Size = size, Players = players, Seed = seed, Preset = p });

    private static void AssertSameTerrain(Terrain a, Terrain b)
    {
        Assert.True(SaveGame.TerrainBytes(a).AsSpan().SequenceEqual(SaveGame.TerrainBytes(b)), "terrain bytes differ");
        Assert.Equal(GameInfo.TerrainHashOf(a), GameInfo.TerrainHashOf(b));
        Assert.Equal(a.Props, b.Props);
    }

    [Theory]
    [InlineData(MapPreset.Plains, 0)]
    [InlineData(MapPreset.Archipelago, 0)]
    [InlineData(MapPreset.Highlands, 8)]
    [InlineData(MapPreset.Archipelago, 8)]
    public void RoundTripIsExact(MapPreset preset, int bits)
    {
        var gen = Gen(preset, 256, 2, 3);
        var t = bits == 0 ? gen : MapFile.Quantize(gen, bits);
        Assert.NotEmpty(t.Props);
        var info = new MapFileInfo { Name = "Test map", Description = "desc", HeightBits = bits, Generated = true, Preset = preset, Seed = 3, WaterAmount = 0.25f };
        var bytes = MapFile.Encode(t, info);
        Assert.Equal(bits, info.HeightBits); // kept: every height is a multiple of 2^-bits
        var back = MapFile.Decode(bytes);
        AssertSameTerrain(t, back.Terrain);
        Assert.Equal(("Test map", "desc", 2, 256, preset, 3, 0.25f, info.Hash), (back.Info.Name, back.Info.Description, back.Info.Players, back.Info.Width, back.Info.Preset, back.Info.Seed, back.Info.WaterAmount, back.Info.Hash));
        Assert.Equal(info.Hash, MapFile.ContentHash(back.Terrain, bits));
        // Re-encoding the decoded map gives the same file.
        Assert.Equal(bytes, MapFile.Encode(back.Terrain, new MapFileInfo { Name = "Test map", Description = "desc", HeightBits = bits, Generated = true, Preset = preset, Seed = 3, WaterAmount = 0.25f }));
        // Header-only read from disk.
        var path = Path.Combine(Path.GetTempPath(), $"rts_maptest_{preset}_{bits}_{Environment.ProcessId}{MapFile.Extension}");
        try
        {
            File.WriteAllBytes(path, bytes);
            var head = MapFile.ReadHeader(path);
            Assert.Equal((info.Hash, "Test map", 2, Path.GetFileNameWithoutExtension(path)), (head.Hash, head.Name, head.Players, head.Id));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void UnquantizableHeightsFallBackToExactBits()
    {
        var t = Gen(MapPreset.Plains, 256, 2, 5);
        var info = new MapFileInfo { HeightBits = 8 };
        var back = MapFile.Decode(MapFile.Encode(t, info));
        Assert.Equal(0, info.HeightBits);
        AssertSameTerrain(t, back.Terrain);
    }

    [Fact]
    public void DamagedFilesAreRefused()
    {
        var bytes = MapFile.Encode(Gen(MapPreset.Plains, 256, 2, 1), new MapFileInfo { Name = "x" });
        Assert.Throws<MapFileException>(() => MapFile.Decode(bytes[..(bytes.Length / 2)]));
        Assert.Throws<MapFileException>(() => MapFile.Decode(bytes[..5]));
        Assert.Throws<MapFileException>(() => MapFile.Decode(new byte[100]));
        var flipped = (byte[])bytes.Clone();
        flipped[^10] ^= 0x55;
        Assert.Throws<MapFileException>(() => MapFile.Decode(flipped));
    }

    [Fact]
    public void ExactFileGamePlaysExactlyLikeTheGeneratedGame()
    {
        static SkirmishSettings S() => new() { MapSize = 256, Seed = 42, Players = 2, AiPlayers = [true, true] };
        var gen = GameSetup.CreateSkirmish(TestUtil.Db, S());
        var s = S();
        s.MapFile = MapFile.Decode(MapFile.Encode(MapGenerator.Generate(GameSetup.MapSettingsFor(s)), new MapFileInfo { Name = "same" }));
        var file = GameSetup.CreateSkirmish(TestUtil.Db, s);
        for (int i = 0; i < 300; i++) { gen.Step([]); file.Step([]); }
        Assert.Equal(gen.StateHash(), file.StateHash());
    }

    [Fact]
    public void SpreadStartsPicksFarApartStarts()
    {
        Assert.Equal([0, 1], MapFile.SpreadStarts(MapGenerator.StartPositions(256, 4), 2));
        Assert.Equal([0, 4], MapFile.SpreadStarts(MapGenerator.StartPositions(512, 8), 2));
        Assert.Equal([0, 2, 4], MapFile.SpreadStarts(MapGenerator.StartPositions(512, 6), 3));
    }

    [Fact]
    public void EveryPackMapLoadsAndPassesTheGeneratorGuarantees()
    {
        var maps = MapLibrary.List();
        Assert.InRange(maps.Count, 8, 12);
        Assert.Equal([256, 512, 1024], maps.Select(m => m.Width).Distinct().Order());
        Assert.Contains(maps, m => m.Players == 2);
        Assert.Contains(maps, m => m.Players == 8);
        foreach (var head in maps)
        {
            var f = MapLibrary.Load(head.Id);
            Assert.Equal(head.Hash, f.Info.Hash);
            Assert.InRange(f.Info.Players, 2, 8);
            Assert.False(string.IsNullOrWhiteSpace(f.Info.Name));
            Assert.Equal(f.Info.Hash, MapFile.ContentHash(f.Terrain, f.Info.HeightBits)); // hash stable
            var errors = MapChecks.Check(f.Terrain, TestUtil.Db, naval: MapPresets.NeedsNaval(f.Info.Preset));
            Assert.True(errors.Count == 0, $"{head.Id}: {string.Join("; ", errors)}");
            for (int p = 2; p < f.Info.Players; p++) Assert.Equal(p, f.TerrainFor(p).StartPositions.Length);
        }
        // Pinned: changing the format or re-exporting the pack changes map hashes (saves name maps by id + hash).
        Assert.Equal(0x0AF9AD17AF615153UL, maps.Single(m => m.Id == "twin_meadows").Hash);
    }

    [Fact]
    public void GameOnAMapFileSavesLoadsAndReplays()
    {
        var map = MapLibrary.Load("four_ponds"); // 4 starts, played by 2
        var s = new SkirmishSettings { Players = 2, Seed = 9, AiPlayers = [true, true], MapFile = map };
        var sim = GameSetup.CreateSkirmish(TestUtil.Db, s);
        Assert.Equal(2, sim.World.Terrain.StartPositions.Length);
        Assert.Equal(map.Terrain.StartPositions[1], sim.World.Terrain.StartPositions[1]);
        var info = GameInfo.ForSkirmish(s, sim, localArmy: 0, sandbox: false);
        Assert.Null(info.Map);
        Assert.Equal(("four_ponds", map.Info.Hash, "Four Ponds"), (info.MapFile, info.MapFileHash, info.MapName));
        Assert.Equal("four_ponds", GameInfo.FromJson(info.ToJson()).MapFile);

        var rec = new ReplayRecorder();
        var runner = new SimRunner(sim) { Hooks = rec };
        while (sim.World.Tick < 250) runner.RunOneTick();
        var save = SaveGame.Encode(SaveGame.Capture(sim, info, rec, "map file game"));
        var loaded = SaveGame.Decode(save, TestUtil.Db);
        Assert.Equal(sim.StateHash(), loaded.Sim.StateHash());
        Assert.Equal("four_ponds", loaded.Info.MapFile);

        var player = SaveGame.OpenReplay(SaveGame.EncodeReplay(info, rec.Snapshot()), TestUtil.Db);
        Assert.True(player.RunToEnd(), $"desync at tick {player.DesyncTick}");
        Assert.Equal(sim.StateHash(), player.Sim.StateHash());
    }

    [Fact]
    public void MissingOrDifferentMapFileIsRefused()
    {
        var map = MapLibrary.Load("twin_meadows");
        var s = new SkirmishSettings { Players = 2, AiPlayers = [false, true], MapFile = map };
        var sim = GameSetup.CreateSkirmish(TestUtil.Db, s);
        var info = GameInfo.ForSkirmish(s, sim, 0, false);
        Assert.Equal(GameInfo.TerrainHashOf(sim.World.Terrain), GameInfo.TerrainHashOf(SaveGame.TerrainFor(info, TestUtil.Db, [])));

        var other = info.Clone();
        other.MapFileHash ^= 1;
        var e = Assert.Throws<IncompatibleSaveException>(() => SaveGame.TerrainFor(other, TestUtil.Db, []));
        Assert.Contains("different", e.Message);

        // (8b-3) a file is also found by its content hash (e.g. a map downloaded in multiplayer under another name)
        var renamed = info.Clone();
        renamed.MapFile = "no_such_map";
        Assert.Equal(GameInfo.TerrainHashOf(sim.World.Terrain), GameInfo.TerrainHashOf(SaveGame.TerrainFor(renamed, TestUtil.Db, [])));

        var missing = renamed.Clone();
        missing.MapFileHash ^= 1;
        e = Assert.Throws<IncompatibleSaveException>(() => SaveGame.TerrainFor(missing, TestUtil.Db, []));
        Assert.Contains("not installed", e.Message);

        Assert.Throws<InvalidOperationException>(() => GameSetup.CreateSkirmish(TestUtil.Db,
            new SkirmishSettings { Players = 3, AiPlayers = [false, true, true], MapFile = map }));
    }
}

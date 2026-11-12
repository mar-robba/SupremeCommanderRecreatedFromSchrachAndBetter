using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.AI;
using Rts.Sim.Core;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.World;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// (10a-1) Cross-OS determinism. The sim's math is pure IEEE float arithmetic (<see cref="DMath"/>), so a fixed-seed
/// game must give the same state hash on every OS and CPU. <see cref="GoldenGameHashIsTheSameOnEveryMachine"/> is the
/// CROSS-OS CHECK: it is green on Linux x64 where the values were written; whoever runs the tests on Windows/macOS/ARM
/// and sees it fail has found a cross-OS desync (not a stale literal), unless the sim's behaviour was changed on
/// purpose: then update the literals (the failure message prints the new values) and say why in the commit.
/// Also: files from before the deterministic math (<see cref="GameInfo.MathVersion"/> 0) are refused clearly.
/// </summary>
public class CrossOsDeterminismTests
{
    private readonly ITestOutputHelper _out;
    public CrossOsDeterminismTests(ITestOutputHelper output) => _out = output;

    // Written on Linux x64 (2026-11-12, DMath.Version 1). See the class summary before changing them.
    private const ulong GoldenTerrainHash = 0xC870F62B6FC7C28DUL;
    private const ulong GoldenStateHash = 0xD273E007297C55F1UL;

    /// <summary>AI (Hard, UEF) vs AI (Normal, Cybran) on a generated 256 map (the generator uses DMath trig and
    /// noise), plus a mixed brawl in the middle (tanks, artillery arcs, bombers, interceptors) so weapons, ballistics
    /// and flight math all run; 600 ticks = one minute of game time.</summary>
    private static (Simulation sim, List<UnitHandle>[] brawlers) GoldenGame()
    {
        var sim = GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings
        {
            Seed = 20261112, MapSize = 256, Players = 2, MapPreset = MapPreset.Plains,
            AiPlayers = [true, true], AiDifficulties = [AiDifficulty.Hard, AiDifficulty.Normal], Factions = ["uef", "cybran"],
        });
        var w = sim.World;
        string[][] brawl =
        [
            ["uef_t1_tank", "uef_t1_artillery", "uef_t1_bomber", "uef_t1_interceptor", "uef_t1_mobile_aa"],
            ["cybran_t1_assault_bot", "cybran_t1_artillery", "cybran_t1_bomber", "cybran_t1_interceptor", "cybran_t1_mobile_aa"],
        ];
        var cmds = new List<Command>();
        var brawlers = new List<UnitHandle>[2];
        for (int a = 0; a < 2; a++)
        {
            var units = brawlers[a] = new List<UnitHandle>();
            float side = a == 0 ? -1 : 1;
            for (int kind = 0; kind < brawl[a].Length; kind++)
                for (int k = 0; k < 6; k++)
                    units.Add(TestUtil.Spawn(sim, brawl[a][kind], a, new Vector2(128 + side * (14 + kind * 3), 116 + k * 4), a == 0 ? 0 : MathF.PI));
            cmds.Add(new IssueOrderCommand(a, units.ToArray(), Order.AttackMoveTo(new Vector2(128 - side * 30, 128)), false));
        }
        sim.Step(cmds.ToArray());
        w.Events.Effects.Clear();
        for (int t = 1; t < 600; t++)
        {
            sim.Step();
            w.Events.Effects.Clear();
        }
        return (sim, brawlers);
    }

    [Fact]
    public void GoldenGameHashIsTheSameOnEveryMachine()
    {
        // A fixed thread count, so a failure on a machine with another core count can only be the math
        // (thread-count independence has its own test, CoreTests.SimulationIsDeterministicAcrossThreadCounts).
        int oldThreads = Par.MaxThreads;
        Par.MaxThreads = 2;
        Simulation sim;
        List<UnitHandle>[] brawlers;
        try { (sim, brawlers) = GoldenGame(); }
        finally { Par.MaxThreads = oldThreads; }
        int[] lost = brawlers.Select(b => b.Count(h => !sim.World.Units.IsActive(h))).ToArray();
        ulong terrain = GameInfo.TerrainHashOf(sim.World.Terrain), state = sim.StateHash();
        _out.WriteLine($"terrain 0x{terrain:X16}UL, state 0x{state:X16}UL at tick {sim.World.Tick}, brawl losses {lost[0]}/{brawlers[0].Count} and {lost[1]}/{brawlers[1].Count}");
        Assert.Equal(600, sim.World.Tick);
        Assert.All(lost, n => Assert.True(n > 0)); // the brawl fought (weapons and ballistics ran)
        Assert.True(terrain == GoldenTerrainHash,
            $"CROSS-OS CHECK: the generated terrain differs: 0x{terrain:X16}UL, expected 0x{GoldenTerrainHash:X16}UL (map generator math)");
        Assert.True(state == GoldenStateHash,
            $"CROSS-OS CHECK: the game state differs after 600 ticks: 0x{state:X16}UL, expected 0x{GoldenStateHash:X16}UL");
    }

    // ------------------------------------------------------------------ files from before the deterministic math

    private static (Simulation sim, GameInfo info) SmallGame(bool generatedMap)
    {
        var s = new SkirmishSettings { Seed = 5, MapSize = 256, Players = 2, AiPlayers = [false, false] };
        var sim = GameSetup.CreateSkirmish(TestUtil.Db, s);
        var info = GameInfo.ForSkirmish(s, sim, 0, false);
        if (!generatedMap) info.Map = null; // "custom map": the terrain is embedded in the file
        return (sim, info);
    }

    /// <summary>The file with its header as an old build wrote it: no MathVersion field (and optionally another
    /// terrain hash, as the old MathF generator made a slightly different terrain).</summary>
    private static byte[] AsOldFile(byte[] file, SaveFileKind kind, ulong? terrainHash = null)
    {
        var (info, payload) = SaveContainer.Decode(file, kind);
        var json = JsonNode.Parse(info.ToJson())!.AsObject();
        Assert.True(json.Remove(nameof(GameInfo.MathVersion)));
        if (terrainHash is ulong h) json[nameof(GameInfo.TerrainHash)] = h;
        return SaveContainer.EncodeRaw(kind, SaveContainer.Version, json.ToJsonString(), payload);
    }

    [Fact]
    public void FilesRecordTheMathVersionAndOldHeadersReadAsZero()
    {
        var (sim, info) = SmallGame(generatedMap: true);
        Assert.Equal(DMath.Version, info.MathVersion);
        Assert.True(DMath.Version >= 1);
        var file = SaveGame.Encode(SaveGame.Capture(sim, info, null, "now"));
        Assert.Equal(DMath.Version, SaveContainer.Decode(file, SaveFileKind.Save).info.MathVersion);
        Assert.Equal(0, SaveContainer.Decode(AsOldFile(file, SaveFileKind.Save), SaveFileKind.Save).info.MathVersion);
    }

    [Fact]
    public void ReplaysFromBeforeTheDeterministicMathAreRefusedWithAClearMessage()
    {
        var (sim, info) = SmallGame(generatedMap: true);
        var rec = new ReplayRecorder();
        var runner = new SimRunner(sim) { Hooks = rec };
        for (int t = 0; t < 40; t++) runner.RunOneTick();
        var file = SaveGame.EncodeReplay(info, rec.Snapshot());
        Assert.True(SaveGame.OpenReplay(file, TestUtil.Db).RunToEnd()); // a current replay plays

        var old = AsOldFile(file, SaveFileKind.Replay);
        Assert.False(SaveGame.ReplayMathMatches(SaveContainer.Decode(old, SaveFileKind.Replay).info));
        var e = Assert.Throws<IncompatibleSaveException>(() => SaveGame.OpenReplay(old, TestUtil.Db));
        Assert.Contains("older game version", e.Message);

        // A future math version is refused too.
        var (head, payload) = SaveContainer.Decode(file, SaveFileKind.Replay);
        head.MathVersion = DMath.Version + 1;
        var future = SaveContainer.EncodeRaw(SaveFileKind.Replay, SaveContainer.Version, head.ToJson(), payload);
        e = Assert.Throws<IncompatibleSaveException>(() => SaveGame.OpenReplay(future, TestUtil.Db));
        Assert.Contains($"math version {DMath.Version + 1}", e.Message);
    }

    [Fact]
    public void OldSavesOnGeneratedMapsAreRefusedClearlyAndOthersLoadWithoutTheirReplay()
    {
        // Generated map: the old generator's terrain hash no longer matches = a clear message, not a crash or a desync.
        var (sim, info) = SmallGame(generatedMap: true);
        var file = SaveGame.Encode(SaveGame.Capture(sim, info, null, "gen"));
        var oldGen = AsOldFile(file, SaveFileKind.Save, terrainHash: info.TerrainHash ^ 0x5A5A5A5A);
        var e = Assert.Throws<IncompatibleSaveException>(() => SaveGame.Decode(oldGen, TestUtil.Db));
        Assert.Contains("generated map by an older game version", e.Message);

        // Embedded terrain (same for map files): the old save loads and plays on; its replay log (recorded with the
        // old math) is dropped, so the recording restarts with this build's math.
        var (sim2, info2) = SmallGame(generatedMap: false);
        var rec = new ReplayRecorder();
        var runner = new SimRunner(sim2) { Hooks = rec };
        for (int t = 0; t < 30; t++) runner.RunOneTick();
        var withReplay = SaveGame.Encode(SaveGame.Capture(sim2, info2, rec, "custom"));
        Assert.NotNull(SaveGame.Decode(withReplay, TestUtil.Db).Replay);
        var loaded = SaveGame.Decode(AsOldFile(withReplay, SaveFileKind.Save), TestUtil.Db);
        Assert.Null(loaded.Replay);
        Assert.Equal(DMath.Version, loaded.Info.MathVersion);
        Assert.Equal(sim2.StateHash(), loaded.Sim.StateHash());
    }
}

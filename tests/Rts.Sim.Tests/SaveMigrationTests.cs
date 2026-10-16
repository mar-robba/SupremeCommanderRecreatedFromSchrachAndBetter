using Rts.Sim.Data;
using Rts.Sim.Persistence;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>
/// Files of an older container version load through <see cref="SaveMigrations"/>. The old files are written from a
/// current game (current payload, old version number + old header shape), so the test never goes stale when sim
/// layouts change. Add a case here for every new version step.
/// </summary>
public class SaveMigrationTests
{
    private static (Simulation sim, GameInfo info) Game()
    {
        var sim = TestUtil.NewSkirmish(seed: 5);
        TestUtil.Run(sim, 30);
        var info = GameInfo.ForSkirmish(new SkirmishSettings { Seed = 5, MapSize = 256, Players = 2, AiPlayers = [false, false] }, sim, 0, false);
        return (sim, info);
    }

    /// <summary>A version 1 header: players from before factions (Faction "").</summary>
    private static string V1Header(GameInfo info)
    {
        var old = info.Clone();
        foreach (var p in old.Players) p.Faction = "";
        return old.ToJson();
    }

    [Fact]
    public void EveryOlderVersionHasAStep()
    {
        Assert.Equal(SaveContainer.Version - SaveContainer.MinVersion, SaveMigrations.Steps.Length);
        for (int k = 0; k < SaveMigrations.Steps.Length; k++)
            Assert.Equal(SaveContainer.MinVersion + k, SaveMigrations.Steps[k].From);
    }

    [Fact]
    public void Version1SaveLoadsAndContinuesIdentically()
    {
        var (sim, info) = Game();
        var file = SaveGame.Encode(SaveGame.Capture(sim, info, null, "old"));
        var (_, payload) = SaveContainer.Decode(file, SaveFileKind.Save);
        var v1 = SaveContainer.EncodeRaw(SaveFileKind.Save, 1, V1Header(info), payload);

        var loaded = SaveGame.Decode(v1, TestUtil.Db);
        Assert.Equal(1, loaded.Info.FileVersion);
        Assert.All(loaded.Info.Players, p => Assert.Equal(FactionDb.DefaultFaction, p.Faction));
        Assert.Equal(sim.StateHash(), loaded.Sim.StateHash());
        TestUtil.Run(sim, 40);
        TestUtil.Run(loaded.Sim, 40);
        Assert.Equal(sim.StateHash(), loaded.Sim.StateHash());

        // File lists read (and migrate) only the header.
        string path = Path.Combine(Path.GetTempPath(), $"rts_migration_{Environment.ProcessId}.rtssave");
        try
        {
            File.WriteAllBytes(path, v1);
            var head = SaveContainer.ReadInfo(path, out var kind);
            Assert.Equal(SaveFileKind.Save, kind);
            Assert.Equal(1, head.FileVersion);
            Assert.All(head.Players, p => Assert.Equal(FactionDb.DefaultFaction, p.Faction));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Version1ReplayOpensAndPlays()
    {
        var sim = TestUtil.NewSkirmish(seed: 5);
        var info = GameInfo.ForSkirmish(new SkirmishSettings { Seed = 5, MapSize = 256, Players = 2, AiPlayers = [false, false] }, sim, 0, false);
        var rec = new ReplayRecorder();
        var runner = new SimRunner(sim) { Hooks = rec };
        for (int t = 0; t < 60; t++) runner.RunOneTick();
        var file = SaveGame.EncodeReplay(info, rec.Snapshot());
        var (_, payload) = SaveContainer.Decode(file, SaveFileKind.Replay);
        var v1 = SaveContainer.EncodeRaw(SaveFileKind.Replay, 1, V1Header(info), payload);

        var player = SaveGame.OpenReplay(v1, TestUtil.Db);
        Assert.Equal(1, player.Info.FileVersion);
        Assert.True(player.RunToEnd(), $"desync at tick {player.DesyncTick}");
    }

    [Fact]
    public void CurrentFilesAreNotMigratedAndUnknownVersionsAreRejected()
    {
        var (sim, info) = Game();
        var file = SaveGame.Encode(SaveGame.Capture(sim, info, null, "now"));
        var loaded = SaveGame.Decode(file, TestUtil.Db);
        Assert.Equal(SaveContainer.Version, loaded.Info.FileVersion);
        Assert.Equal(info.Players.Select(p => p.Faction), loaded.Info.Players.Select(p => p.Faction));

        var (_, payload) = SaveContainer.Decode(file, SaveFileKind.Save);
        foreach (ushort v in new[] { (ushort)(SaveContainer.MinVersion - 1), (ushort)(SaveContainer.Version + 1) })
        {
            var bad = SaveContainer.EncodeRaw(SaveFileKind.Save, v, info.ToJson(), payload);
            Assert.Throws<IncompatibleSaveException>(() => SaveGame.Decode(bad, TestUtil.Db));
        }
    }
}

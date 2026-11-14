using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.World;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

public class ReplayTests
{
    private readonly ITestOutputHelper _out;
    public ReplayTests(ITestOutputHelper output) => _out = output;

    /// <summary>Records a rich game through a SimRunner (sorted commands, like the client) up to <paramref name="ticks"/>.</summary>
    private static (SimRunner runner, ReplayRecorder rec, GameInfo info) RecordRich(int ticks)
    {
        var sim = PersistenceTestUtil.NewRich();
        var info = GameInfo.ForSkirmish(PersistenceTestUtil.Settings(), sim, localArmy: 0, sandbox: true);
        var rec = new ReplayRecorder();
        var runner = new SimRunner(sim) { Hooks = rec };
        while (sim.World.Tick < ticks)
        {
            foreach (var c in PersistenceTestUtil.CommandsFor(sim, sim.World.Tick + 1)) runner.Enqueue(c);
            runner.RunOneTick();
        }
        return (runner, rec, info);
    }

    [Fact]
    public void RecordedReplayResimulatesToIdenticalHashes()
    {
        var (runner, rec, info) = RecordRich(1400);
        var log = rec.Snapshot();
        Assert.Equal(140, log.Hashes.Count);
        var file = SaveGame.EncodeReplay(info, log);
        _out.WriteLine($"replay: {file.Length / 1024} KB for {log.LastTick} ticks, {log.CommandCount} commands, initial state {log.InitialState.Length / 1024} KB raw");

        var player = SaveGame.OpenReplay(file, TestUtil.Db);
        Assert.Equal(1400, player.EndTick);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(player.RunToEnd(), $"desync at tick {player.DesyncTick}");
        _out.WriteLine($"re-sim {player.EndTick} ticks in {sw.ElapsedMilliseconds} ms");
        Assert.Equal(140, player.HashesVerified);
        Assert.Equal(runner.Simulation.StateHash(), player.Sim.StateHash());
        var x = WorldState.Capture(runner.Simulation);
        var y = WorldState.Capture(player.Sim);
        int diff = x.AsSpan().CommonPrefixLength(y);
        Assert.True(diff == x.Length && x.Length == y.Length, $"state differs: {WorldState.Locate(runner.Simulation, diff)}");

        // Backward seek (checkpoint at 1200) and forward again: same states.
        player.SeekTo(1250);
        Assert.Equal(1250, player.Tick);
        player.SeekTo(1400);
        Assert.Equal(runner.Simulation.StateHash(), player.Sim.StateHash());
        Assert.True(player.DesyncTick < 0);
    }

    [Fact]
    public void TamperedCommandIsReportedAsDesync()
    {
        var (_, rec, info) = RecordRich(300);
        var log = rec.Snapshot();
        var player = new ReplayPlayer(info, log, TestUtil.Db, PersistenceTestUtil.NewRich().World.Terrain);
        // Drop the sandbox setup commands of tick 1: the game diverges right away.
        var cmds = (Command[])player.CommandsFor(1);
        Array.Clear(cmds);
        var empty = player.CommandsFor(1).Where(c => c != null).ToArray();
        Assert.Empty(empty);
        player.CheckpointInterval = 0;
        var tampered = new ReplayPlayerHarness(player);
        tampered.Run();
        Assert.True(player.DesyncTick > 0 && player.DesyncTick <= 10, $"desync tick {player.DesyncTick}");
    }

    /// <summary>Steps a player while skipping null commands (the tampered entries).</summary>
    private sealed class ReplayPlayerHarness(ReplayPlayer p)
    {
        public void Run()
        {
            while (!p.AtEnd)
            {
                p.Sim.Step(p.CommandsFor(p.Tick + 1).Where(c => c != null).ToList());
                p.AfterStep(p.Sim);
            }
        }
    }

    [Fact]
    public void SaveGameKeepsTheFullReplayAndContinuesIt()
    {
        var (runner, rec, info) = RecordRich(700);
        var saved = SaveGame.Capture(runner.Simulation, info, rec, "test");
        var file = SaveGame.Encode(saved);
        _out.WriteLine($"save: {file.Length / 1024} KB (state {saved.State.Length / 1024} KB raw)");

        var loaded = SaveGame.Decode(file, TestUtil.Db);
        Assert.Equal(700, loaded.Sim.World.Tick);
        Assert.Equal("test", loaded.Info.Name);
        Assert.True(SandboxSystemEnabled(loaded.Sim));
        Assert.NotNull(loaded.Replay);

        // Continue both games 300 more ticks; the loaded one records on top of the loaded replay.
        var rec2 = new ReplayRecorder(loaded.Replay);
        var runner2 = new SimRunner(loaded.Sim) { Hooks = rec2 };
        while (runner.Simulation.World.Tick < 1000)
        {
            foreach (var c in PersistenceTestUtil.CommandsFor(runner.Simulation, runner.Simulation.World.Tick + 1)) runner.Enqueue(c);
            runner.RunOneTick();
            foreach (var c in PersistenceTestUtil.CommandsFor(runner2.Simulation, runner2.Simulation.World.Tick + 1)) runner2.Enqueue(c);
            runner2.RunOneTick();
        }
        Assert.Equal(runner.Simulation.StateHash(), runner2.Simulation.StateHash());

        // The continued replay covers ticks 0..1000 and re-simulates without desync.
        var log = rec2.Snapshot();
        Assert.Equal(0, log.InitialTick);
        Assert.Equal(1000, log.LastTick);
        var player = SaveGame.OpenReplay(SaveGame.EncodeReplay(loaded.Info, log), TestUtil.Db);
        Assert.True(player.RunToEnd(), $"desync at tick {player.DesyncTick}");
        Assert.Equal(runner.Simulation.StateHash(), player.Sim.StateHash());
    }

    private static bool SandboxSystemEnabled(Simulation sim) => Rts.Sim.Systems.SandboxSystem.IsEnabled(sim.World);

    [Fact]
    public void DamagedAndIncompatibleFilesFailCleanly()
    {
        var sim = TestUtil.NewSkirmish(seed: 5);
        TestUtil.Run(sim, 20);
        var info = GameInfo.ForSkirmish(new SkirmishSettings { Seed = 5, MapSize = 256, Players = 2, AiPlayers = [false, false] }, sim, 0, false);
        var file = SaveGame.Encode(SaveGame.Capture(sim, info, null, "x"));
        Assert.Equal(20, SaveGame.Decode(file, TestUtil.Db).Sim.World.Tick);

        // Truncation, bit flips, garbage, wrong kind: always a SaveFormatException, never a crash or a bad game.
        Assert.Throws<SaveFormatException>(() => SaveGame.Decode(file[..(file.Length / 2)], TestUtil.Db));
        Assert.Throws<SaveFormatException>(() => SaveGame.Decode(file[..10], TestUtil.Db));
        Assert.Throws<SaveFormatException>(() => SaveGame.Decode(new byte[1000], TestUtil.Db));
        Assert.Throws<SaveFormatException>(() => SaveGame.OpenReplay(file, TestUtil.Db));
        var rng = new Random(1);
        for (int k = 0; k < 40; k++)
        {
            var bad = (byte[])file.Clone();
            int pos = 40 + rng.Next(bad.Length - 40);
            bad[pos] ^= (byte)(1 << rng.Next(8));
            try { SaveGame.Decode(bad, TestUtil.Db); }
            catch (SaveFormatException) { }
        }

        // Raw state bytes damaged (bypassing the checksum): still a clean SaveFormatException.
        var state = WorldState.Capture(sim);
        for (int k = 0; k < 40; k++)
        {
            var bad = (byte[])state.Clone();
            int pos = rng.Next(Math.Min(bad.Length, 200_000));
            bad[pos] ^= 0xFF;
            try { WorldState.Restore(bad, TestUtil.Db, sim.World.Terrain); }
            catch (SaveFormatException) { }
        }
        Assert.Throws<SaveFormatException>(() => WorldState.Restore(state[..(state.Length / 3)], TestUtil.Db, sim.World.Terrain));

        // (11a-2) Changed blueprint stats: loads, migrated, the replay log restarts. A different map: refused.
        var other = Rts.Sim.Data.BlueprintDb.LoadDirectory(Rts.Sim.Data.DataPaths.UnitBlueprintDir);
        other[0].MaxHealth += 1;
        var migrated = SaveGame.Decode(file, other);
        Assert.True(migrated.Migration.Migrated);
        Assert.Contains(migrated.Migration.Lines, l => l.Contains("blueprints changed") && l.Contains(other[0].Id));
        Assert.Null(migrated.Replay);
        Assert.Throws<IncompatibleSaveException>(() => WorldState.Restore(state, TestUtil.Db, TestUtil.NewSkirmish(seed: 6, players: 3).World.Terrain));
        Assert.Throws<IncompatibleSaveException>(() => TypeNames.Resolve("Rts.Sim:Rts.Sim.World.NoSuchType"));
    }

    [Fact]
    public void FileHeadersRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rts-save-test-" + Environment.ProcessId);
        string oldSaves = SaveStore.SavesDir, oldReplays = SaveStore.ReplaysDir;
        SaveStore.SavesDir = Path.Combine(dir, "saves");
        SaveStore.ReplaysDir = Path.Combine(dir, "replays");
        int oldMax = SaveStore.MaxReplays;
        try
        {
            var (runner, rec, info) = RecordRich(30);
            info.Name = "My game";
            var path = SaveStore.WriteSave(SaveGame.Encode(SaveGame.Capture(runner.Simulation, info, rec, "My game")), "My game");
            var listed = Assert.Single(SaveStore.ListSaves());
            Assert.Equal(path, listed.Path);
            Assert.Equal("My game", listed.Info!.Name);
            Assert.Equal(30, listed.Info.Ticks);
            Assert.Equal(4, listed.Info.Players.Count);
            Assert.True(listed.Info.Players[0].IsAI);
            Assert.Equal(AiDifficulty.Hard, listed.Info.Players[0].Difficulty);
            Assert.True(listed.Info.Sandbox);
            Assert.Equal(Rts.Sim.Map.MapPreset.Lakes, listed.Info.Map!.Preset);

            SaveStore.MaxReplays = 3;
            var replay = SaveGame.EncodeReplay(info, rec.Snapshot());
            for (int k = 0; k < 5; k++) SaveStore.WriteReplay(replay, info);
            Assert.Equal(3, SaveStore.ListReplays().Count);
            File.WriteAllText(Path.Combine(SaveStore.ReplaysDir, "junk" + SaveStore.ReplayExt), "not a replay");
            Assert.Contains(SaveStore.ListReplays(), e => e.Error != null);
        }
        finally
        {
            SaveStore.SavesDir = oldSaves;
            SaveStore.ReplaysDir = oldReplays;
            SaveStore.MaxReplays = oldMax;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}

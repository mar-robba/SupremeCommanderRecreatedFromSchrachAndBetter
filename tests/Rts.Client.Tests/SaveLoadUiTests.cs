using System.Numerics;
using Rts.Client.Menus;
using Rts.Client.Persistence;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.World;

namespace Rts.Client.Tests;

/// <summary>Client side of save/load and replays: texts, client config round trip, recording → save/replay files →
/// load / replay playback with seeking (headless, runner not started: jobs run at RunJobs).</summary>
public class SaveLoadUiTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    [Fact]
    public void LengthsAndDefaultNames()
    {
        Assert.Equal("0:00", SaveText.Length(0));
        Assert.Equal("1:15", SaveText.Length(75.9f));
        Assert.Equal("59:59", SaveText.Length(3599));
        Assert.Equal("1:02:05", SaveText.Length(3725));

        var cfg = new GameConfig { Preset = MapPreset.Lakes, MapSize = 512 };
        var now = new DateTime(2026, 10, 13, 14, 5, 0);
        Assert.Equal("Lakes 512 2026-10-13 14-05", SaveText.DefaultSaveName(cfg, now));
        cfg.Sandbox = true;
        string name = SaveText.DefaultSaveName(cfg, now);
        Assert.StartsWith("Sandbox ", name);
        Assert.Equal(name, SaveStore.Sanitize(name)); // file-name safe as is
    }

    [Fact]
    public void ErrorMessagesAreReadable()
    {
        Assert.StartsWith("Incompatible file:", SaveText.ErrorMessage(new IncompatibleSaveException("blueprints changed")));
        Assert.StartsWith("Damaged or invalid file:", SaveText.ErrorMessage(new SaveFormatException("checksum mismatch")));
        Assert.StartsWith("Damaged or invalid file:", SaveText.ErrorMessage(new AggregateException(new SaveFormatException("x"))));
        Assert.Contains("InvalidOperationException", SaveText.ErrorMessage(new InvalidOperationException("boom")));
    }

    [Fact]
    public void ClientConfigRoundTripsThroughJson()
    {
        var cfg = new GameConfig { MapSize = 512, Preset = MapPreset.Archipelago, WaterAmount = 0.4f, Seed = 99, Sandbox = true, SandboxAi = true, Scene = "naval" };
        cfg.Slots[1].Difficulty = AiDifficulty.Hard;
        cfg.Slots[1].Team = 2;
        cfg.Slots[1].ColorIndex = 5;
        cfg.Slots[2].Kind = SlotKind.AI;
        var json = ClientGameData.From(cfg, new Vector3(10, 20, 30)).ToJson();

        var back = ClientGameData.FromJson(json)!.ToConfig();
        Assert.Equal((cfg.MapSize, cfg.Preset, cfg.WaterAmount, cfg.Seed, cfg.Sandbox, cfg.SandboxAi, cfg.Scene),
            (back.MapSize, back.Preset, back.WaterAmount, back.Seed, back.Sandbox, back.SandboxAi, back.Scene));
        for (int i = 0; i < GameConfig.MaxSlots; i++)
            Assert.Equal((cfg.Slots[i].Kind, cfg.Slots[i].Difficulty, cfg.Slots[i].Team, cfg.Slots[i].ColorIndex),
                (back.Slots[i].Kind, back.Slots[i].Difficulty, back.Slots[i].Team, back.Slots[i].ColorIndex));
        Assert.Equal(new Vector3(10, 20, 30), back.SceneCamera);

        Assert.Null(ClientGameData.FromJson(null));
        Assert.Null(ClientGameData.FromJson("{not json"));
    }

    [Fact]
    public void ConfigFallsBackToTheHeader()
    {
        var info = new GameInfo
        {
            Map = new MapGenSettings { Size = 512, Seed = 3, Preset = MapPreset.Canyon, Players = 3 },
            LocalArmy = 1, Sandbox = true,
            Players =
            [
                new PlayerInfo { Name = "AI 1", IsAI = true, Difficulty = AiDifficulty.Hard, Team = 100, Color = GameSetup.ArmyColors[3] },
                new PlayerInfo { Name = "Player", Team = 2, Color = GameSetup.ArmyColors[0] },
                new PlayerInfo { Name = "AI 2", IsAI = true, Team = 2, Color = 0x12345678 },
            ],
        };
        var cfg = ClientGameData.ConfigFor(info);
        Assert.Equal((512, 3, MapPreset.Canyon, true), (cfg.MapSize, cfg.Seed, cfg.Preset, cfg.Sandbox));
        Assert.Equal(3, cfg.Players);
        Assert.Equal((SlotKind.AI, AiDifficulty.Hard, 0, 3), (cfg.Slots[0].Kind, cfg.Slots[0].Difficulty, cfg.Slots[0].Team, cfg.Slots[0].ColorIndex));
        Assert.Equal((SlotKind.Human, 2, 0), (cfg.Slots[1].Kind, cfg.Slots[1].Team, cfg.Slots[1].ColorIndex));
        Assert.Equal(2, cfg.Slots[2].ColorIndex); // unknown color → slot index
        Assert.Null(cfg.Problem());
    }

    [Fact]
    public void RecordedGameSavesLoadsAndReplaysWithSeeking()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rts-client-saves-" + Guid.NewGuid().ToString("N")[..8]);
        string oldSaves = SaveStore.SavesDir, oldReplays = SaveStore.ReplaysDir;
        SaveStore.SavesDir = Path.Combine(dir, "saves");
        SaveStore.ReplaysDir = Path.Combine(dir, "replays");
        try
        {
            var cfg = new GameConfig { Seed = 7, MapSize = 256 };
            var sim = GameSession.CreateSimulation(Db, cfg, out int local);
            var rec = GameRecording.ForNewGame(cfg, sim, local);
            var runner = new SimRunner(sim) { Hooks = rec };
            for (int i = 0; i < 40; i++) runner.RunOneTick();
            var start = sim.World.Terrain.StartPositions[0];
            runner.Enqueue(new DebugSpawnCommand(0, Db.IndexOf("uef_t1_tank"), start + new Vector2(12, 0), 3));
            for (int i = 0; i < 80; i++) runner.RunOneTick();

            // Save (runner stopped: the capture job runs at once), written on a worker.
            string? saved = null;
            rec.Save(runner, "My save", ClientGameData.From(cfg, new Vector3(10, 20, 30)), p => saved = p);
            GameRecording.WaitForPendingWrites(TimeSpan.FromSeconds(60));
            Assert.NotNull(saved);
            var entry = Assert.Single(SaveStore.ListSaves());
            Assert.Equal("My save", SaveText.Title(entry));
            Assert.Equal(120, entry.Info!.Ticks);
            var loaded = SaveGame.Load(saved!, Db);
            Assert.Equal(sim.StateHash(), loaded.Sim.StateHash());
            var cfg2 = ClientGameData.ConfigFor(loaded.Info);
            Assert.Equal((cfg.Seed, cfg.MapSize), (cfg2.Seed, cfg2.MapSize));
            Assert.Equal(new Vector3(10, 20, 30), cfg2.SceneCamera);

            // End of the session: the replay is written; play it with seeks forward and back.
            rec.Finish(runner);
            GameRecording.WaitForPendingWrites(TimeSpan.FromSeconds(60));
            var rep = Assert.Single(SaveStore.ListReplays());
            Assert.Null(rep.Error);
            var player = SaveGame.OpenReplay(rep.Path, Db);
            var runner2 = new SimRunner(player.Sim) { Hooks = player };
            var seeker = new ReplaySeeker(player, runner2);
            seeker.SeekTo(110);
            runner2.RunJobs();
            Assert.False(seeker.IsSeeking(out _));
            Assert.Equal(110, runner2.Tick);
            Assert.Same(player.Sim, runner2.Simulation);
            seeker.SeekTo(30);
            runner2.RunJobs();
            Assert.Null(seeker.Error);
            Assert.Equal(30, runner2.Tick);
            while (!player.AtEnd) runner2.RunOneTick();
            Assert.Equal(-1, player.DesyncTick);
            Assert.True(player.HashesVerified > 0);
            Assert.Equal(sim.StateHash(), runner2.Simulation.StateHash());
        }
        finally
        {
            SaveStore.SavesDir = oldSaves;
            SaveStore.ReplaysDir = oldReplays;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}

using System.Numerics;
using Rts.Client.Menus;
using Rts.Client.Persistence;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Persistence;

namespace Rts.Client.Tests;

/// <summary>Autosave (Persistence/Autosave.cs): game-time interval, rolling slots, rules, list titles, a real autosave file.</summary>
[Collection("SaveStore folders")] // redirects the static SaveStore.SavesDir (like SaveLoadUiTests): never in parallel
public class AutosaveTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    [Fact]
    public void FirstAutosaveAfterFiveMinutesOfGameTimeThenOncePerInterval()
    {
        Assert.Equal(3000, Autosave.DefaultIntervalTicks); // 5 min at 10 ticks/s
        var a = new Autosave();
        Assert.False(a.Due(0)); // starts the clock
        Assert.False(a.Due(1500));
        Assert.False(a.Due(2999));
        Assert.True(a.Due(3000));
        Assert.False(a.Due(3000)); // same frame/tick again
        Assert.False(a.Due(4000));
        Assert.False(a.Due(5999));
        Assert.True(a.Due(6004)); // a frame may skip a few ticks: the interval restarts from the saved tick
        Assert.Equal(6004, a.LastTick);
        Assert.False(a.Due(9003));
        Assert.True(a.Due(9004));
    }

    [Fact]
    public void LoadedGameWaitsAFullIntervalAndPauseNeverAutosaves()
    {
        var loaded = new Autosave();
        Assert.False(loaded.Due(12345)); // first tick seen = the loaded tick: no instant autosave
        Assert.False(loaded.Due(15344));
        Assert.True(loaded.Due(15345));

        var paused = new Autosave(50);
        Assert.False(paused.Due(10));
        for (int frame = 0; frame < 10000; frame++) Assert.False(paused.Due(40)); // paused: the tick doesn't advance
        Assert.True(paused.Due(60));
    }

    [Fact]
    public void RulesSkipReplaysSandboxScenesScreenshotsAndFinishedGames()
    {
        bool Allowed(bool setting = true, bool forced = false, bool canSave = true, bool readOnly = false, bool sandbox = false,
            bool scene = false, bool screenshot = false, bool ended = false)
            => Autosave.Allowed(setting, forced, canSave, readOnly, sandbox, scene, screenshot, ended);
        Assert.True(Allowed());
        Assert.False(Allowed(setting: false));
        Assert.True(Allowed(setting: false, forced: true));
        Assert.False(Allowed(canSave: false));
        Assert.False(Allowed(readOnly: true));
        Assert.False(Allowed(sandbox: true));
        Assert.False(Allowed(scene: true));
        Assert.False(Allowed(screenshot: true));
        Assert.True(Allowed(screenshot: true, forced: true));
        Assert.False(Allowed(ended: true));
        Assert.False(Allowed(ended: true, forced: true));
    }

    [Fact]
    public void SlotsRotateEmptyFirstThenOldestAndNeverTouchOtherSaves()
    {
        WithTempSaves(dir =>
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(SaveStore.SavePath(SaveStore.QuickSaveName), [1]);
            File.WriteAllBytes(SaveStore.SavePath("autosave_9"), [1]);
            var t0 = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

            Assert.Equal("autosave_1", Autosave.PickSlot());
            Touch("autosave_1", t0);
            Assert.Equal("autosave_2", Autosave.PickSlot());
            Touch("autosave_2", t0.AddMinutes(5));
            Assert.Equal("autosave_3", Autosave.PickSlot());
            Touch("autosave_3", t0.AddMinutes(10));
            Assert.Equal("autosave_1", Autosave.PickSlot()); // all full: the oldest
            Touch("autosave_1", t0.AddMinutes(15));
            Assert.Equal("autosave_2", Autosave.PickSlot());
            File.Delete(SaveStore.SavePath("autosave_3"));
            Assert.Equal("autosave_3", Autosave.PickSlot()); // an empty slot wins over the oldest file
            Assert.Equal("autosave_2", Autosave.PickSlot(avoid: "autosave_3"));

            // An Autosave never picks the slot it wrote last (its write may still be running).
            var a = new Autosave();
            Assert.Equal("autosave_3", a.NextSlot());
            Assert.Equal("autosave_2", a.NextSlot()); // autosave_3 still not written
            Assert.True(File.Exists(SaveStore.SavePath(SaveStore.QuickSaveName)));
            Assert.True(File.Exists(SaveStore.SavePath("autosave_9")));
        });
    }

    [Fact]
    public void ListTitles()
    {
        Assert.Equal((1, 3, 0, 0, 0), (Autosave.SlotOf("autosave_1"), Autosave.SlotOf("autosave_3"), Autosave.SlotOf("autosave_4"),
            Autosave.SlotOf("autosave_01"), Autosave.SlotOf("my autosave_1")));
        var e = new SaveEntry(Path.Combine("saves", "autosave_2.rtssave"), null, "x", DateTime.UtcNow);
        Assert.Equal("Autosave 2", SaveText.Title(e));
        Assert.Equal("Raid (autosave 2)", Autosave.Title("autosave_2", "Raid"));
        Assert.Null(Autosave.Title("quicksave", "quicksave"));
        Assert.Equal("Quicksave", SaveText.Title(new SaveEntry(Path.Combine("saves", "quicksave.rtssave"), null, "x", DateTime.UtcNow)));
    }

    [Fact]
    public void AutosaveFileIsWrittenListedAndLoads()
    {
        WithTempSaves(_ =>
        {
            var cfg = new GameConfig { Seed = 3, MapSize = 256 };
            var sim = GameSession.CreateSimulation(Db, cfg, out int local);
            var rec = GameRecording.ForNewGame(cfg, sim, local);
            var runner = new SimRunner(sim) { Hooks = rec };
            var auto = new Autosave(20);
            string? saved = null;
            Assert.False(auto.Due(runner.Tick));
            for (int i = 0; i < 25; i++)
            {
                runner.RunOneTick();
                if (!auto.Due(runner.Tick)) continue;
                // Runner stopped: the capture runs at once; the file is written on a worker.
                rec.Save(runner, auto.NextSlot(), ClientGameData.From(cfg, new Vector3(1, 2, 3)), p => saved = p, toast: "Autosaved");
                break;
            }
            GameRecording.WaitForPendingWrites(TimeSpan.FromSeconds(60));
            Assert.NotNull(saved);
            Assert.Equal(Path.GetFullPath(SaveStore.SavePath("autosave_1")), Path.GetFullPath(saved!));
            var entry = Assert.Single(SaveStore.ListSaves());
            Assert.Equal("Autosave 1", SaveText.Title(entry));
            Assert.Equal(20, entry.Info!.Ticks);
            var loaded = SaveGame.Load(saved!, Db);
            Assert.Equal(sim.StateHash(), loaded.Sim.StateHash());
        });
    }

    private static void Touch(string name, DateTime utc)
    {
        string path = SaveStore.SavePath(name);
        File.WriteAllBytes(path, [1]);
        File.SetLastWriteTimeUtc(path, utc);
    }

    private static void WithTempSaves(Action<string> body)
    {
        string dir = Path.Combine(Path.GetTempPath(), "rts-autosave-" + Guid.NewGuid().ToString("N")[..8]);
        string old = SaveStore.SavesDir, oldReplays = SaveStore.ReplaysDir;
        SaveStore.SavesDir = Path.Combine(dir, "saves");
        SaveStore.ReplaysDir = Path.Combine(dir, "replays");
        try { body(SaveStore.SavesDir); }
        finally
        {
            SaveStore.SavesDir = old;
            SaveStore.ReplaysDir = oldReplays;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}

using System.Text.Json;
using Rts.Sim.AI;
using Rts.Sim.Data;
using Rts.Sim.Persistence;

namespace Rts.Sim.Tests;

/// <summary>
/// (11a-2) Saves survive blueprint changes (ARCHITECTURE "Save migration"): every saved blueprint index is translated by
/// id, changed stats load (health clamped to the new maximum), an id the save uses that is gone is refused by name.
/// The games are saved with the real db; the "new build" is a db loaded from the same files with edits.
/// </summary>
public class SaveBlueprintMigrationTests
{
    /// <summary>The unit blueprint files loaded like <see cref="BlueprintDb.LoadDirectory"/>, but in the given order,
    /// without the ids <paramref name="drop"/> rejects, and with <paramref name="edit"/> applied before finalizing.</summary>
    private static BlueprintDb Db(bool reverse = false, Func<string, bool>? drop = null, Action<UnitBlueprint>? edit = null)
    {
        var files = Directory.EnumerateFiles(DataPaths.UnitBlueprintDir, "*.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).ToList();
        if (reverse) files.Reverse();
        var db = new BlueprintDb();
        var dropped = new List<string>();
        foreach (var file in files)
        {
            var bp = JsonSerializer.Deserialize<UnitBlueprint>(File.ReadAllText(file), BlueprintDb.JsonOptions)!;
            if (string.IsNullOrEmpty(bp.Id)) bp.Id = Path.GetFileNameWithoutExtension(file);
            if (drop != null && drop(bp.Id)) { dropped.Add(bp.Id); continue; }
            if (drop != null)
            {
                bp.Builds = bp.Builds.Where(x => !drop(x)).ToArray(); // the "new build" no longer lists it anywhere
                if (drop(bp.UpgradesTo)) bp.UpgradesTo = "";
            }
            edit?.Invoke(bp);
            db.Add(bp);
        }
        db.FinalizeDb();
        if (dropped.Count == 0)
        {
            db.Factions = FactionDb.LoadDirectory(DataPaths.FactionDir, db);
            return db;
        }
        // Faction roles that named a dropped blueprint point at the faction's commander instead (still a valid db).
        var dir = Path.Combine(Path.GetTempPath(), "rts-bp-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var f in Directory.EnumerateFiles(DataPaths.FactionDir, "*.json"))
            {
                var text = File.ReadAllText(f);
                var cmd = System.Text.RegularExpressions.Regex.Match(text, "\"Commander\":\\s*\"([^\"]+)\"").Groups[1].Value;
                foreach (var id in dropped) text = text.Replace('"' + id + '"', '"' + cmd + '"');
                File.WriteAllText(Path.Combine(dir, Path.GetFileName(f)), text);
            }
            db.Factions = FactionDb.LoadDirectory(dir, db);
        }
        finally { Directory.Delete(dir, true); }
        return db;
    }

    private sealed class Saved
    {
        public required byte[] File;
        public required Dictionary<int, string> UnitIds;      // unit slot → blueprint id
        public required Dictionary<int, string[]> QueueIds;   // factory slot → queued blueprint ids
        public required Dictionary<int, string[]> RosterIds;  // army → every AiRoster index as an id ("-" = none)
        public required int Tick;
    }

    private static string IdOf(BlueprintDb db, int i) => i < 0 ? "-" : db[i].Id;

    private static Dictionary<int, string> Units(Simulation sim)
    {
        var w = sim.World;
        var d = new Dictionary<int, string>();
        for (int i = 0; i < w.Units.Alive.Length; i++)
            if (w.Units.Alive[i]) d[i] = w.BpOf(i).Id;
        return d;
    }

    private static Dictionary<int, string[]> Queues(Simulation sim)
    {
        var w = sim.World;
        var d = new Dictionary<int, string[]>();
        for (int i = 0; i < w.Units.Alive.Length; i++)
            if (w.Units.Alive[i] && w.Units.Factory[i].Queue is { Count: > 0 } q)
                d[i] = q.Select(b => IdOf(w.Blueprints, b)).ToArray();
        return d;
    }

    private static Dictionary<int, string[]> Rosters(Simulation sim, bool ais)
    {
        var w = sim.World;
        var d = new Dictionary<int, string[]>();
        var ai = AiSystem.StateOf(w);
        if (ai == null || !ais) return d;
        for (int a = 0; a < w.Armies.Length; a++)
        {
            var r = ai.Brain(a).R;
            var ids = new List<string>();
            foreach (var f in TypeLayout.SavedFields(typeof(AiRoster)).OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (f.GetValue(r) is int v) ids.Add(f.Name + "=" + IdOf(w.Blueprints, v));
                else if (f.GetValue(r) is int[] arr) ids.AddRange(arr.Select((x, k) => $"{f.Name}[{k}]=" + IdOf(w.Blueprints, x)));
            }
            d[a] = ids.ToArray();
        }
        return d;
    }

    private static Saved? _rich;

    /// <summary>The rich scenario (4 AIs, one of every blueprint, sandbox) a few hundred ticks in, saved.</summary>
    private static Saved Rich() => _rich ??= Save(PersistenceTestUtil.NewRich(), sim => PersistenceTestUtil.RunTo(sim, 260),
        sim => GameInfo.ForSkirmish(PersistenceTestUtil.Settings(), sim, 0, false));

    private static Saved Save(Simulation sim, Action<Simulation> run, Func<Simulation, GameInfo> info, bool ais = true)
    {
        run(sim);
        return new Saved
        {
            File = SaveGame.Encode(SaveGame.Capture(sim, info(sim), null, "bp-migration")),
            UnitIds = Units(sim), QueueIds = Queues(sim), RosterIds = Rosters(sim, ais), Tick = sim.World.Tick,
        };
    }

    private static void AssertSameBlueprints(Saved s, Simulation sim)
    {
        Assert.Equal(s.Tick, sim.World.Tick);
        Assert.Equal(s.UnitIds, Units(sim));
        var q = Queues(sim);
        Assert.Equal(s.QueueIds.Keys.Order(), q.Keys.Order());
        foreach (var (k, ids) in s.QueueIds) Assert.Equal(ids, q[k]);
        var r = Rosters(sim, s.RosterIds.Count > 0);
        Assert.Equal(s.RosterIds.Keys.Order(), r.Keys.Order());
        foreach (var (k, ids) in s.RosterIds) Assert.Equal(ids, r[k]);
    }

    [Fact]
    public void ScenarioHasQueuesAndRosters()
    {
        var s = Rich();
        Assert.NotEmpty(s.QueueIds);
        Assert.NotEmpty(s.RosterIds);
        Assert.True(s.UnitIds.Count > 100);
    }

    [Fact]
    public void ReorderedBlueprintsLoadWithTheSameBlueprintEverywhere()
    {
        var s = Rich();
        var db = Db(reverse: true);
        Assert.NotEqual(TestUtil.Db[0].Id, db[0].Id); // really reordered
        var g = SaveGame.Decode(s.File, db);
        Assert.True(g.Migration.Migrated);
        Assert.Null(g.Replay); // a migrated save restarts its replay log
        AssertSameBlueprints(s, g.Sim);
        TestUtil.Run(g.Sim, 50); // and it plays on
    }

    [Fact]
    public void ChangedStatsLoadAndHealthIsClampedToTheNewMaximum()
    {
        var s = Rich();
        // The commanders' blueprint (every army has one at full health) loses half its health.
        string id = s.UnitIds.Values.Order(StringComparer.Ordinal).First(x => x.EndsWith("_commander", StringComparison.Ordinal));
        var db = Db(edit: bp => { if (bp.Id == id) bp.MaxHealth /= 2; });
        var g = SaveGame.Decode(s.File, db);
        Assert.True(g.Migration.Migrated);
        Assert.True(g.Migration.Lines.Any(l => l.Contains("blueprints changed") && l.Contains(id)), g.Migration.ToString());
        var dbg = string.Join(", ", Enumerable.Range(0, g.Sim.World.Units.Alive.Length).Where(i => g.Sim.World.Units.Alive[i] && g.Sim.World.BpOf(i).Id.StartsWith(id))
            .Select(i => $"{g.Sim.World.BpOf(i).Id} {g.Sim.World.Units.Health[i]}/{Combat.Veterancy.MaxHealth(g.Sim.World, i)} old {TestUtil.Db[g.Sim.World.BpOf(i).Id].MaxHealth}"));
        Assert.True(g.Migration.Lines.Any(l => l.Contains("more health than their new maximum")), dbg);
        var w = g.Sim.World;
        int seen = 0;
        for (int i = 0; i < w.Units.Alive.Length; i++)
        {
            if (!w.Units.Alive[i]) continue;
            Assert.True(w.Units.Health[i] <= Combat.Veterancy.MaxHealth(w, i) + 1e-3f, $"unit {i} ({w.BpOf(i).Id}) above its maximum");
            if (w.BpOf(i).Id == id) seen++;
        }
        Assert.True(seen > 0);
        AssertSameBlueprints(s, g.Sim);
        TestUtil.Run(g.Sim, 30);
    }

    [Fact]
    public void RemovedBlueprintTheSaveUsesIsRefusedNamingIt()
    {
        var s = Rich();
        // A unit on the map whose blueprint no faction names (so the db without it is still a valid db).
        string id = s.UnitIds.Values.Distinct().Order(StringComparer.Ordinal)
            .First(x => !x.Contains('+') && !x.Contains("commander"));
        var e = Assert.Throws<IncompatibleSaveException>(() => SaveGame.Decode(s.File, Db(drop: x => x == id)));
        Assert.Contains(id, e.Message);
    }

    [Fact]
    public void RemovedBlueprintTheSaveDoesNotUseLoads()
    {
        var sim = TestUtil.NewSkirmish(seed: 5);
        var s = Save(sim, x => TestUtil.Run(x, 30),
            x => GameInfo.ForSkirmish(new SkirmishSettings { Seed = 5, MapSize = 256, Players = 2, AiPlayers = [false, false] }, x, 0, false), ais: false);
        var used = s.UnitIds.Values.ToHashSet();
        var gone = TestUtil.Db.All.Select(b => b.Id)
            .Where(x => !x.Contains('+') && !used.Contains(x) && !x.Contains("commander")).Take(3).ToHashSet();
        Assert.NotEmpty(gone);
        var g = SaveGame.Decode(s.File, Db(drop: gone.Contains));
        Assert.True(g.Migration.Migrated);
        AssertSameBlueprints(s, g.Sim);
        TestUtil.Run(g.Sim, 30);
    }

    [Fact]
    public void IdenticalBlueprintsAreNotAMigration()
    {
        var g = SaveGame.Decode(Rich().File, Db());
        Assert.False(g.Migration.Migrated, g.Migration.ToString());
    }
}

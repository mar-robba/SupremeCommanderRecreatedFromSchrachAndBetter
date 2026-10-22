using Rts.Client.Menus;
using Rts.Sim.Data;
using Rts.Sim.Map;

namespace Rts.Client.Tests;

/// <summary><c>--scene cybran</c>: every Cybran base blueprint is laid out once for army 0 (headless, no window).</summary>
public class CybranSceneTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    [Fact]
    public void SceneSpawnsEveryCybranBlueprintOnce()
    {
        var o = ClientOptions.Parse(["--scene", "cybran"]);
        Assert.Equal(MapPreset.Archipelago, o.Preset);
        Assert.True(o.NoFog);
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--scene", "cybran:bogus"]));
        var cfg = GameConfig.FromOptions(o);
        var sim = GameSession.CreateSimulation(Db, cfg, out _);
        var w = sim.World;
        Assert.NotNull(cfg.SceneCamera);

        var roster = CybranScene.Roster(Db);
        int files = Directory.GetFiles(DataPaths.UnitBlueprintDir, "cybran_*.json").Length;
        Assert.Equal(files, roster.Count);
        var count = new Dictionary<string, int>();
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.IsActive(i) && w.Units.Army[i] == 0)
                count[Db[w.Units.Bp[i]].Id] = count.GetValueOrDefault(Db[w.Units.Bp[i]].Id) + 1;
        foreach (var (group, bp) in roster)
        {
            int expected = group == CybranScene.Group.Air && bp.IsStructure ? CybranScene.Stagings : 1;
            Assert.True(count.GetValueOrDefault(bp.Id) == expected, $"{bp.Id} ({group}): {count.GetValueOrDefault(bp.Id)} spawned, {expected} expected");
        }
        // Naval units and water structures need the water the archipelago map has.
        Assert.Contains(roster, r => r.Group == CybranScene.Group.Naval);
    }
}

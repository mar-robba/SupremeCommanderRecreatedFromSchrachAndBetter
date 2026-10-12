using Rts.Client.Menus;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Client.Tests;

/// <summary><c>--scene naval</c>: option defaults and the scripted setup (headless, no window).</summary>
public class NavalSceneTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    [Fact]
    public void SceneSpawnsFactoriesAndFleetsOnDeepWaterThatStartFighting()
    {
        var o = ClientOptions.Parse(["--scene", "naval"]);
        Assert.Equal(StartScreen.Game, o.StartScreen);
        Assert.Equal(MapPreset.Archipelago, o.Preset);
        Assert.True(o.NoFog);
        var cfg = GameConfig.FromOptions(o);
        var sim = GameSession.CreateSimulation(Db, cfg, out int local);
        var w = sim.World;
        Assert.Equal(0, local);
        Assert.All(w.Armies, a => Assert.False(a.IsAI)); // scripted: no skirmish AI
        Assert.NotNull(cfg.SceneCamera);

        int nf = Db.IndexOf("uef_t1_naval_factory");
        for (int a = 0; a < 2; a++)
        {
            int factories = 0, ships = 0;
            for (int k = 0; k < w.Units.AliveCount; k++)
            {
                int i = w.Units.AliveList[k];
                if (w.Units.Army[i] != a) continue;
                var bp = Db[w.Units.Bp[i]];
                if (w.Units.Bp[i] == nf) factories++;
                if (bp.Movement.Layer is MotionLayer.Water or MotionLayer.Sub)
                {
                    ships++;
                    var p = w.Units.Pos[i];
                    Assert.True(w.Terrain.CanTraverse(MotionLayer.Water, (int)p.X, (int)p.Y), $"{bp.Id} on land at {p}");
                }
            }
            Assert.Equal(1, factories);
            Assert.True(ships >= 12, $"army {a}: {ships} ships");
        }

        // Within ~6 s of game time torpedoes are in the water.
        bool torpedo = false;
        for (int t = 0; t < 60 && !torpedo; t++)
        {
            sim.Step();
            var P = w.Projectiles;
            for (int k = 0; k < P.AliveCount && !torpedo; k++)
            {
                int p = P.AliveList[k];
                var ws = Db[P.SourceBp[p]].Weapons;
                torpedo = P.WeaponIndex[p] < ws.Length && ws[P.WeaponIndex[p]].ProjectileVisual == "torpedo";
            }
        }
        Assert.True(torpedo);
    }
}

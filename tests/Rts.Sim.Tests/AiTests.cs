using System.Numerics;
using Rts.Sim.AI;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

public class AiTests
{
    private static Simulation NewAiGame(int seed, int mapSize, bool[] ai, params AiDifficulty[] difficulty)
    {
        var sim = GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings { Seed = seed, MapSize = mapSize, Players = ai.Length, AiPlayers = ai });
        for (int a = 0; a < ai.Length; a++)
            sim.World.Armies[a].AiDifficulty = difficulty.Length == 0 ? AiDifficulty.Normal : difficulty[Math.Min(a, difficulty.Length - 1)];
        return sim;
    }

    /// <summary>Steps the sim; counts unit deaths per army (effects are cosmetic and normally drained by snapshots).</summary>
    private static void Step(Simulation sim, int[]? deaths = null)
    {
        sim.Step();
        var fx = sim.World.Events.Effects;
        if (deaths != null)
            foreach (var e in fx)
                if (e.Kind == EffectKind.UnitDeath && (uint)e.Army < (uint)deaths.Length) deaths[e.Army]++;
        fx.Clear();
    }

    private static int CountComplete(SimWorld w, int army, UnitCategory cat)
    {
        var u = w.Units;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.IsActive(i) && u.Army[i] == army && u.IsComplete(i) && w.BpOf(i).Has(cat)) n++;
        return n;
    }

    [Fact]
    public void AiAgainstPassiveArmyBuildsEconomyAndArmy()
    {
        var sim = NewAiGame(42, 256, [false, true]);
        var w = sim.World;
        int deepEnergyStall = 0, deepMassStall = 0;
        int ticks = 3000; // 5 game minutes
        for (int t = 0; t < ticks; t++)
        {
            Step(sim);
            var eco = w.Armies[1].Eco;
            if (eco.EnergyEfficiency < 0.5f) deepEnergyStall++;
            if (eco.MassEfficiency < 0.5f) deepMassStall++;
        }
        int mex = CountComplete(w, 1, UnitCategory.MassExtractor);
        int factories = CountComplete(w, 1, UnitCategory.Factory);
        int units = w.Armies[1].UnitCount;
        int mobile = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.IsActive(i) && w.Units.Army[i] == 1 && w.Units.IsComplete(i) && w.BpOf(i).IsMobile) mobile++;
        string info = $"mex={mex} factories={factories} units={units} mobile={mobile} deepStall E={deepEnergyStall} M={deepMassStall} " +
                      $"income M={w.Armies[1].Eco.MassIncome} E={w.Armies[1].Eco.EnergyIncome}";
        Assert.True(mex >= 6, info);
        Assert.True(factories >= 2, info);
        Assert.True(units >= 20, info);
        Assert.True(mobile >= 10, info);
        Assert.True(deepEnergyStall < ticks / 10, info);
        Assert.True(deepMassStall < ticks / 4, info);
        // The passive army was left alone by the AI's commands (it only orders its own units).
        Assert.True(w.Units.IsActive(w.Armies[0].Commander));
        Assert.True(w.Units.Orders[w.Armies[0].Commander.Index].IsEmpty);
    }

    [Fact]
    public void AiGameIsDeterministicAcrossThreadCounts()
    {
        (ulong hash, int units) RunWith(int threads)
        {
            int old = Par.MaxThreads;
            Par.MaxThreads = threads;
            try
            {
                var sim = NewAiGame(11, 256, [true, true], AiDifficulty.Normal, AiDifficulty.Hard);
                for (int t = 0; t < 2000; t++) Step(sim);
                return (sim.StateHash(), sim.World.Units.AliveCount);
            }
            finally { Par.MaxThreads = old; }
        }

        var a = RunWith(1);
        var b = RunWith(8);
        Assert.True(a.units > 30, $"AI armies should have built a lot by now (units={a.units})");
        Assert.Equal(a, b);
    }

    [Fact]
    public void AiVersusAiReachesCombat()
    {
        var sim = NewAiGame(42, 256, [true, true]);
        var w = sim.World;
        var deaths = new int[2];
        int limit = 8 * 60 * SimWorld.TicksPerSecond; // 8 game minutes
        int t = 0;
        for (; t < limit && deaths[0] + deaths[1] < 10; t++) Step(sim, deaths);
        Assert.True(deaths[0] + deaths[1] >= 10, $"only {deaths[0]}+{deaths[1]} deaths after {t} ticks");
        // Both AIs still have their commanders (nobody suicided the ACU early).
        Assert.True(w.Units.IsActive(w.Armies[0].Commander) && w.Units.IsActive(w.Armies[1].Commander));
        var st = AiSystem.StateOf(w)!;
        Assert.True(st.Brain(0).Military.WavesLaunched + st.Brain(1).Military.WavesLaunched > 0);
    }

    [Fact]
    public void AiPlacementFindsValidSitesAroundObstacles()
    {
        var sim = NewAiGame(42, 256, [false, true]);
        var w = sim.World;
        Step(sim); // initializes the brain
        var brain = AiSystem.StateOf(w)!.Brain(1);
        int factory = TestUtil.Db.IndexOf("uef_t1_land_factory");
        int pgen = TestUtil.Db.IndexOf("uef_t1_power_generator");
        // Clutter the preferred area (toward the enemy) with structures of the passive army.
        for (int k = 0; k < 6; k++)
        {
            var p = brain.BasePos + brain.EnemyDir * (6 + k * 3) + new Vector2(-brain.EnemyDir.Y, brain.EnemyDir.X) * ((k % 3) - 1) * 4;
            w.SpawnUnit(pgen, 0, p, 0, complete: true);
        }
        var sites = new List<Vector2>();
        for (int n = 0; n < 4; n++)
        {
            Assert.True(brain.Placement.FindSite(factory, brain.BasePos, brain.EnemyDir, 4, 40, out var site));
            var bp = TestUtil.Db[factory];
            Assert.True(w.Occupancy.CanPlace(bp, site, w.Terrain));
            foreach (var d in w.Terrain.Deposits) Assert.True(Vector2.Distance(d.Pos, site) > 4f, $"site {site} covers deposit {d.Pos}");
            foreach (var s in sites) Assert.True(Vector2.Distance(s, site) >= 9f, "factory sites overlap");
            sites.Add(site);
            // Occupy it so the next search must look elsewhere.
            w.SpawnUnit(factory, 1, site, 0, complete: false);
        }
    }

    [Fact]
    public void AiOnlyActsForAiArmiesAndStopsWhenDefeated()
    {
        var sim = NewAiGame(5, 256, [false, true]);
        var w = sim.World;
        for (int t = 0; t < 200; t++) Step(sim);
        Assert.True(w.Armies[1].UnitCount > 1, "the AI should have started building");
        Assert.Equal(1, w.Armies[0].UnitCount);
        // Kill the AI commander: the army is defeated and the AI no longer issues commands.
        w.KillUnit(w.Armies[1].Commander.Index, UnitHandle.None);
        Step(sim);
        Assert.True(w.Armies[1].Defeated);
        var brain = AiSystem.StateOf(w)!.Brain(1);
        int before = brain.Stats.Commands;
        for (int t = 0; t < 50; t++) Step(sim);
        Assert.Equal(before, brain.Stats.Commands);
    }
}

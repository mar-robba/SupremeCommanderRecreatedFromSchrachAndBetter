using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

/// <summary>SupCom adjacency bonuses (Economy/AdjacencySystem + AdjacencyRules).</summary>
public class AdjacencyTests
{
    private const float Eps = 1e-4f;

    private static void Rich(SimWorld w)
    {
        foreach (var a in w.Armies) { a.Eco.Mass = a.Eco.MassMax; a.Eco.Energy = a.Eco.EnergyMax; }
    }

    private static void StepRich(Simulation sim, int n, params Command[] first)
    {
        for (int t = 0; t < n; t++)
        {
            Rich(sim.World);
            sim.Step(t == 0 ? first : []);
        }
    }

    /// <summary>A free 6×6 site (land factory) away from the commander; its center is integral.</summary>
    private static Vector2 FactorySite(SimWorld w) => FindSite(w, "uef_t1_land_factory", CommanderPos(w), 20, 60);

    /// <summary>Spawns 3 power generators along the factory's +X side (each shares 2 of its 6 cells).</summary>
    private static UnitHandle[] PgensRightOf(Simulation sim, Vector2 fpos, int army = 0, bool complete = true)
    {
        var hs = new UnitHandle[3];
        for (int k = 0; k < 3; k++)
            hs[k] = sim.World.SpawnUnit(Bp("uef_t1_power_generator"), army, fpos + new Vector2(4, -2 + 2 * k), 0, complete);
        return hs;
    }

    [Fact]
    public void PowerGeneratorsNextToFactoryLowerItsBuildEnergyDemand()
    {
        float Demand(bool withPgens, out float frac, out float adj)
        {
            var sim = TestUtil.NewSkirmish();
            var w = sim.World;
            var fpos = FactorySite(w);
            var f = TestUtil.Spawn(sim, "uef_t1_land_factory", 0, fpos);
            if (withPgens) PgensRightOf(sim, fpos);
            StepRich(sim, 20, new FactoryEnqueueCommand(0, [f], Bp("uef_t1_tank"), 5));
            frac = w.Units.BuildFraction[w.Units.Factory[f.Index].Current.Index];
            adj = w.Units.Econ[f.Index].AdjEnergyUse;
            return w.Armies[0].Eco.EnergyDemand;
        }
        float without = Demand(false, out float fracA, out float adjA);
        float with = Demand(true, out float fracB, out float adjB);
        Assert.Equal(0f, adjA);
        Assert.Equal(0.1f, adjB, 4); // 3 × 10% × (2/6 of a side)
        // Tank: 280 E over 280 s of BuildTime at BuildRate 20 => 20 E/s; 10% less.
        Assert.Equal(without - 2f, with, 3);
        Assert.Equal(fracA, fracB, 5); // same build speed, just cheaper
    }

    [Fact]
    public void MassStorageNextToExtractorGivesTwelveAndAHalfPercent()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var dep = NearestDeposit(w, CommanderPos(w) + new Vector2(40, 40), DepositType.Mass);
        var mex = TestUtil.Spawn(sim, "uef_t1_mass_extractor", 0, dep);
        var mpos = w.Units.Pos[mex.Index];
        var st = TestUtil.Spawn(sim, "uef_t1_mass_storage", 0, mpos + new Vector2(2, 0));
        StepRich(sim, 3);
        Assert.Equal(0.125f, w.Units.Econ[mex.Index].AdjMassProd, 5);
        Assert.Equal(2f * 1.125f, w.Units.Econ[mex.Index].MassProduction, 4);
        Assert.Equal(0f, w.Units.Econ[st.Index].AdjMassProd); // storage itself gets nothing

        // Upgrade (blueprint swap re-initializes the economy module): the bonus is recomputed.
        Assert.True(UpgradeSystem.Start(w, mex.Index));
        UpgradeSystem.Complete(w, mex.Index);
        Assert.Equal(0f, w.Units.Econ[mex.Index].AdjMassProd);
        StepRich(sim, 1);
        Assert.Equal(Bp("uef_t2_mass_extractor"), w.Units.Bp[mex.Index]);
        Assert.Equal(0.125f, w.Units.Econ[mex.Index].AdjMassProd, 5);

        // Giving the storage away removes the bonus.
        Assert.True(SandboxSystem.ChangeOwner(w, st.Index, 1));
        StepRich(sim, 1);
        Assert.Equal(0f, w.Units.Econ[mex.Index].AdjMassProd);
    }

    [Fact]
    public void EnergyStorageNextToPowerGeneratorBoostsProduction()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var site = FactorySite(w);
        var pg = TestUtil.Spawn(sim, "uef_t1_power_generator", 0, site);
        var es = TestUtil.Spawn(sim, "uef_t1_energy_storage", 0, w.Units.Pos[pg.Index] + new Vector2(0, 2));
        StepRich(sim, 2);
        Assert.Equal(0.125f, w.Units.Econ[pg.Index].AdjEnergyProd, 5);
        Assert.Equal(20f * 1.125f, w.Units.Econ[pg.Index].EnergyProduction, 4);
        Assert.Equal(0f, w.Units.Econ[es.Index].AdjEnergyProd);
    }

    [Fact]
    public void PowerGeneratorNextToMassFabricatorLowersUpkeepAndDeathRemovesIt()
    {
        float Demand(bool withPgen, out Simulation sim, out UnitHandle fab, out UnitHandle pg)
        {
            sim = TestUtil.NewSkirmish();
            var w = sim.World;
            var site = FactorySite(w);
            fab = TestUtil.Spawn(sim, "uef_t2_mass_fabricator", 0, site);
            pg = withPgen ? TestUtil.Spawn(sim, "uef_t1_power_generator", 0, w.Units.Pos[fab.Index] + new Vector2(-2, 0)) : UnitHandle.None;
            StepRich(sim, 3);
            return w.Armies[0].Eco.EnergyDemand;
        }
        float without = Demand(false, out _, out _, out _);
        float with = Demand(true, out var sim, out var fab, out var pg);
        var w = sim.World;
        Assert.Equal(0.1f, w.Units.Econ[fab.Index].AdjEnergyUse, 5);
        Assert.Equal(135f, EconomySystem.Upkeep(w, Strategic.StrategicState.Of(w), fab.Index, w.BpOf(fab.Index)), 3);
        Assert.Equal(without - 15f, with, 3);

        w.KillUnit(pg.Index, UnitHandle.None);
        StepRich(sim, 2);
        Assert.Equal(0f, w.Units.Econ[fab.Index].AdjEnergyUse);
        Assert.Equal(without, w.Armies[0].Eco.EnergyDemand, 3);
    }

    [Fact]
    public void EnemyCornerAndUnfinishedNeighboursDontCount()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var site = FactorySite(w);
        var fab = TestUtil.Spawn(sim, "uef_t2_mass_fabricator", 0, site);
        var p = w.Units.Pos[fab.Index];
        TestUtil.Spawn(sim, "uef_t1_power_generator", 1, p + new Vector2(2, 0));   // enemy, shares a side
        TestUtil.Spawn(sim, "uef_t1_power_generator", 0, p + new Vector2(2, 2));   // own, corner only
        TestUtil.Spawn(sim, "uef_t1_power_generator", 0, p + new Vector2(-2, -2)); // own, corner only
        var site2 = w.SpawnUnit(Bp("uef_t1_power_generator"), 0, p + new Vector2(0, -2), 0, complete: false); // unfinished
        StepRich(sim, 2);
        Assert.Equal(0f, w.Units.Econ[fab.Index].AdjEnergyUse);

        // Finishing the site turns its bonus on.
        ConstructionSystem.CompleteUnit(w, site2.Index);
        StepRich(sim, 1);
        Assert.Equal(0.1f, w.Units.Econ[fab.Index].AdjEnergyUse, 5);
    }

    [Fact]
    public void EnergyUseReductionIsCappedAndRecomputedOnlyOnChanges()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var site = FactorySite(w);
        var fab = TestUtil.Spawn(sim, "uef_t2_mass_fabricator", 0, site);
        var p = w.Units.Pos[fab.Index];
        // Four T3 power generators (6×6, 20% per full 2-cell side each) in a pinwheel would give 80%: capped at 75%.
        foreach (var d in new[] { new Vector2(4, -2), new Vector2(2, 4), new Vector2(-4, 2), new Vector2(-2, -4) })
            TestUtil.Spawn(sim, "uef_t3_power_generator", 0, p + d);
        StepRich(sim, 2);
        Assert.Equal(AdjacencyRules.MaxEnergyUseReduction, w.Units.Econ[fab.Index].AdjEnergyUse, 5);
        int before = AdjacencySystem.RecomputeCount(w);
        StepRich(sim, 20);
        Assert.Equal(before, AdjacencySystem.RecomputeCount(w));
    }
}

using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

/// <summary>Pause toggle (SetPausedCommand): paused units stop consuming AND progressing, and resume on unpause.</summary>
public class PauseTests
{
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

    private static SetPausedCommand Pause(UnitHandle h, bool on) => new(0, [h], on);

    private static Vector2 Site(SimWorld w, string id = "uef_t1_land_factory") => FindSite(w, id, CommanderPos(w), 20, 60);

    /// <summary>Pauses, checks that progress and the army's demand stop (after the one tick already paid), then
    /// unpauses and checks that progress resumes.</summary>
    private static void AssertPauseStops(Simulation sim, UnitHandle unit, Func<float> progress)
    {
        var w = sim.World;
        StepRich(sim, 2, Pause(unit, true));
        Assert.True((w.Units.Flags[unit.Index] & UnitFlags.Paused) != 0);
        float p0 = progress();
        StepRich(sim, 30);
        Assert.Equal(p0, progress());
        Assert.Equal(0f, w.Units.Econ[unit.Index].MassRequest);
        Assert.Equal(0f, w.Units.Econ[unit.Index].EnergyRequest);
        Assert.Equal(0f, w.Armies[0].Eco.MassDemand);
        StepRich(sim, 10, Pause(unit, false));
        Assert.True(progress() > p0, "progress resumes after unpause");
        Assert.True(w.Armies[0].Eco.MassDemand > 0);
    }

    [Fact]
    public void PausedFactoryStopsBuildingAndConsuming()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var f = TestUtil.Spawn(sim, "uef_t1_land_factory", 0, Site(w));
        StepRich(sim, 10, new FactoryEnqueueCommand(0, [f], Bp("uef_t1_tank"), 3));
        var cur = w.Units.Factory[f.Index].Current;
        Assert.True(w.Units.IsActive(cur));
        AssertPauseStops(sim, f, () => w.Units.BuildFraction[cur.Index]);
    }

    [Fact]
    public void PausedEngineerAndCommanderStopBuilding()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var site = Site(w, "uef_t1_power_generator");
        var eng = TestUtil.Spawn(sim, "uef_t1_engineer", 0, site + new Vector2(0, 4));
        StepRich(sim, 10, new IssueOrderCommand(0, [eng], Order.BuildAt(Bp("uef_t1_power_generator"), site), false));
        var pg = w.Units.Orders[eng.Index].Current.Spawned;
        Assert.True(w.Units.IsActive(pg));
        AssertPauseStops(sim, eng, () => w.Units.BuildFraction[pg.Index]);

        var com = w.Armies[0].Commander;
        var site2 = Site(w, "uef_t1_power_generator");
        w.Units.Pos[com.Index] = site2 + new Vector2(0, 5);
        StepRich(sim, 10, new StopCommand(0, [eng]), new IssueOrderCommand(0, [com], Order.BuildAt(Bp("uef_t1_power_generator"), site2), false));
        var pg2 = w.Units.Orders[com.Index].Current.Spawned;
        Assert.True(w.Units.IsActive(pg2));
        AssertPauseStops(sim, com, () => w.Units.BuildFraction[pg2.Index]);
    }

    [Fact]
    public void PausedUpgradeStopsAndAssistingEngineersWait()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var site = Site(w);
        var f = TestUtil.Spawn(sim, "uef_t1_land_factory", 0, site);
        var eng = TestUtil.Spawn(sim, "uef_t1_engineer", 0, site + new Vector2(0, 6));
        StepRich(sim, 10, new UpgradeCommand(0, [f]), new IssueOrderCommand(0, [eng], Order.GuardUnit(f), false));
        Assert.True(w.Units.Upgrade[f.Index].Active);
        Assert.Equal(BuilderMode.Upgrade, w.Units.Builder[eng.Index].Mode);
        AssertPauseStops(sim, f, () => w.Units.Upgrade[f.Index].Fraction);
        // While paused the assisting engineer did nothing either (checked by the zero army demand); now it helps again.
        Assert.Equal(BuilderMode.Upgrade, w.Units.Builder[eng.Index].Mode);
    }

    [Fact]
    public void PausedFactoryAlsoStopsItsAssistants()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var site = Site(w);
        var f = TestUtil.Spawn(sim, "uef_t1_land_factory", 0, site);
        var eng = TestUtil.Spawn(sim, "uef_t1_engineer", 0, site + new Vector2(0, 6));
        StepRich(sim, 10, new FactoryEnqueueCommand(0, [f], Bp("uef_t1_tank"), 3), new IssueOrderCommand(0, [eng], Order.GuardUnit(f), false));
        var cur = w.Units.Factory[f.Index].Current;
        Assert.Equal(BuilderMode.Assist, w.Units.Builder[eng.Index].Mode);
        AssertPauseStops(sim, f, () => w.Units.BuildFraction[cur.Index]);
    }

    [Fact]
    public void PausedUpgradingMexKeepsProducingWhileItsUpgradeStops()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var dep = NearestDeposit(w, CommanderPos(w), DepositType.Mass);
        var mex = TestUtil.Spawn(sim, "uef_t1_mass_extractor", 0, dep);
        StepRich(sim, 5, new UpgradeCommand(0, [mex]));
        Assert.True(w.Units.Upgrade[mex.Index].Active);
        StepRich(sim, 2, Pause(mex, true));
        float frac = w.Units.Upgrade[mex.Index].Fraction;
        StepRich(sim, 20);
        Assert.Equal(frac, w.Units.Upgrade[mex.Index].Fraction);
        Assert.Equal(2f, w.Units.Econ[mex.Index].MassProduction, 4); // still producing
        Assert.Equal(0f, w.Armies[0].Eco.MassDemand);
        StepRich(sim, 5, Pause(mex, false));
        Assert.True(w.Units.Upgrade[mex.Index].Fraction > frac);
    }

    [Fact]
    public void PausedMassFabricatorStopsProducingAndPayingUpkeep()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var fab = TestUtil.Spawn(sim, "uef_t2_mass_fabricator", 0, Site(w, "uef_t2_mass_fabricator"));
        StepRich(sim, 3);
        Assert.Equal(1f, w.Units.Econ[fab.Index].MassProduction, 4);
        float demand = w.Armies[0].Eco.EnergyDemand;
        Assert.True(demand >= 150f);
        StepRich(sim, 2, Pause(fab, true));
        Assert.Equal(0f, w.Units.Econ[fab.Index].MassProduction);
        Assert.Equal(demand - 150f, w.Armies[0].Eco.EnergyDemand, 3);
        StepRich(sim, 2, Pause(fab, false));
        Assert.Equal(1f, w.Units.Econ[fab.Index].MassProduction, 4);
    }

    [Fact]
    public void PausedSiloStopsStockpiling()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var tml = TestUtil.Spawn(sim, "uef_t2_tactical_missile_launcher", 0, Site(w, "uef_t2_tactical_missile_launcher"));
        var strat = Strategic.StrategicState.Of(w);
        StepRich(sim, 10);
        Assert.True(strat.Silo[tml.Index].Progress > 0);
        AssertPauseStops(sim, tml, () => strat.Silo[tml.Index].Progress);
    }
}

using System.Numerics;
using Rts.Sim.Orders;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

public class ArmyStatsTests
{
    [Fact]
    public void BuiltLostKilledAndCollectedAreCounted()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var site = FindSite(w, "uef_t1_land_factory", CommanderPos(w), 10);
        var f = TestUtil.Spawn(sim, "uef_t1_land_factory", 0, site);
        sim.Step();
        TestUtil.Run(sim, 160, new FactoryEnqueueCommand(0, [f], Bp("uef_t1_tank"), 1));
        Assert.Equal(1, w.Armies[0].Stats.UnitsBuilt);
        Assert.Equal(0, w.Armies[0].Stats.StructuresBuilt); // spawned complete, not built
        Assert.True(w.Armies[0].Stats.MassCollected > 0);
        Assert.True(w.Armies[0].Stats.MassSpent > 0);
        Assert.True(w.Armies[1].Stats.MassCollected > 0);

        // A tank of army 1 next to an engineer of army 0, far from both commanders.
        var mid = new Vector2(w.Terrain.Width * 0.5f, w.Terrain.Height * 0.5f);
        var victim = TestUtil.Spawn(sim, "uef_t1_engineer", 0, mid);
        var killer = TestUtil.Spawn(sim, "uef_t1_tank", 1, mid + new Vector2(6, 0));
        for (int t = 0; t < 400 && w.Units.IsActive(victim); t++) sim.Step();
        Assert.False(w.Units.IsActive(victim));
        Assert.True(w.Units.IsActive(killer));
        Assert.Equal(1, w.Armies[0].Stats.UnitsLost);
        Assert.Equal(1, w.Armies[1].Stats.UnitsKilled);
        Assert.Equal(0, w.Armies[0].Stats.UnitsKilled);
    }

    [Fact]
    public void WrecksAndReclaimDontDistortKillsAndLosses()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var cmd = w.Armies[0].Commander;
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 1, CommanderPos(w) + new Vector2(14, 3));
        sim.Step();
        Systems.DamageSystem.Apply(w, tank.Index, 1e6f, cmd);
        sim.Step();
        Assert.Equal(1, w.Props.Count); // the tank left a wreck
        Assert.Equal(1, w.Armies[0].Stats.UnitsKilled);
        Assert.Equal(1, w.Armies[1].Stats.UnitsLost);
        Assert.Equal(1, w.Units.Kills[cmd.Index]); // veterancy counter agrees

        // Reclaiming the wreck gives mass but is neither a kill nor a loss.
        float collected = w.Armies[0].Stats.MassCollected;
        int p = w.Props.AliveList[0];
        TestUtil.Run(sim, 120, new IssueOrderCommand(0, [cmd], Order.ReclaimAt(Core.UnitHandle.None, w.Props.Pos[p] + new Vector2(0.5f, 0)), false));
        Assert.Equal(0, w.Props.Count);
        Assert.True(w.Armies[0].Stats.MassCollected > collected + 40f);
        Assert.Equal(1, w.Armies[0].Stats.UnitsKilled);
        Assert.Equal(0, w.Armies[0].Stats.UnitsLost);
        Assert.Equal(1, w.Armies[1].Stats.UnitsLost);
        Assert.Equal(0, w.Armies[1].Stats.UnitsKilled);
    }

    [Fact]
    public void CommanderKillDefeatsTheArmyAndEndsTheGame()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var cmd1 = w.Armies[1].Commander;
        TestUtil.Spawn(sim, "uef_t1_engineer", 1, CommanderPos(w, 1) + new Vector2(6, 0));
        var killer = TestUtil.Spawn(sim, "uef_t1_tank", 0, CommanderPos(w, 1) + new Vector2(-20, 0));
        sim.Step();
        Systems.DamageSystem.Apply(w, cmd1.Index, 1e6f, killer);
        sim.Step();
        Assert.True(w.Armies[1].Defeated);
        Assert.Equal(w.Armies[0].Team, w.WinningTeam);
        Assert.Equal(0, w.Armies[1].UnitCount);
        Assert.Equal(1, w.Armies[0].Stats.UnitsKilled);  // the commander; the rest self-destructs
        Assert.Equal(2, w.Armies[1].Stats.UnitsLost);    // commander + engineer
        Assert.False(w.Armies[0].Defeated);
    }
}

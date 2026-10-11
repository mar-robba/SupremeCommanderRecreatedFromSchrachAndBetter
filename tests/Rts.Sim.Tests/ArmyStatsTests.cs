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
}

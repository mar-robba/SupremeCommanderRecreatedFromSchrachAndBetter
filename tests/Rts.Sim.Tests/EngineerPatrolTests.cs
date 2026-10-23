using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.Systems;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

/// <summary>Engineer patrol automation (Economy/EngineerPatrol): repair &gt; assist &gt; reclaim near the route.</summary>
public class EngineerPatrolTests
{
    private sealed record Route(Simulation Sim, SimWorld W, UnitHandle Unit, Vector2 Start, Vector2 End, Vector2 Side)
    {
        /// <summary>A point beside the route, <paramref name="along"/> (0..1) of the way, <paramref name="off"/> to the side.</summary>
        public Vector2 Beside(float along, float off) => Vector2.Lerp(Start, End, along) + Side * off;
    }

    /// <summary>A unit of army 0 near its commander, facing a 30-unit patrol leg toward the map center.</summary>
    private static Route Setup(string unit = "uef_t1_engineer")
    {
        var sim = TestUtil.NewSkirmish(seed: 5);
        var w = sim.World;
        var c = CommanderPos(w);
        var dir = Vector2.Normalize(new Vector2(w.Terrain.Width, w.Terrain.Height) * 0.5f - c);
        var start = c + dir * 10f;
        var end = c + dir * 40f;
        var h = TestUtil.Spawn(sim, unit, 0, start);
        w.Armies[0].Eco.Mass = 0;
        w.Armies[0].Eco.Energy = w.Armies[0].Eco.EnergyMax;
        return new Route(sim, w, h, start, end, new Vector2(-dir.Y, dir.X));
    }

    private static void Patrol(Route r) =>
        r.Sim.Step([new IssueOrderCommand(0, [r.Unit], Order.PatrolTo(r.End), false)]);

    private static int RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return t;
            sim.Step();
        }
        Assert.Fail($"not within {maxTicks} ticks: {what}");
        return -1;
    }

    private static ref Order Front(Route r) => ref r.W.Units.Orders[r.Unit.Index].Current;

    private static void AssertKeepsPatrolling(Route r)
    {
        var u = r.W.Units;
        var q = u.Orders[r.Unit.Index];
        Assert.Equal(2, q.Count);
        foreach (var o in q.AsSpan())
        {
            Assert.Equal(OrderType.Patrol, o.Type);
            Assert.Equal(EngineerPatrol.JobNone, o.State);
        }
        // Walks the whole route: reaches both ends.
        RunUntil(r.Sim, () => Vector2.Distance(u.Pos[r.Unit.Index], r.End) < 3f, 600, "reach the far patrol point");
        RunUntil(r.Sim, () => Vector2.Distance(u.Pos[r.Unit.Index], r.Start) < 3f, 600, "walk back to the start");
        Assert.Equal(2, q.Count);
    }

    [Fact]
    public void PatrollingEngineerReclaimsWreckNearRouteThenResumes()
    {
        var r = Setup();
        var w = r.W;
        int wreck = w.Props.Spawn(r.Beside(0.5f, 6f), 0, Bp("uef_t1_tank"), 1f, 20f, 0f);
        var wreckH = w.Props.HandleOf(wreck);
        Patrol(r);
        int ticks = 1 + RunUntil(r.Sim, () => !w.Props.IsActive(wreckH), 400, "reclaim the wreck");
        r.Sim.Step(); // the last tick's reclaim reaches storage one tick later (EconomySystem)
        float fromCommander = (ticks + 1) * SimWorld.Dt * 1f;
        Assert.InRange(w.Armies[0].Eco.Mass, fromCommander + 20f - 0.3f, fromCommander + 20f + 0.3f);
        AssertKeepsPatrolling(r);
    }

    [Fact]
    public void PatrollingEngineerRepairsDamagedAllyToFullHealth()
    {
        var r = Setup();
        var w = r.W;
        w.Armies[0].Eco.Mass = 100;
        var tank = TestUtil.Spawn(r.Sim, "uef_t1_tank", 0, r.Beside(0.4f, 7f));
        float max = Combat.Veterancy.MaxHealth(w, tank.Index);
        w.Units.Health[tank.Index] = max * 0.6f;
        Patrol(r);
        RunUntil(r.Sim, () => Front(r).State == EngineerPatrol.JobRepair, 100, "pick the repair job");
        Assert.Equal(tank, Front(r).Target);
        RunUntil(r.Sim, () => w.Units.Health[tank.Index] >= max, 600, "repair the tank");
        Assert.True(w.Units.IsActive(tank));
        r.Sim.Step();
        AssertKeepsPatrolling(r);
    }

    [Fact]
    public void PatrollingEngineerAssistsUnfinishedAlliedStructure()
    {
        var r = Setup();
        var w = r.W;
        w.Armies[0].Eco.Mass = w.Armies[0].Eco.MassMax;
        var site = FindSite(w, "uef_t1_power_generator", r.Beside(0.5f, 7f), 0, 4);
        var h = w.SpawnUnit(Bp("uef_t1_power_generator"), 0, site, 0, complete: false);
        Assert.False(w.Units.IsComplete(h.Index));
        Patrol(r);
        RunUntil(r.Sim, () => Front(r).State == EngineerPatrol.JobAssist, 150, "pick the assist job");
        RunUntil(r.Sim, () => w.Units.IsComplete(h.Index), 600, "finish the site");
        Assert.True(w.Units.IsActive(h));
        r.Sim.Step();
        AssertKeepsPatrolling(r);
    }

    [Fact]
    public void RepairComesBeforeReclaim()
    {
        var r = Setup();
        var w = r.W;
        w.Armies[0].Eco.Mass = 100;
        // The wreck is nearer to the engineer than the damaged tank: repair still wins.
        int wreck = w.Props.Spawn(r.Beside(0.25f, -4f), 0, Bp("uef_t1_tank"), 1f, 20f, 0f);
        var wreckH = w.Props.HandleOf(wreck);
        var tank = TestUtil.Spawn(r.Sim, "uef_t1_tank", 0, r.Beside(0.3f, 8f));
        float max = Combat.Veterancy.MaxHealth(w, tank.Index);
        w.Units.Health[tank.Index] = max * 0.8f;
        Patrol(r);
        RunUntil(r.Sim, () => Front(r).State != EngineerPatrol.JobNone, 100, "pick a job");
        Assert.Equal(EngineerPatrol.JobRepair, Front(r).State);
        Assert.Equal(tank, Front(r).Target);
        Assert.True(w.Props.IsActive(wreckH));
        RunUntil(r.Sim, () => w.Units.Health[tank.Index] >= max, 600, "repair the tank");
        RunUntil(r.Sim, () => !w.Props.IsActive(wreckH), 300, "then reclaim the wreck");
    }

    [Fact]
    public void NonBuilderPatrolIgnoresWrecksAndDamagedAllies()
    {
        var r = Setup("uef_t1_tank");
        var w = r.W;
        int wreck = w.Props.Spawn(r.Beside(0.5f, 4f), 0, Bp("uef_t1_tank"), 1f, 20f, 0f);
        var wreckH = w.Props.HandleOf(wreck);
        var ally = TestUtil.Spawn(r.Sim, "uef_t1_tank", 0, r.Beside(0.3f, 6f));
        w.Units.Health[ally.Index] *= 0.5f;
        Patrol(r);
        var u = w.Units;
        RunUntil(r.Sim, () =>
        {
            Assert.Equal(EngineerPatrol.JobNone, Front(r).State);
            return Vector2.Distance(u.Pos[r.Unit.Index], r.End) < 3f;
        }, 200, "the tank reaches the far patrol point");
        Assert.True(w.Props.IsActive(wreckH));
        Assert.Equal(BuilderMode.None, u.Builder[r.Unit.Index].Mode);
        AssertKeepsPatrolling(r);
    }

    [Fact]
    public void SaveLoadDuringPatrolRepairContinuesIdentically()
    {
        var r = Setup();
        var w = r.W;
        w.Armies[0].Eco.Mass = 100;
        var tank = TestUtil.Spawn(r.Sim, "uef_t1_tank", 0, r.Beside(0.4f, 7f));
        float max = Combat.Veterancy.MaxHealth(w, tank.Index);
        w.Units.Health[tank.Index] = max * 0.5f;
        int wreck = w.Props.Spawn(r.Beside(0.6f, -5f), 0, Bp("uef_t1_tank"), 1f, 20f, 0f);
        Patrol(r);
        RunUntil(r.Sim, () => w.Units.Builder[r.Unit.Index].Mode == BuilderMode.Repair, 300, "start repairing");
        TestUtil.Run(r.Sim, 30);
        Assert.Equal(EngineerPatrol.JobRepair, Front(r).State);

        var saved = WorldState.Capture(r.Sim);
        var b = WorldState.Restore(saved, w.Blueprints, w.Terrain);
        Assert.Equal(r.Sim.StateHash(), b.StateHash());
        Assert.Equal(EngineerPatrol.JobRepair, b.World.Units.Orders[r.Unit.Index].Current.State);
        for (int t = 0; t < 600; t++)
        {
            r.Sim.Step();
            b.Step();
            if (t % 25 == 0) Assert.True(r.Sim.StateHash() == b.StateHash(), $"StateHash differs {t} ticks after the load");
        }
        Assert.Equal(r.Sim.StateHash(), b.StateHash());
        Assert.True(w.Units.Health[tank.Index] >= max);
        Assert.False(w.Props.IsActive(w.Props.HandleOf(wreck)) && w.Props.Mass[wreck] >= 20f); // reclaim started or done
        Assert.Equal(b.World.Units.Health[tank.Index], w.Units.Health[tank.Index]);
    }
}

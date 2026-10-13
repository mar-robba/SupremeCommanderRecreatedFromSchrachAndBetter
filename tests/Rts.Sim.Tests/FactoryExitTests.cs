using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

/// <summary>Finished units drive out of their factory (no teleport to the exit); aircraft take off from the pad.</summary>
public class FactoryExitTests
{
    /// <summary>Queues one unit and steps until the factory releases it; returns the unit (released this tick).</summary>
    private static UnitHandle BuildOne(Simulation sim, UnitHandle factory, string bpId, int maxTicks = 3000)
    {
        var u = sim.World.Units;
        sim.Step([new FactoryEnqueueCommand(factory.Index >= 0 ? u.Army[factory.Index] : 0, [factory], Bp(bpId), 1)]);
        var cur = UnitHandle.None;
        for (int k = 0; k < maxTicks; k++)
        {
            sim.Step();
            var c = u.Factory[factory.Index].Current;
            if (!c.IsNone) cur = c;
            if (!cur.IsNone && c.IsNone && u.IsActive(cur) && u.IsComplete(cur.Index)) return cur;
        }
        Assert.Fail($"{bpId} not released");
        return UnitHandle.None;
    }

    /// <summary>Follows a released surface unit: never jumps, leaves the footprint and ends up near the exit.</summary>
    private static void AssertDrivesOut(Simulation sim, UnitHandle factory, UnitHandle unit, int ticks)
    {
        var w = sim.World;
        var u = w.Units;
        var ubp = w.BpOf(unit.Index);
        var fpos = u.Pos[factory.Index];
        float half = ConstructionSystem.HalfSize(w.BpOf(factory.Index));
        float maxStep = ubp.Movement.MaxSpeed * SimWorld.Dt * 2.5f + 0.2f;
        Assert.True(Vector2.Distance(u.Pos[unit.Index], fpos) <= maxStep, $"released at {u.Pos[unit.Index]}, factory {fpos}");
        Assert.True(u.Move[unit.Index].LeaveTime > 0, "should be leaving the factory");
        var last = u.Pos[unit.Index];
        bool outside = false;
        for (int k = 0; k < ticks; k++)
        {
            sim.Step();
            var p = u.Pos[unit.Index];
            Assert.True(Vector2.Distance(p, last) <= maxStep, $"jumped {Vector2.Distance(p, last):F2} at tick {k}");
            last = p;
            if (MathF.Max(MathF.Abs(p.X - fpos.X), MathF.Abs(p.Y - fpos.Y)) > half + 0.5f * ubp.Radius) outside = true;
        }
        Assert.True(outside, $"never left the factory footprint, at {last}");
        Assert.Equal(0f, u.Move[unit.Index].LeaveTime);
    }

    [Fact]
    public void TankDrivesOutOfTheLandFactoryAndReachesTheRally()
    {
        var sim = TestUtil.NewSkirmish(42);
        var w = sim.World;
        var site = FindSite(w, "uef_t1_land_factory", CommanderPos(w), 10);
        var f = TestUtil.Spawn(sim, "uef_t1_land_factory", 0, site);
        var rally = site + new Vector2(14, 3);
        sim.Step([new IssueOrderCommand(0, [f], Order.MoveTo(rally), false)]);
        var tank = BuildOne(sim, f, "uef_t1_tank");
        AssertDrivesOut(sim, f, tank, 60);
        Ticks(sim, 150);
        Assert.True(Vector2.Distance(w.Units.Pos[tank.Index], rally) < 2.5f, $"tank at {w.Units.Pos[tank.Index]}, rally {rally}");
    }

    [Fact]
    public void FrigateSailsOutOfTheNavalFactory()
    {
        var sim = NavalWorld.Archipelago(seed: 42);
        var w = sim.World;
        var t = w.Terrain;
        var site = NavalWorld.NavalSite(w, t.StartPositions[0]);
        var fac = w.SpawnUnit(Bp("uef_t1_naval_factory"), 0, site, DMath.HeadingOf(t.StartPositions[1] - site));
        var frigate = BuildOne(sim, fac, "uef_t1_frigate");
        AssertDrivesOut(sim, fac, frigate, 80);
        Assert.True(w.Units.Orders[frigate.Index].IsEmpty || w.Units.Move[frigate.Index].HasGoal);
    }

    [Fact]
    public void InterceptorTakesOffFromTheAirFactoryPad()
    {
        var sim = TestUtil.NewSkirmish(42);
        var w = sim.World;
        var site = FindSite(w, "uef_t1_air_factory", CommanderPos(w), 10);
        var f = TestUtil.Spawn(sim, "uef_t1_air_factory", 0, site);
        var plane = BuildOne(sim, f, "uef_t1_interceptor");
        var bp = TestUtil.Db["uef_t1_interceptor"];
        float ground = w.Terrain.GetSurfaceHeight(w.Units.Pos[plane.Index]);
        Assert.True(Vector2.Distance(w.Units.Pos[plane.Index], site) < bp.Movement.MaxSpeed * SimWorld.Dt * 2f + 0.5f, "takes off from the pad");
        Assert.True(w.Units.Elevation[plane.Index] < ground + bp.Movement.Altitude * 0.5f, "starts low");
        Ticks(sim, 60);
        float alt = w.Units.Elevation[plane.Index] - w.Terrain.GetSurfaceHeight(w.Units.Pos[plane.Index]);
        Assert.True(alt > bp.Movement.Altitude * 0.8f, $"should have climbed, altitude {alt:F1}");
    }
}

using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.Transport;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

/// <summary>Transports / carriers (wave 4h) meeting capture, pause and factory roll-off (wave 4f).</summary>
public class TransportPolishIntegrationTests
{
    private static void Fill(SimWorld w, int army = 0)
    {
        var e = w.Armies[army].Eco;
        e.Mass = e.MassMax;
        e.Energy = e.EnergyMax;
    }

    private static bool Carried(SimWorld w, UnitHandle h) => w.Units.IsActive(h) && (w.Units.Flags[h.Index] & UnitFlags.Carried) != 0;

    [Fact]
    public void CapturedTransportTakesItsCargoAlong()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var p = FindSite(w, "uef_t1_land_factory", CommanderPos(w), 40, 80);
        var tr = TestUtil.Spawn(sim, "uef_t1_transport", 1, p);
        var tanks = new[] { TestUtil.Spawn(sim, "uef_t1_tank", 1, p + new Vector2(3, 0)), TestUtil.Spawn(sim, "uef_t1_tank", 1, p + new Vector2(-3, 0)) };
        foreach (var t in tanks) TransportSystem.Board(w, tr.Index, t.Index);
        var eng = TestUtil.Spawn(sim, "uef_t1_engineer", 0, p + new Vector2(0, 5));
        sim.Step();
        Assert.All(tanks, t => Assert.True(Carried(w, t)));
        Assert.False(CaptureSystem.CanCapture(w, eng.Index, tanks[0].Index)); // carried: off the map

        var cmd = new IssueOrderCommand(0, [eng], Order.CaptureUnit(tr), false);
        bool captured = false;
        for (int t = 0; t < 2000 && !captured; t++)
        {
            Fill(w);
            sim.Step(t == 0 ? [cmd] : []);
            captured = w.Units.Army[tr.Index] == 0;
        }
        Assert.True(captured, "the transport should be captured");
        Assert.All(tanks, t =>
        {
            Assert.True(Carried(w, t)); // still aboard
            Assert.Equal(0, w.Units.Army[t.Index]); // the cargo changed army with its carrier
        });
        Assert.Equal(2, w.Cargo.Count(tr.Index));

        // The new owner can unload them.
        var drop = w.Terrain.Clamp(p + new Vector2(-12, 10));
        sim.Step([new IssueOrderCommand(0, [tr], TransportOrders.Unload(drop), false)]);
        for (int t = 0; t < 600 && tanks.Any(h => Carried(w, h)); t++) sim.Step();
        Assert.All(tanks, t => Assert.False(Carried(w, t)));
        Assert.All(tanks, t => Assert.Equal(0, w.Units.Army[t.Index]));
    }

    [Fact]
    public void TransportCarryingACommanderCantBeCaptured()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var com = w.Armies[1].Commander;
        var p = w.Units.Pos[com.Index];
        var tr = TestUtil.Spawn(sim, "uef_t1_transport", 1, p + new Vector2(4, 0));
        var eng = TestUtil.Spawn(sim, "uef_t1_engineer", 0, p + new Vector2(0, 5));
        sim.Step();
        Assert.True(CaptureSystem.CanCapture(w, eng.Index, tr.Index));
        TransportSystem.Board(w, tr.Index, com.Index);
        Assert.True(UnitTransfer.HoldsCommander(w, tr.Index));
        Assert.False(CaptureSystem.CanCapture(w, eng.Index, tr.Index));
    }

    [Fact]
    public void BoardingEndsTheFactoryRollOut()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var p = CommanderPos(w) + new Vector2(8, 8);
        var tr = TestUtil.Spawn(sim, "uef_t1_transport", 0, p);
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 0, p + new Vector2(3, 0));
        w.Units.Move[tank.Index].LeaveTime = 5f; // still driving out of its factory
        w.Units.Move[tank.Index].FactoryExit = p + new Vector2(20, 0);
        TransportSystem.Board(w, tr.Index, tank.Index);
        Assert.Equal(0f, w.Units.Move[tank.Index].LeaveTime);
    }

    [Fact]
    public void PausedAtlantisStopsBuildingAircraft()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var atl = TestUtil.Spawn(sim, "uef_x_atlantis", 0, CommanderPos(w) + new Vector2(0, 14));
        Assert.True(PauseSystem.CanPause(w.BpOf(atl.Index)));
        Fill(w);
        sim.Step([new FactoryEnqueueCommand(0, [atl], Bp("uef_t1_interceptor"), 1)]);
        TestUtil.Run(sim, 10);
        var cur = w.Units.Factory[atl.Index].Current;
        Assert.True(w.Units.IsActive(cur));
        sim.Step([new SetPausedCommand(0, [atl], true)]);
        sim.Step();
        float frac = w.Units.BuildFraction[cur.Index];
        for (int t = 0; t < 50; t++) { Fill(w); sim.Step(); }
        Assert.Equal(frac, w.Units.BuildFraction[cur.Index]);
        sim.Step([new SetPausedCommand(0, [atl], false)]);
        for (int t = 0; t < 20; t++) { Fill(w); sim.Step(); }
        Assert.True(w.Units.BuildFraction[cur.Index] > frac);
    }
}

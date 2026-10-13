using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.Systems;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

/// <summary>Engineers/commanders capturing enemy units (Economy/CaptureSystem, UnitTransfer).</summary>
public class CaptureTests
{
    private static void Fill(SimWorld w, int army = 0)
    {
        var e = w.Armies[army].Eco;
        e.Mass = e.MassMax;
        e.Energy = e.EnergyMax;
    }

    /// <summary>A spot well away from both commanders (nobody shoots at the test units).</summary>
    private static Vector2 Away(SimWorld w) => FindSite(w, "uef_t1_land_factory", CommanderPos(w), 40, 80);

    private static Command Capture(UnitHandle[] captors, UnitHandle target) => new IssueOrderCommand(0, captors, Order.CaptureUnit(target), false);

    /// <summary>Steps (army 0 kept full) until the target belongs to army 0; returns the ticks taken or -1.</summary>
    private static int StepUntilCaptured(Simulation sim, UnitHandle target, int maxTicks, Command? first = null)
    {
        var w = sim.World;
        for (int t = 1; t <= maxTicks; t++)
        {
            Fill(w);
            sim.Step(t == 1 && first != null ? [first] : []);
            if (w.Units.Army[target.Index] == 0) return t;
        }
        return -1;
    }

    [Fact]
    public void CaptureTakesTheExpectedTimeAndEnergyAndChangesArmy()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var p = Away(w);
        var pg = TestUtil.Spawn(sim, "uef_t1_power_generator", 1, p);
        var eng = TestUtil.Spawn(sim, "uef_t1_engineer", 0, p + new Vector2(0, 4));
        sim.Step();
        var stats = w.Armies[0].Stats;
        float e0 = stats.EnergySpent, m0 = stats.MassSpent;
        // BuildTime 125 / BuildRate 5 / 2 = 12.5 s = 125 ticks (paid at T, applied at T+1).
        int ticks = StepUntilCaptured(sim, pg, 400, Capture([eng], pg));
        Assert.InRange(ticks, 125, 127);
        Assert.Equal(750f, stats.EnergySpent - e0, 0); // target BuildCostEnergy
        Assert.Equal(0f, stats.MassSpent - m0, 3);
        Assert.Equal(0, w.Units.Army[pg.Index]);
        Assert.True(w.Units.IsActive(pg)); // same handle, new owner
        Assert.Equal(0f, w.Units.Builder[pg.Index].CaptureProgress);
        Fill(w);
        sim.Step();
        Assert.True(w.Units.Orders[eng.Index].IsEmpty); // the capture order completed
        Assert.Equal(BuilderMode.None, w.Units.Builder[eng.Index].Mode);
        // It now produces for army 0 (commander 20 + power generator 20).
        Assert.Equal(40f, w.Armies[0].Eco.EnergyIncome, 3);
        Assert.Equal(20f, w.Armies[1].Eco.EnergyIncome, 3);
    }

    [Fact]
    public void CapturedFactoryKeepsBuildingItsUnitForTheNewOwner()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var p = Away(w);
        var f = TestUtil.Spawn(sim, "uef_t1_land_factory", 1, p);
        var engs = new[] { new Vector2(0, 7), new Vector2(7, 0), new Vector2(0, -7) }
            .Select(d => TestUtil.Spawn(sim, "uef_t1_engineer", 0, p + d)).ToArray();
        Fill(w, 1);
        sim.Step([new FactoryEnqueueCommand(1, [f], Bp("uef_t1_tank"), 5)]);
        var cur = w.Units.Factory[f.Index].Current;
        Assert.True(w.Units.IsActive(cur));
        // BuildTime 300 / (3 × 5) / 2 = 10 s, before the tank inside (14 s) is done.
        int ticks = StepUntilCaptured(sim, f, 400, Capture(engs, f));
        Assert.InRange(ticks, 100, 102);
        Assert.True(w.Units.IsActive(cur) && !w.Units.IsComplete(cur.Index));
        Assert.Equal(0, w.Units.Army[cur.Index]); // the unit inside went with the factory
        Assert.Empty(w.Units.Factory[f.Index].Queue!);
        // Army 0 can use it now.
        for (int t = 0; t < 400; t++) { Fill(w); sim.Step(t == 0 ? [new FactoryEnqueueCommand(0, [f], Bp("uef_t1_tank"), 1)] : []); }
        Assert.Contains(UnitsOf(w, 0, "uef_t1_tank"), i => w.Units.IsComplete(i));
        Assert.Empty(UnitsOf(w, 1, "uef_t1_tank"));
    }

    [Fact]
    public void EnergyStallSlowsCapture()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var p = Away(w);
        var pg = TestUtil.Spawn(sim, "uef_t1_power_generator", 1, p);
        var eng = TestUtil.Spawn(sim, "uef_t1_engineer", 0, p + new Vector2(0, 4));
        sim.Step();
        for (int t = 0; t < 125; t++)
        {
            w.Armies[0].Eco.Energy = 0; // only the commander's 20 E/s: capture wants 60 E/s
            sim.Step(t == 0 ? [Capture([eng], pg)] : []);
        }
        Assert.Equal(1, w.Units.Army[pg.Index]);
        Assert.InRange(CaptureSystem.Progress(w, pg.Index), 0.25f, 0.4f);
    }

    [Fact]
    public void CommandersAndAlliesCantBeCaptured()
    {
        var sim = TestUtil.NewSkirmish(players: 3);
        var w = sim.World;
        w.Armies[2].Team = w.Armies[0].Team; // army 2 is our ally
        var p = Away(w);
        var enemyCom = w.Armies[1].Commander;
        var allyPg = TestUtil.Spawn(sim, "uef_t1_power_generator", 2, p + new Vector2(6, 0));
        var e1 = TestUtil.Spawn(sim, "uef_t1_engineer", 0, w.Units.Pos[enemyCom.Index] + new Vector2(0, 3));
        var e2 = TestUtil.Spawn(sim, "uef_t1_engineer", 0, p + new Vector2(4, 2));
        sim.Step([Capture([e1], enemyCom), new IssueOrderCommand(0, [e2], Order.CaptureUnit(allyPg), false)]);
        Assert.True(w.Intel.IsVisible(0, enemyCom.Index)); // rejected because it is a commander, not unseen
        Assert.True(w.Units.Orders[e1.Index].IsEmpty);
        Assert.True(w.Units.Orders[e2.Index].IsEmpty);
        Assert.Equal(1, w.Units.Army[enemyCom.Index]);
        Assert.Equal(2, w.Units.Army[allyPg.Index]);
        Assert.False(CaptureSystem.CanCapture(w, e1.Index, enemyCom.Index));
        Assert.False(UnitTransfer.Transfer(w, enemyCom.Index, 0));
    }

    [Fact]
    public void TwoEngineersCaptureTwiceAsFastAndSnapshotShowsProgress()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var p = Away(w);
        var pg = TestUtil.Spawn(sim, "uef_t1_power_generator", 1, p);
        var a = TestUtil.Spawn(sim, "uef_t1_engineer", 0, p + new Vector2(0, 4));
        var b = TestUtil.Spawn(sim, "uef_t1_engineer", 0, p + new Vector2(0, -4));
        sim.Step();
        Fill(w);
        sim.Step([Capture([a, b], pg)]);
        for (int t = 0; t < 20; t++) { Fill(w); sim.Step(); }
        var snap = new SimSnapshot();
        sim.WriteSnapshot(snap);
        var va = snap.Units[snap.SlotToView[a.Index]];
        var vt = snap.Units[snap.SlotToView[pg.Index]];
        Assert.Equal(BuilderMode.Capture, va.WorkMode);
        Assert.InRange(vt.CaptureFrac, 0.3f, 0.36f); // 20 ticks × 2 × 0.008
        Assert.Equal(vt.CaptureFrac, va.WorkProgress, 4);
        int ticks = StepUntilCaptured(sim, pg, 200);
        Assert.InRange(ticks + 21, 62, 65); // 125 / 2 ticks in total
    }

    [Fact]
    public void AbandonedCaptureResets()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var p = Away(w);
        var pg = TestUtil.Spawn(sim, "uef_t1_power_generator", 1, p);
        var eng = TestUtil.Spawn(sim, "uef_t1_engineer", 0, p + new Vector2(0, 4));
        sim.Step();
        for (int t = 0; t < 50; t++) { Fill(w); sim.Step(t == 0 ? [Capture([eng], pg)] : []); }
        Assert.InRange(CaptureSystem.Progress(w, pg.Index), 0.35f, 0.41f);
        for (int t = 0; t < CaptureSystem.ResetTicks + 2; t++) sim.Step(t == 0 ? [new StopCommand(0, [eng])] : []);
        Assert.Equal(0f, CaptureSystem.Progress(w, pg.Index));
        var snap = new SimSnapshot();
        sim.WriteSnapshot(snap);
        Assert.Equal(0f, snap.Units[snap.SlotToView[pg.Index]].CaptureFrac);
        for (int t = 0; t < 10; t++) { Fill(w); sim.Step(t == 0 ? [Capture([eng], pg)] : []); }
        Assert.InRange(CaptureSystem.Progress(w, pg.Index), 0.06f, 0.09f); // started again from zero
    }
}

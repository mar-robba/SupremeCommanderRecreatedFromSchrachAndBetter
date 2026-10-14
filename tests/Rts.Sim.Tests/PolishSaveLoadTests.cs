using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.Systems;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

/// <summary>Save → load in the middle of agent f's mechanics: a capture in progress, a paused factory, a unit driving
/// out of its factory, a veteran unit and the adjacency cache. The reload must be byte-identical and continue identically.</summary>
public class PolishSaveLoadTests
{
    [Fact]
    public void CapturePauseFactoryExitAndVeterancySurviveSaveLoad()
    {
        var sim = TestUtil.NewSkirmish(42);
        var w = sim.World;
        var u = w.Units;
        foreach (var a in w.Armies) { a.Eco.Mass = a.Eco.MassMax; a.Eco.Energy = a.Eco.EnergyMax; }
        var cmd = CommanderPos(w);
        var fsite = FindSite(w, "uef_t1_land_factory", cmd, 10);
        var f = TestUtil.Spawn(sim, "uef_t1_land_factory", 0, fsite);
        sim.Step([new FactoryEnqueueCommand(0, [f], Bp("uef_t1_tank"), 2)]);

        // Run until the first tank leaves the factory.
        UnitHandle tank = UnitHandle.None;
        for (int k = 0; k < 3000 && tank.IsNone; k++)
        {
            sim.Step();
            foreach (int i in UnitsOf(w, 0, "uef_t1_tank"))
                if (u.IsComplete(i) && u.Move[i].LeaveTime > 0) tank = u.HandleOf(i);
        }
        Assert.False(tank.IsNone, "no tank left the factory");
        u.Kills[tank.Index] = Veterancy.KillsPerLevel(w.BpOf(tank.Index)); // veteran 1

        // Enemy power generator captured by an engineer (away from our guns); the factory is paused mid-build.
        var psite = FindSite(w, "uef_t1_power_generator", cmd, 40, 60);
        var pgen = TestUtil.Spawn(sim, "uef_t1_power_generator", 1, psite);
        var eng = TestUtil.Spawn(sim, "uef_t1_engineer", 0, psite + new Vector2(0, 4));
        sim.Step([
            new IssueOrderCommand(0, [eng], Order.CaptureUnit(pgen), false),
            new SetPausedCommand(0, [f], true),
        ]);
        for (int k = 0; k < 400 && CaptureSystem.Progress(w, pgen.Index) <= 0.05f; k++) sim.Step();
        float p = CaptureSystem.Progress(w, pgen.Index);
        Assert.InRange(p, 0.05f, 0.95f);
        Assert.True(u.Move[tank.Index].LeaveTime > 0 || Vector2.Distance(u.Pos[tank.Index], fsite) > 3f);

        var saved = WorldState.Capture(sim);
        var b = WorldState.Restore(saved, w.Blueprints, w.Terrain);
        Assert.True(saved.AsSpan().SequenceEqual(WorldState.Capture(b)), "reload is not byte-identical");
        Assert.Equal(p, CaptureSystem.Progress(b.World, pgen.Index));
        Assert.True((b.World.Units.Flags[f.Index] & UnitFlags.Paused) != 0);

        for (int t = 0; t < 200; t += 20)
        {
            TestUtil.Run(sim, 20);
            TestUtil.Run(b, 20);
            Assert.Equal(sim.StateHash(), b.StateHash());
        }
        var bu = b.World.Units;
        Assert.True(bu.Army[pgen.Index] == 0, $"not captured: army {bu.Army[pgen.Index]}, active {bu.IsActive(pgen)}, progress {CaptureSystem.Progress(b.World, pgen.Index)}, saved at {p}, energy {b.World.Armies[0].Eco.Energy}, cmd order {(bu.Orders[b.World.Armies[0].Commander.Index].IsEmpty ? "none" : bu.Orders[b.World.Armies[0].Commander.Index].Current.Type.ToString())}");
        Assert.Equal(1, Veterancy.Level(b.World.BpOf(tank.Index), b.World.Units.Kills[tank.Index]));
    }
}

using Rts.Sim.Combat;
using Rts.Sim.Commander;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Commander enhancements together with pause and veterancy (blueprint variant swapped in place).</summary>
public class EnhancementPolishTests
{
    private static void StepRich(Simulation sim, params Command[] cmds)
    {
        foreach (var a in sim.World.Armies) { a.Eco.Mass = a.Eco.MassMax; a.Eco.Energy = a.Eco.EnergyMax; }
        sim.Step(cmds);
    }

    [Fact]
    public void PausedEnhancementStopsAndResumes()
    {
        var sim = TestUtil.NewSkirmish();
        sim.Step();
        var w = sim.World;
        var acu = w.Armies[0].Commander;
        int i = acu.Index;
        StepRich(sim, new EnhanceCommand(0, [acu], "HeavyAntimatterCannon"));
        for (int k = 0; k < 50; k++) StepRich(sim);
        Assert.True(w.Units.Upgrade[i].Active);

        StepRich(sim, new SetPausedCommand(0, [acu], true));
        StepRich(sim);
        float frozen = w.Units.Upgrade[i].Fraction;
        Assert.True(frozen > 0);
        for (int k = 0; k < 50; k++) StepRich(sim);
        Assert.Equal(frozen, w.Units.Upgrade[i].Fraction);
        Assert.True(w.Units.Upgrade[i].Active, "pausing must not cancel the enhancement");
        Assert.Equal(0f, w.Units.Econ[i].MassRequest);

        StepRich(sim, new SetPausedCommand(0, [acu], false));
        for (int k = 0; k < 20; k++) StepRich(sim);
        Assert.True(w.Units.Upgrade[i].Fraction > frozen, "resumes after unpausing");
    }

    [Fact]
    public void VeteranCommanderKeepsItsVeterancyThroughAnEnhancement()
    {
        var sim = TestUtil.NewSkirmish();
        sim.Step();
        var w = sim.World;
        var acu = w.Armies[0].Commander;
        int i = acu.Index;
        var basebp = w.BpOf(i);
        w.Units.Kills[i] = Veterancy.KillsPerLevel(basebp) * 2; // veteran level 2
        w.Units.Health[i] = Veterancy.MaxHealth(w, i) * 0.5f;

        var e = EnhancementCatalog.Find(basebp, "HeavyAntimatterCannon");
        int target = EnhancementCatalog.WithSlot(w.Blueprints, basebp, basebp.Enhancements[e].Slot, e);
        w.Units.Upgrade[i] = new UpgradeState { Active = true, TargetBp = target };
        UpgradeSystem.Complete(w, i);

        var nbp = w.BpOf(i);
        Assert.Equal(target, nbp.Index);
        Assert.Equal(2, Veterancy.Level(nbp, w.Units.Kills[i]));
        Assert.Equal(Veterancy.MaxHealth(w, i) * 0.5f, w.Units.Health[i], 1);
        Assert.True(Veterancy.MaxHealth(w, i) > nbp.MaxHealth);
    }
}

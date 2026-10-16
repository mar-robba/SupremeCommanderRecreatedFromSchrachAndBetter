using Rts.Sim.Combat;
using Rts.Sim.Commander;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Strategic;
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
    [Fact]
    public void StopCancelsAnEnhancementWithRefund()
    {
        var sim = TestUtil.NewSkirmish();
        sim.Step();
        var w = sim.World;
        var acu = w.Armies[0].Commander;
        int i = acu.Index;
        int bp0 = w.Units.Bp[i];
        StepRich(sim, new EnhanceCommand(0, [acu], "HeavyAntimatterCannon"));
        for (int k = 0; k < 50; k++) StepRich(sim);
        Assert.True(w.Units.Upgrade[i].Active);
        var eco = w.Armies[0].Eco;
        eco.Mass = 0;
        sim.Step([new StopCommand(0, [acu])]);
        Assert.False(w.Units.Upgrade[i].Active, "Stop cancels the enhancement");
        Assert.Equal(bp0, w.Units.Bp[i]);
        Assert.True(eco.Mass > 0, "the spent mass is refunded");
    }

    [Fact]
    public void ShieldKeepsItsHpThroughAnEnhancementChange()
    {
        var sim = TestUtil.NewSkirmish();
        sim.Step();
        var w = sim.World;
        var acu = w.Armies[0].Commander;
        int i = acu.Index;
        var s = StrategicState.Of(w);
        void Install(string id)
        {
            var bp = w.BpOf(i);
            int e = EnhancementCatalog.Find(bp, id);
            int target = EnhancementCatalog.WithSlot(w.Blueprints, bp, bp.Enhancements[e].Slot, e);
            w.Units.Upgrade[i] = new UpgradeState { Active = true, TargetBp = target };
            UpgradeSystem.Complete(w, i);
        }

        Install("PersonalShield");
        StepRich(sim);
        Assert.True(s.Shield[i].Up);
        float max = w.BpOf(i).Shield!.MaxHealth;
        s.Shield[i].Hp = max * 0.3f;
        s.Shield[i].RegenDelay = 100; // no regeneration during the test
        Install("ZephyrAmplifier"); // other slot: same shield, new blueprint
        Assert.True(s.Shield[i].Up);
        Assert.Equal(max * 0.3f, s.Shield[i].Hp, 2);

        Install("ShieldGeneratorField"); // bigger shield: HP kept, not refilled
        Assert.True(s.Shield[i].Up);
        Assert.Equal(max * 0.3f, s.Shield[i].Hp, 2);

        // A collapsed shield stays down (recharge time clamped to the new blueprint).
        s.Shield[i].Up = false;
        s.Shield[i].Hp = 0;
        s.Shield[i].RechargeLeft = 5f;
        Install("AdvancedEngineering");
        Assert.False(s.Shield[i].Up);
        Assert.Equal(MathF.Min(5f, w.BpOf(i).Shield!.RechargeTime), s.Shield[i].RechargeLeft, 3);
    }
}

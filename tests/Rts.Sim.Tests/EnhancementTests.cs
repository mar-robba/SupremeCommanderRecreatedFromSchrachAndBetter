using System.Numerics;
using Rts.Sim.Commander;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Strategic;
using Rts.Sim.Systems;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

/// <summary>Commander/SCU enhancements: blueprint variants, building over time, slots, prerequisites, refunds, effects.</summary>
public class EnhancementTests
{
    private static void Rich(SimWorld w)
    {
        foreach (var a in w.Armies) { a.Eco.Mass = a.Eco.MassMax; a.Eco.Energy = a.Eco.EnergyMax; }
    }

    private static void StepRich(Simulation sim, params Command[] cmds)
    {
        Rich(sim.World);
        sim.Step(cmds);
    }

    private static (Simulation sim, UnitHandle acu) NewGame()
    {
        var sim = TestUtil.NewSkirmish();
        sim.Step();
        return (sim, sim.World.Armies[0].Commander);
    }

    private static int Enh(SimWorld w, UnitHandle h, string id) => EnhancementCatalog.Find(w.BpOf(h.Index), id);

    /// <summary>Installs an enhancement at once (test shortcut for the build).</summary>
    private static void InstallNow(SimWorld w, UnitHandle h, string id)
    {
        var bp = w.BpOf(h.Index);
        int e = EnhancementCatalog.Find(bp, id);
        Assert.True(EnhancementCatalog.CanInstall(bp, e), $"can't install {id} on {bp.Id}");
        int target = EnhancementCatalog.WithSlot(w.Blueprints, bp, bp.Enhancements[e].Slot, e);
        w.Units.Upgrade[h.Index] = new UpgradeState { Active = true, TargetBp = target };
        UpgradeSystem.Complete(w, h.Index);
    }

    [Fact]
    public void EveryCombinationIsAGeneratedVariant()
    {
        var db = TestUtil.Db;
        var acu = db["uef_commander"];
        Assert.True(acu.IsEnhanceable && !acu.IsVariant);
        Assert.Equal(3 * 4 * 4, acu.EnhancementVariants.Length); // right arm 2, left arm 3, back 3 (+ empty)
        Assert.Equal(acu.EnhancementVariants.Length, acu.EnhancementVariants.Distinct().Count());
        foreach (int v in acu.EnhancementVariants)
        {
            var vb = db[v];
            Assert.Equal(acu.Index, vb.EnhancementBase);
            Assert.Equal(v, EnhancementCatalog.VariantIndex(db, acu, vb.Installed));
            if (v != acu.Index) { Assert.True(vb.IsVariant); Assert.Empty(vb.BuiltBy); }
        }
        var id = "uef_commander+AdvancedEngineering+HeavyAntimatterCannon+PersonalShield";
        Assert.True(db.TryGet(id, out var combo));
        Assert.Equal(acu.Weapons[0].Damage * 2, combo!.Weapons[0].Damage);
        Assert.NotNull(combo.Shield);
        Assert.Equal(3 * 2 * 3, db["uef_scu"].EnhancementVariants.Length);
    }

    [Fact]
    public void EnhancementBuildsOverTimeWithItsCostAndAppliesEffects()
    {
        var (sim, acu) = NewGame();
        var w = sim.World;
        int i = acu.Index;
        var basebp = w.BpOf(i);
        int hac = Enh(w, acu, "HeavyAntimatterCannon");
        int target = EnhancementCatalog.WithSlot(w.Blueprints, basebp, EnhancementSlot.RightArm, hac);
        var cost = UpgradeSystem.Cost(w, i, target);
        Assert.Equal((1000f, 20000f, 900f), cost);

        StepRich(sim, new EnhanceCommand(0, [acu], "HeavyAntimatterCannon"));
        Assert.True(w.Units.Upgrade[i].Active);
        int ticks = 1;
        float massRate = 0;
        while (w.Units.Bp[i] != target && ticks < 1200)
        {
            StepRich(sim);
            ticks++;
            if (ticks == 100) massRate = w.Units.Econ[i].MassRequest;
        }
        float expected = cost.Time / basebp.Economy.BuildRate / SimWorld.Dt; // 900 / 10 = 90 s
        Assert.InRange(ticks, expected - 5, expected + 10);
        Assert.Equal(cost.Mass / (cost.Time / basebp.Economy.BuildRate), massRate, 1);

        var bp = w.BpOf(i);
        Assert.True(w.Units.IsActive(acu));                 // same handle
        Assert.Equal(acu, w.Armies[0].Commander);            // still the commander
        Assert.Equal(hac, bp.Installed[(int)EnhancementSlot.RightArm]);
        Assert.Equal(basebp.Weapons[0].Damage * 2, bp.Weapons[0].Damage);
        Assert.Equal(basebp.Weapons[0].Range + 8, bp.Weapons[0].Range);
        Assert.False(w.Units.Upgrade[i].Active);
    }

    [Fact]
    public void SlotsArePrerequisiteChainsAndMutuallyExclusive()
    {
        var (sim, acu) = NewGame();
        var w = sim.World;
        int i = acu.Index;
        float hp0 = w.BpOf(i).MaxHealth;
        Assert.False(EnhancementSystem.CanStart(w, i, Enh(w, acu, "T3Engineering"))); // needs Advanced Engineering
        InstallNow(w, acu, "AdvancedEngineering");
        Assert.Equal(25f, w.BpOf(i).Economy.BuildRate);
        Assert.Contains(Bp("uef_t2_power_generator"), w.BpOf(i).BuildIndices);
        Assert.DoesNotContain(Bp("uef_x_fatboy"), w.BpOf(i).BuildIndices);
        Assert.True(EnhancementSystem.CanStart(w, i, Enh(w, acu, "T3Engineering")));
        Assert.False(EnhancementSystem.CanStart(w, i, Enh(w, acu, "ResourceAllocation"))); // same slot, taken
        Assert.True(EnhancementSystem.CanStart(w, i, Enh(w, acu, "ZephyrAmplifier")));      // other slot

        InstallNow(w, acu, "T3Engineering");
        var bp = w.BpOf(i);
        Assert.True(EnhancementCatalog.HasInstalled(bp, Enh(w, acu, "AdvancedEngineering")));
        Assert.Equal(60f, bp.Economy.BuildRate);            // 10 + 15 + 35: prerequisite effects stack
        Assert.Equal(hp0 + 6000, bp.MaxHealth);
        Assert.Equal(bp.MaxHealth, w.Units.Health[i]);       // health keeps its fraction
        Assert.Contains(Bp("uef_x_fatboy"), bp.BuildIndices);
        Assert.Contains(Bp("uef_x_mavor"), bp.BuildIndices);
        Assert.Contains(Bp("uef_t3_power_generator"), bp.BuildIndices);
        Assert.Contains(Bp("uef_t1_land_factory"), bp.BuildIndices);

        // Removing clears the whole slot, at once, without refund.
        var eco = w.Armies[0].Eco;
        eco.Mass = 0;
        StepRich(sim, new RemoveEnhancementCommand(0, [acu], EnhancementSlot.LeftArm));
        Assert.Equal(10f, w.BpOf(i).Economy.BuildRate);
        Assert.Equal(-1, w.BpOf(i).Installed[(int)EnhancementSlot.LeftArm]);
        Assert.Equal(TestUtil.Db["uef_commander"].Index, w.Units.Bp[i]);
    }

    [Fact]
    public void CancelRefundsAndAnyNewOrderInterrupts()
    {
        var (sim, acu) = NewGame();
        var w = sim.World;
        int i = acu.Index;
        int bp0 = w.Units.Bp[i];
        StepRich(sim, new EnhanceCommand(0, [acu], "HeavyAntimatterCannon"));
        for (int t = 0; t < 300; t++) StepRich(sim);
        float frac = w.Units.Upgrade[i].Fraction;
        Assert.InRange(frac, 0.25f, 0.4f);
        var eco = w.Armies[0].Eco;
        eco.Mass = 0;
        eco.Energy = 0;
        UpgradeSystem.Cancel(w, i);
        Assert.False(w.Units.Upgrade[i].Active);
        Assert.Equal(MathF.Min(eco.MassMax, 1000f * frac), eco.Mass, 1);
        Assert.Equal(MathF.Min(eco.EnergyMax, 20000f * frac), eco.Energy, 1);
        Assert.Equal(bp0, w.Units.Bp[i]);

        // A move order interrupts (and refunds) the enhancement.
        StepRich(sim, new EnhanceCommand(0, [acu], "ZephyrAmplifier"));
        for (int t = 0; t < 20; t++) StepRich(sim);
        Assert.True(w.Units.Upgrade[i].Active);
        StepRich(sim, new IssueOrderCommand(0, [acu], Order.MoveTo(w.Units.Pos[i] + new Vector2(5, 0)), false));
        Assert.False(w.Units.Upgrade[i].Active);
        Assert.Equal(bp0, w.Units.Bp[i]);
    }

    [Fact]
    public void EngineersAssistAnEnhancementWithGuard()
    {
        var (sim, acu) = NewGame();
        var w = sim.World;
        int i = acu.Index;
        var eng = TestUtil.Spawn(sim, "uef_t3_engineer", 0, w.Units.Pos[i] + new Vector2(3, 0));
        StepRich(sim, new EnhanceCommand(0, [acu], "HeavyAntimatterCannon"),
            new IssueOrderCommand(0, [eng], Order.GuardUnit(acu), false));
        int ticks = 1;
        while (w.Units.Upgrade[i].Active && ticks < 1000) { StepRich(sim); ticks++; }
        Assert.Equal(Enh(w, acu, "HeavyAntimatterCannon"), w.BpOf(i).Installed[(int)EnhancementSlot.RightArm]);
        Assert.True(ticks < 400, $"assisted enhancement took {ticks} ticks"); // 900 / (10 + 30) = 22.5 s
    }

    [Fact]
    public void ShieldAndMissilePackEnhancementsUseTheStrategicModules()
    {
        var (sim, acu) = NewGame();
        var w = sim.World;
        int i = acu.Index;
        var s = StrategicState.Of(w);
        float upkeep0 = w.BpOf(i).Economy.MaintenanceEnergy;

        InstallNow(w, acu, "PersonalShield");
        StepRich(sim);
        Assert.Equal(3f, w.BpOf(i).Shield!.Radius);
        Assert.True(s.Shield[i].Up);
        Assert.Equal(upkeep0 + 100, w.BpOf(i).Economy.MaintenanceEnergy);
        InstallNow(w, acu, "ShieldGeneratorField");
        StepRich(sim);
        Assert.Equal(18f, w.BpOf(i).Shield!.Radius);
        Assert.Equal(upkeep0 + 300, w.BpOf(i).Economy.MaintenanceEnergy);
        Assert.Equal(1, s.Units.Count(h => h == acu));

        StepRich(sim, new RemoveEnhancementCommand(0, [acu], EnhancementSlot.Back));
        Assert.Null(w.BpOf(i).Shield);
        InstallNow(w, acu, "TacticalMissilePack");
        var bp = w.BpOf(i);
        Assert.Equal(2, bp.Weapons.Length);
        Assert.Equal(1, bp.Silo!.WeaponIndex);
        Assert.True(SiloSystem.IsLauncher(bp));
        for (int t = 0; t < 400 && s.Silo[i].Stock < 1; t++) StepRich(sim);
        Assert.True(s.Silo[i].Stock >= 1, "the missile pack builds missiles");
        int stock = s.Silo[i].Stock;

        InstallNow(w, acu, "ZephyrAmplifier"); // other slot: the stock survives the blueprint swap
        Assert.Equal(stock, s.Silo[i].Stock);
        Assert.Equal(1, s.Units.Count(h => h == acu));
    }
}

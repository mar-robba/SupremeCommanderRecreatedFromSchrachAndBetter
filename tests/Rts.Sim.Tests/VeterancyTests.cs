using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.Systems;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

/// <summary>Veterancy in the sim (Combat/Veterancy): level-up heal, regen, repair/heal caps, snapshot.</summary>
public class VeterancyTests
{
    private static Vector2 Away(SimWorld w) => FindSite(w, "uef_t1_land_factory", CommanderPos(w), 30, 70);

    [Fact]
    public void TechOneUnitReachesLevelOneAtThreeKillsAndIsHealedByTheBonus()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var p = Away(w);
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 0, p);
        sim.Step(); // into the alive list (snapshot below)
        int t = tank.Index;
        var bp = w.BpOf(t);
        w.Units.Health[t] = 200;
        for (int k = 0; k < 3; k++)
        {
            var victim = TestUtil.Spawn(sim, "uef_t1_engineer", 1, p + new Vector2(0, 40 + 3 * k));
            w.KillUnit(victim.Index, tank);
            if (k < 2) Assert.Equal(200f, w.Units.Health[t]);
        }
        Assert.Equal(3, w.Units.Kills[t]);
        Assert.Equal(1, Veterancy.Level(bp, w.Units.Kills[t]));
        Assert.Equal(bp.MaxHealth * 1.1f, Veterancy.MaxHealth(w, t), 3);
        Assert.Equal(200f + bp.MaxHealth * 0.1f, w.Units.Health[t], 3);

        // Snapshot: effective max health and health fraction.
        var snap = new SimSnapshot();
        sim.WriteSnapshot(snap);
        var v = snap.Units[snap.SlotToView[t]];
        Assert.Equal(bp.MaxHealth * 1.1f, v.MaxHealth, 3);
        Assert.Equal(w.Units.Health[t] / (bp.MaxHealth * 1.1f), v.HealthFrac, 4);
    }

    [Fact]
    public void VeteranRegeneratesAndCapsAtEffectiveMaxHealth()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 0, Away(w));
        int t = tank.Index;
        var bp = w.BpOf(t);
        Assert.Equal(0f, bp.RegenRate);
        w.Units.Kills[t] = 6; // level 2: +2 HP/s
        w.Units.Health[t] = 100;
        EconomyTestUtil.Ticks(sim, 10);
        Assert.Equal(102f, w.Units.Health[t], 2);

        float max = Veterancy.MaxHealth(w, t);
        w.Units.Health[t] = max - 0.05f;
        EconomyTestUtil.Ticks(sim, 5);
        Assert.Equal(max, w.Units.Health[t], 3);
        Assert.True(max > bp.MaxHealth);
    }

    [Fact]
    public void RepairAndSandboxHealGoUpToTheEffectiveMaxHealth()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var p = Away(w);
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 0, p);
        var eng = TestUtil.Spawn(sim, "uef_t1_engineer", 0, p + new Vector2(3, 0));
        int t = tank.Index;
        w.Units.Kills[t] = 3; // max 330
        float max = Veterancy.MaxHealth(w, t);
        w.Units.Health[t] = w.BpOf(t).MaxHealth - 50; // 250: below both the blueprint and the effective max
        sim.Step([new IssueOrderCommand(0, [eng], Order.RepairUnit(tank), false)]);
        for (int k = 0; k < 600 && !w.Units.Orders[eng.Index].IsEmpty; k++) sim.Step();
        Assert.True(w.Units.Orders[eng.Index].IsEmpty, "repair should finish");
        Assert.Equal(max, w.Units.Health[t], 2);

        // Sandbox heal/damage use the effective max health too.
        SandboxSystem.Enable(w);
        w.Units.Health[t] = 10;
        sim.Step([new SandboxUnitActionCommand(0, [tank], SandboxUnitAction.Heal)]);
        Assert.Equal(max, w.Units.Health[t], 2);
        sim.Step([new SandboxUnitActionCommand(0, [tank], SandboxUnitAction.Damage, 0.5f)]);
        Assert.Equal(max * 0.5f + 0.1f, w.Units.Health[t], 2); // + one tick of level-1 regen (1 HP/s)
    }
}

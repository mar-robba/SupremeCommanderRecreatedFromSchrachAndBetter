using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Strategic;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Shield polish: upkeep/recharge of collapsed and paused shields, domes vs bombs and shells from above.</summary>
public class ShieldPolishTests
{
    private const string Gen = "uef_t2_shield_generator";

    /// <summary>T1 power generators (20 E/s each) in a far corner plus a full energy storage.</summary>
    private static void Power(Simulation sim, int army, float energyPerSecond)
    {
        int n = (int)MathF.Ceiling(energyPerSecond / 20f);
        float z = army == 0 ? 6 : sim.World.Terrain.Height - 6;
        for (int k = 0; k < n; k++) CombatWorld.Spawn(sim, "uef_t1_power_generator", army, 6 + k * 2.5f, z);
        CombatWorld.Spawn(sim, "uef_t1_energy_storage", army, sim.World.Terrain.Width - 8, z);
        sim.World.Armies[army].Eco.Energy = 5000;
    }

    private static ShieldState ShieldOf(Simulation sim, UnitHandle h) => StrategicState.Of(sim.World).Shield[h.Index];

    private static void Collapse(Simulation sim, UnitHandle gen)
    {
        var sb = TestUtil.Db[Gen].Shield!;
        Assert.True(ShieldSystem.Damage(sim.World, gen.Index, sb.MaxHealth * 2, new Vector3(64, 20, 64), 0, 0));
        Assert.False(ShieldSystem.IsUp(sim.World, gen.Index));
    }

    private static float Upkeep => TestUtil.Db[Gen].Economy.MaintenanceEnergy;

    // ------------------------------------------------------------------ upkeep / recharge

    [Fact]
    public void CollapsedShieldPaysNoUpkeep()
    {
        var sim = CombatWorld.Flat();
        Power(sim, 0, 200);
        var gen = CombatWorld.Spawn(sim, Gen, 0, 64, 64);
        for (int t = 0; t < 3; t++) sim.Step();
        Assert.True(ShieldSystem.IsUp(sim.World, gen.Index));
        float upDemand = sim.World.Armies[0].Eco.EnergyDemand;
        Assert.True(upDemand >= Upkeep - 0.01f, $"up shield demand {upDemand}");
        Collapse(sim, gen);
        for (int t = 0; t < 3; t++) sim.Step();
        Assert.False(ShieldSystem.IsUp(sim.World, gen.Index));
        Assert.Equal(upDemand - Upkeep, sim.World.Armies[0].Eco.EnergyDemand, 2);
    }

    [Fact]
    public void CollapsedShieldRechargesAndComesBack()
    {
        var sim = CombatWorld.Flat();
        Power(sim, 0, 200);
        var gen = CombatWorld.Spawn(sim, Gen, 0, 64, 64);
        var sb = TestUtil.Db[Gen].Shield!;
        sim.Step();
        Collapse(sim, gen);
        int ticks = (int)(sb.RechargeTime * SimWorld.TicksPerSecond);
        for (int t = 0; t < ticks / 2; t++) sim.Step();
        float left = ShieldOf(sim, gen).RechargeLeft;
        Assert.True(left < sb.RechargeTime * 0.6f && left > 0, $"recharge left {left}");
        for (int t = 0; t < ticks / 2 + 5; t++) sim.Step();
        Assert.True(ShieldSystem.IsUp(sim.World, gen.Index), "collapsed shield should come back after RechargeTime");
        Assert.Equal(sb.MaxHealth, ShieldOf(sim, gen).Hp);
        // Back up: it pays its upkeep again.
        sim.Step();
        Assert.True(sim.World.Armies[0].Eco.EnergyDemand >= Upkeep - 0.01f);
    }

    [Fact]
    public void CollapsedShieldDoesNotRechargeDuringEnergyStall()
    {
        var sim = CombatWorld.Flat();
        Power(sim, 0, 200);
        var gen = CombatWorld.Spawn(sim, Gen, 0, 64, 64);
        var sb = TestUtil.Db[Gen].Shield!;
        sim.Step();
        Collapse(sim, gen);
        // A T3 mass fabricator (3500 E/s upkeep) drains the storage in ~1.5 s, then the army stalls on energy.
        var fab = CombatWorld.Spawn(sim, "uef_t3_mass_fabricator", 0, 100, 100);
        int ticks = (int)(sb.RechargeTime * SimWorld.TicksPerSecond);
        for (int t = 0; t < 30; t++) sim.Step();
        Assert.True(sim.World.Armies[0].Eco.EnergyEfficiency < ShieldSystem.MinPowerEfficiency, "army should be stalling");
        float left = ShieldOf(sim, gen).RechargeLeft;
        for (int t = 0; t < ticks + 20; t++) sim.Step();
        Assert.False(ShieldSystem.IsUp(sim.World, gen.Index), "no recharge while the army stalls");
        Assert.Equal(left, ShieldOf(sim, gen).RechargeLeft, 3);
        // Stall over: the recharge resumes.
        sim.World.KillUnit(fab.Index, UnitHandle.None);
        for (int t = 0; t < 20; t++) sim.Step();
        Assert.True(ShieldOf(sim, gen).RechargeLeft < left - 1f, "recharge resumes after the stall");
    }

    [Fact]
    public void ShieldWithNoPowerNeverComesBack()
    {
        var sim = CombatWorld.Flat();
        // Army 1 has no power plants and no stored energy: no demand (efficiency 1) but nothing to pay the upkeep with.
        var gen = CombatWorld.Spawn(sim, Gen, 1, 64, 64);
        var sb = TestUtil.Db[Gen].Shield!;
        int ticks = (int)(sb.RechargeTime * SimWorld.TicksPerSecond);
        int ups = 0;
        for (int t = 0; t < ticks * 2; t++)
        {
            sim.Step();
            ups += sim.World.Events.Effects.Count(e => e.Kind == EffectKind.ShieldUp);
            sim.World.Events.Effects.Clear();
        }
        Assert.False(ShieldSystem.IsUp(sim.World, gen.Index));
        Assert.Equal(0, ups);
    }

    [Fact]
    public void PausedShieldIsDownAndCostsNothing()
    {
        var sim = CombatWorld.Flat();
        Power(sim, 0, 200);
        var gen = CombatWorld.Spawn(sim, Gen, 0, 64, 64);
        var sb = TestUtil.Db[Gen].Shield!;
        for (int t = 0; t < 3; t++) sim.Step();
        float upDemand = sim.World.Armies[0].Eco.EnergyDemand;
        sim.Step([new SetPausedCommand(0, [gen], true)]);
        sim.Step();
        Assert.False(ShieldSystem.IsUp(sim.World, gen.Index));
        Assert.Equal(upDemand - Upkeep, sim.World.Armies[0].Eco.EnergyDemand, 2);
        // While paused it never recharges.
        int ticks = (int)(sb.RechargeTime * SimWorld.TicksPerSecond);
        for (int t = 0; t < ticks + 20; t++) sim.Step();
        Assert.False(ShieldSystem.IsUp(sim.World, gen.Index));
        Assert.Equal(upDemand - Upkeep, sim.World.Armies[0].Eco.EnergyDemand, 2);
        // Unpaused: it recharges and comes back up.
        sim.Step([new SetPausedCommand(0, [gen], false)]);
        for (int t = 0; t < ticks + 10; t++) sim.Step();
        Assert.True(ShieldSystem.IsUp(sim.World, gen.Index));
    }
}

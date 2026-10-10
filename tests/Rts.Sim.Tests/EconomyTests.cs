using System.Numerics;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

public class EconomyTests
{
    [Fact]
    public void StartingStorageComesFromCommander()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var cbp = TestUtil.Db["uef_commander"].Economy;
        foreach (var a in w.Armies)
        {
            Assert.Equal(cbp.StorageMass, a.Eco.MassMax);
            Assert.Equal(cbp.StorageEnergy, a.Eco.EnergyMax);
            Assert.Equal(a.Eco.MassMax, a.Eco.Mass);
            Assert.Equal(a.Eco.EnergyMax, a.Eco.Energy);
        }
        sim.Step();
        var e = w.Armies[0].Eco;
        Assert.Equal(cbp.ProductionMass, e.MassIncome, 3);
        Assert.Equal(cbp.ProductionEnergy, e.EnergyIncome, 3);
        Assert.Equal(1f, e.MassEfficiency);
    }

    [Fact]
    public void StorageMaxIsSumOfCompleteUnits()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var site = FindSite(w, "uef_t1_mass_storage", CommanderPos(w), 6);
        TestUtil.Spawn(sim, "uef_t1_mass_storage", 0, site);
        // An unfinished storage adds nothing.
        w.SpawnUnit(Bp("uef_t1_energy_storage"), 0, FindSite(w, "uef_t1_energy_storage", CommanderPos(w), 6), 0, complete: false);
        sim.Step();
        Assert.Equal(650f + 500f, w.Armies[0].Eco.MassMax);
        Assert.Equal(4000f, w.Armies[0].Eco.EnergyMax);
    }

    [Fact]
    public void CommanderBuildsMassExtractorAndMassIncomeRises()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        sim.Step();
        var cmd = w.Armies[0].Commander;
        float before = w.Armies[0].Eco.MassIncome;
        var dep = NearestDeposit(w, CommanderPos(w), DepositType.Mass);
        // Click slightly off the deposit: the site must snap onto it.
        TestUtil.Run(sim, 200, new IssueOrderCommand(0, [cmd], Order.BuildAt(Bp("uef_t1_mass_extractor"), dep + new Vector2(1.2f, -0.8f)), false));

        var mexes = UnitsOf(w, 0, "uef_t1_mass_extractor");
        Assert.Single(mexes);
        int m = mexes[0];
        Assert.True(w.Units.IsComplete(m), $"mex fraction {w.Units.BuildFraction[m]}");
        Assert.True(Vector2.Distance(w.Units.Pos[m], dep) < 1f);
        Assert.True(w.Units.Orders[cmd.Index].IsEmpty);
        Assert.Equal(0, (int)(w.Units.Flags[cmd.Index] & UnitFlags.Building));
        Assert.True(w.Armies[0].Eco.MassIncome > before + 1.9f, $"income {w.Armies[0].Eco.MassIncome}");
        // Mex upkeep shows up as energy demand.
        Assert.Equal(2f, w.Armies[0].Eco.EnergyDemand, 3);
    }

    [Fact]
    public void PowerGeneratorRaisesEnergyIncome()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        sim.Step();
        var cmd = w.Armies[0].Commander;
        float before = w.Armies[0].Eco.EnergyIncome;
        var site = FindSite(w, "uef_t1_power_generator", CommanderPos(w), 8);
        TestUtil.Run(sim, 220, new IssueOrderCommand(0, [cmd], Order.BuildAt(Bp("uef_t1_power_generator"), site), false));
        var pgens = UnitsOf(w, 0, "uef_t1_power_generator");
        Assert.Single(pgens);
        Assert.True(w.Units.IsComplete(pgens[0]));
        Assert.Equal(before + 20f, w.Armies[0].Eco.EnergyIncome, 3);
    }

    [Fact]
    public void BuildCostIsPaidExactly()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        sim.Step();
        var cmd = w.Armies[0].Commander;
        var site = FindSite(w, "uef_t1_power_generator", CommanderPos(w), 3, 5); // in range: no walking
        w.Armies[0].Eco.Mass = 300;
        const int ticks = 200;
        TestUtil.Run(sim, ticks, new IssueOrderCommand(0, [cmd], Order.BuildAt(Bp("uef_t1_power_generator"), site), false));
        Assert.True(w.Units.IsComplete(UnitsOf(w, 0, "uef_t1_power_generator")[0]));
        // Commander makes 1 mass/s; the generator costs 75 mass.
        float expected = 300 + ticks * SimWorld.Dt * 1f - 75f;
        Assert.InRange(w.Armies[0].Eco.Mass, expected - 0.3f, expected + 0.3f);
    }

    [Fact]
    public void BuildProgressSlowsWhenEnergyStalls()
    {
        float Progress(bool stall, out float effE)
        {
            var sim = TestUtil.NewSkirmish();
            var w = sim.World;
            sim.Step();
            var cmd = w.Armies[0].Commander;
            var site = FindSite(w, "uef_t1_power_generator", CommanderPos(w), 3, 5);
            if (stall) w.Armies[0].Eco.Energy = 0;
            TestUtil.Run(sim, 60, new IssueOrderCommand(0, [cmd], Order.BuildAt(Bp("uef_t1_power_generator"), site), false));
            effE = w.Armies[0].Eco.EnergyEfficiency;
            return w.Units.BuildFraction[UnitsOf(w, 0, "uef_t1_power_generator")[0]];
        }

        float normal = Progress(false, out float effNormal);
        float stalled = Progress(true, out float effStalled);
        Assert.Equal(1f, effNormal);
        // Only the commander's 20 E/s for a 60 E/s build: ~1/3 speed.
        Assert.InRange(effStalled, 0.3f, 0.37f);
        Assert.True(stalled > 0, "some progress from the commander's own energy");
        Assert.True(stalled < normal * 0.45f, $"stalled {stalled} vs normal {normal}");
    }

    [Fact]
    public void MassExtractorOutputScalesWithEnergyEfficiency()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var dep = NearestDeposit(w, CommanderPos(w), DepositType.Mass);
        TestUtil.Spawn(sim, "uef_t1_mass_extractor", 0, dep);
        // 12 radars (5 E/s upkeep each) + mex (2) = 62 E/s demand vs 20 E/s from the commander, no storage left.
        var c = CommanderPos(w);
        for (int k = 0; k < 12; k++) TestUtil.Spawn(sim, "uef_t1_radar", 0, c + new Vector2(-14 + k % 4 * 3, -14 + k / 4 * 3));
        w.Armies[0].Eco.Energy = 0;
        sim.Step();
        var e = w.Armies[0].Eco;
        float effE = 20f / 62f;
        Assert.Equal(effE, e.EnergyEfficiency, 3);
        Assert.Equal(1f + 2f * effE, e.MassIncome, 3);
        Assert.Equal(0f, e.Energy, 3);
    }

    [Fact]
    public void ReclaimedWreckIncreasesMass()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var cmd = w.Armies[0].Commander;
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 1, CommanderPos(w) + new Vector2(14, 3));
        sim.Step();
        w.KillUnit(tank.Index, cmd);
        sim.Step();
        Assert.Equal(1, w.Props.Count);
        int p = w.Props.AliveList[0];
        float wreckMass = 56f * 0.81f;
        Assert.Equal(wreckMass, w.Props.Mass[p], 3);
        Assert.Equal(Bp("uef_t1_tank"), w.Props.BlueprintIndex[p]);

        // Snapshot exposes it.
        var snap = new Snapshot.SimSnapshot();
        sim.WriteSnapshot(snap);
        Assert.Equal(1, snap.PropCount);
        Assert.Equal(wreckMass, snap.Props[0].Mass, 3);

        w.Armies[0].Eco.Mass = 0;
        const int ticks = 120;
        TestUtil.Run(sim, ticks, new IssueOrderCommand(0, [cmd], Order.ReclaimAt(Core.UnitHandle.None, w.Props.Pos[p] + new Vector2(0.5f, 0)), false));
        Assert.Equal(0, w.Props.Count);
        float fromCommander = ticks * SimWorld.Dt * 1f;
        Assert.InRange(w.Armies[0].Eco.Mass, fromCommander + wreckMass - 0.3f, fromCommander + wreckMass + 0.3f);
        Assert.True(w.Units.Orders[cmd.Index].IsEmpty);
    }

    [Fact]
    public void UnfinishedSiteLeavesNoWreck()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var site = FindSite(w, "uef_t1_power_generator", CommanderPos(w), 8);
        var h = w.SpawnUnit(Bp("uef_t1_power_generator"), 0, site, 0, complete: false);
        sim.Step();
        w.KillUnit(h.Index, Core.UnitHandle.None);
        sim.Step();
        Assert.Equal(0, w.Props.Count);
    }
}

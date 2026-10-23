using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

/// <summary>In-place structure upgrades (UpgradeSystem) and the T2/T3 blueprints.</summary>
public class UpgradeTests
{
    private static (Simulation sim, UnitHandle h) WithStructure(string id, int seed = 42)
    {
        var sim = TestUtil.NewSkirmish(seed);
        var w = sim.World;
        var bp = TestUtil.Db[id];
        var site = bp.Economy.RequiresMassDeposit
            ? NearestDeposit(w, CommanderPos(w), DepositType.Mass)
            : FindSite(w, id, CommanderPos(w), 10);
        var h = TestUtil.Spawn(sim, id, 0, site);
        sim.Step();
        return (sim, h);
    }

    /// <summary>Keeps army storage full so efficiency stays 1 (tests measure time/cost, not income).</summary>
    private static void Rich(SimWorld w, int army = 0)
    {
        var e = w.Armies[army].Eco;
        e.Mass = e.MassMax;
        e.Energy = e.EnergyMax;
    }

    private static void StepRich(Simulation sim, int n, params Command[] first)
    {
        for (int t = 0; t < n; t++)
        {
            Rich(sim.World);
            sim.Step(t == 0 ? first : []);
        }
    }

    /// <summary>Steps (rich) until the unit has the blueprint or maxTicks pass; returns the ticks taken.</summary>
    private static int StepUntilBp(Simulation sim, UnitHandle h, string bpId, int maxTicks)
    {
        int bp = Bp(bpId);
        for (int t = 1; t <= maxTicks; t++)
        {
            StepRich(sim, 1);
            if (sim.World.Units.Bp[h.Index] == bp) return t;
        }
        return -1;
    }

    [Fact]
    public void UpgradeCompletesInPlaceWithCorrectCostKeepingHandleAndHealthFraction()
    {
        var (sim, f) = WithStructure("uef_t1_land_factory");
        var w = sim.World;
        var u = w.Units;
        var t1 = TestUtil.Db["uef_t1_land_factory"];
        var t2 = TestUtil.Db["uef_t2_land_factory"];
        u.Health[f.Index] = t1.MaxHealth * 0.5f;
        var rally = u.Pos[f.Index] + new Vector2(12, 0);
        sim.Step([new IssueOrderCommand(0, [f], Order.MoveTo(rally), false)]);
        var stats = w.Armies[0].Stats;
        float mass0 = stats.MassSpent, energy0 = stats.EnergySpent;
        int unitsBefore = w.Armies[0].UnitCount;

        StepRich(sim, 1, new UpgradeCommand(0, [f]));
        Assert.True(u.Upgrade[f.Index].Active);
        Assert.Equal(t2.Index, u.Upgrade[f.Index].TargetBp);
        // Own build power 20 on the T2 BuildTime 1800 = 90 s (+1 tick: pay first, then progress).
        int ticks = StepUntilBp(sim, f, "uef_t2_land_factory", 1000);
        Assert.InRange(ticks + 1, 895, 905);

        Assert.True(u.IsActive(f), "same handle after the upgrade");
        Assert.False(u.Upgrade[f.Index].Active);
        Assert.Equal(0.5f, u.Health[f.Index] / t2.MaxHealth, 3);
        Assert.Equal(unitsBefore, w.Armies[0].UnitCount);
        Assert.Equal(1, u.Orders[f.Index].Count); // rally orders survive
        Assert.Equal(t2.Economy.BuildCostMass, stats.MassSpent - mass0, 1f);
        Assert.Equal(t2.Economy.BuildCostEnergy, stats.EnergySpent - energy0, 5f);
        Assert.Equal(TestUtil.Db.IndexOf("uef_t3_land_factory"), w.BpOf(f.Index).UpgradesToIndex);
    }

    [Fact]
    public void MassExtractorKeepsProducingWhileUpgrading()
    {
        var (sim, m) = WithStructure("uef_t1_mass_extractor");
        var w = sim.World;
        var u = w.Units;
        StepRich(sim, 5, new UpgradeCommand(0, [m]));
        Assert.True(u.Upgrade[m.Index].Active);
        StepRich(sim, 300);
        Assert.True(u.Upgrade[m.Index].Fraction is > 0.2f and < 0.5f, $"progress {u.Upgrade[m.Index].Fraction}");
        Assert.Equal(2f, u.Econ[m.Index].MassProduction, 3); // still the T1 rate

        // T1 extractor upgrade rate 10 (default for structures without BuildRate) on BuildTime 900 = 90 s.
        int ticks = StepUntilBp(sim, m, "uef_t2_mass_extractor", 1000);
        Assert.True(ticks > 0);
        StepRich(sim, 2);
        Assert.Equal(6f, u.Econ[m.Index].MassProduction, 3);
    }

    [Fact]
    public void FactoryPausesProductionWhileUpgradingThenResumesItsQueue()
    {
        var (sim, f) = WithStructure("uef_t1_land_factory");
        var w = sim.World;
        var u = w.Units;
        // Tank: 280 / 20 = 14 s. Build half of it, then upgrade.
        StepRich(sim, 70, new FactoryEnqueueCommand(0, [f], Bp("uef_t1_tank"), 2));
        var tank = u.Factory[f.Index].Current;
        Assert.True(u.IsActive(tank));
        StepRich(sim, 1, new UpgradeCommand(0, [f]));
        float frac = u.BuildFraction[tank.Index];
        Assert.InRange(frac, 0.4f, 0.6f);

        StepRich(sim, 400);
        Assert.True(u.Upgrade[f.Index].Active);
        Assert.Equal(frac, u.BuildFraction[tank.Index], 3); // frozen inside the factory
        Assert.Equal(2, u.Factory[f.Index].Queue!.Count);
        Assert.Equal(tank, u.Factory[f.Index].Current);

        Assert.True(StepUntilBp(sim, f, "uef_t2_land_factory", 600) > 0);
        // T2 build power 40: the other half of the tank takes 3.5 s.
        StepRich(sim, 40);
        Assert.True(u.IsActive(tank) && u.IsComplete(tank.Index), "the paused tank is finished after the upgrade");
        Assert.Single(u.Factory[f.Index].Queue!);
        Assert.NotEqual(tank, u.Factory[f.Index].Current); // the second tank started
    }

    [Fact]
    public void CancelRefundsWhatWasSpent()
    {
        var (sim, f) = WithStructure("uef_t1_land_factory");
        var w = sim.World;
        var u = w.Units;
        StepRich(sim, 300, new UpgradeCommand(0, [f]));
        float frac = u.Upgrade[f.Index].Fraction;
        Assert.InRange(frac, 0.3f, 0.36f);
        var eco = w.Armies[0].Eco;
        eco.Mass = 100;
        eco.Energy = 100;
        float income = eco.MassIncome * SimWorld.Dt;
        sim.Step([new CancelUpgradeCommand(0, [f])]);

        var t2 = TestUtil.Db["uef_t2_land_factory"];
        Assert.False(u.Upgrade[f.Index].Active);
        Assert.Equal(Bp("uef_t1_land_factory"), u.Bp[f.Index]);
        Assert.Equal(100 + t2.Economy.BuildCostMass * frac + income, eco.Mass, 0.5f);
        Assert.True(eco.Energy > 100 + t2.Economy.BuildCostEnergy * frac - 5, $"energy {eco.Energy}");
        // Nothing more is spent on it afterwards.
        float spent = w.Armies[0].Stats.MassSpent;
        StepRich(sim, 20);
        Assert.Equal(spent, w.Armies[0].Stats.MassSpent, 0.01f);
    }

    [Fact]
    public void AssistingEngineersSpeedUpTheUpgrade()
    {
        var (sim, f) = WithStructure("uef_t1_land_factory");
        var w = sim.World;
        var fp = w.Units.Pos[f.Index];
        var e1 = TestUtil.Spawn(sim, "uef_t1_engineer", 0, fp + new Vector2(0, 5.5f));
        var e2 = TestUtil.Spawn(sim, "uef_t1_engineer", 0, fp + new Vector2(0, -5.5f));
        sim.Step();
        StepRich(sim, 1, new UpgradeCommand(0, [f]), new IssueOrderCommand(0, [e1, e2], Order.GuardUnit(f), false));
        // 20 + 2 × 5 = 30 build power on 1800 = 60 s.
        int ticks = StepUntilBp(sim, f, "uef_t2_land_factory", 1000);
        Assert.InRange(ticks, 590, 640);
        Assert.True(w.Units.IsActive(e1) && w.Units.Orders[e1.Index].Count == 1, "engineers keep guarding");
    }

    [Fact]
    public void UpgradeIsRejectedForSitesForeignArmiesAndTopTier()
    {
        var (sim, f) = WithStructure("uef_t1_land_factory");
        var w = sim.World;
        var u = w.Units;
        StepRich(sim, 2, new UpgradeCommand(1, [f])); // not the owner
        Assert.False(u.Upgrade[f.Index].Active);

        var site = FindSite(w, "uef_t1_power_generator", CommanderPos(w), 14);
        var pg = w.SpawnUnit(Bp("uef_t1_power_generator"), 0, site, 0, complete: false);
        StepRich(sim, 2, new UpgradeCommand(0, [pg])); // no UpgradesTo and unfinished
        Assert.False(u.Upgrade[pg.Index].Active);

        var site3 = FindSite(w, "uef_t3_land_factory", CommanderPos(w), 16, 50);
        var t3 = TestUtil.Spawn(sim, "uef_t3_land_factory", 0, site3);
        StepRich(sim, 2, new UpgradeCommand(0, [t3]));
        Assert.False(u.Upgrade[t3.Index].Active);
    }

    [Fact]
    public void RadarUpgradeExtendsRadarRange()
    {
        var (sim, r) = WithStructure("uef_t1_radar");
        var w = sim.World;
        var u = w.Units;
        var probe = w.Terrain.Clamp(u.Pos[r.Index] + Vector2.Normalize(new Vector2(w.Terrain.Width * 0.5f, w.Terrain.Height * 0.5f) - u.Pos[r.Index]) * 150);
        StepRich(sim, 3);
        Assert.False(w.Intel.IsPositionOnRadar(0, probe));
        StepRich(sim, 1, new UpgradeCommand(0, [r]));
        Assert.True(StepUntilBp(sim, r, "uef_t2_radar", 700) > 0); // 500 / 10 = 50 s
        StepRich(sim, 3);
        Assert.True(w.Intel.IsPositionOnRadar(0, probe));
        Assert.Equal(TestUtil.Db.IndexOf("uef_t3_omni_sensor"), w.BpOf(r.Index).UpgradesToIndex);
    }

    [Fact]
    public void T2EngineerBuildsT1AndT2StructuresButT1EngineerCannotBuildT2()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var cp = CommanderPos(w);
        var e2 = TestUtil.Spawn(sim, "uef_t2_engineer", 0, cp + new Vector2(3, 0));
        var e1 = TestUtil.Spawn(sim, "uef_t1_engineer", 0, cp + new Vector2(-3, 0));
        sim.Step();
        var siteA = FindSite(w, "uef_t2_point_defense", cp, 6);
        StepRich(sim, 30,
            new IssueOrderCommand(0, [e1], Order.BuildAt(Bp("uef_t2_point_defense"), siteA), false));
        Assert.Empty(UnitsOf(w, 0, "uef_t2_point_defense"));

        StepRich(sim, 60, new IssueOrderCommand(0, [e2], Order.BuildAt(Bp("uef_t2_point_defense"), siteA), false));
        Assert.Single(UnitsOf(w, 0, "uef_t2_point_defense"));
        var siteB = FindSite(w, "uef_t1_power_generator", cp, 6);
        StepRich(sim, 60, new IssueOrderCommand(0, [e2], Order.BuildAt(Bp("uef_t1_power_generator"), siteB), false));
        Assert.NotEmpty(UnitsOf(w, 0, "uef_t1_power_generator"));

        var t3 = TestUtil.Db["uef_t3_engineer"];
        foreach (var id in new[] { "uef_t1_mass_extractor", "uef_t2_power_generator", "uef_t3_power_generator", "uef_t3_land_factory" })
            Assert.Contains(Bp(id), t3.BuildIndices);
        Assert.DoesNotContain(Bp("uef_t3_power_generator"), TestUtil.Db["uef_t2_engineer"].BuildIndices);
    }

    [Fact]
    public void TechFactoriesBuildLowerTiersAndTheirOwn()
    {
        var db = TestUtil.Db;
        var t1Land = db["uef_t1_land_factory"].BuildIndices;
        var t2Land = db["uef_t2_land_factory"].BuildIndices;
        var t3Land = db["uef_t3_land_factory"].BuildIndices;
        Assert.All(t1Land, b => Assert.Contains(b, t2Land));
        Assert.All(t2Land, b => Assert.Contains(b, t3Land));
        Assert.Contains(Bp("uef_t2_heavy_tank"), t2Land);
        Assert.DoesNotContain(Bp("uef_t2_heavy_tank"), t1Land);
        Assert.Contains(Bp("uef_t3_heavy_bot"), t3Land);
        Assert.DoesNotContain(Bp("uef_t3_heavy_bot"), t2Land);
        var t2Air = db["uef_t2_air_factory"].BuildIndices;
        var t3Air = db["uef_t3_air_factory"].BuildIndices;
        Assert.All(db["uef_t1_air_factory"].BuildIndices, b => Assert.Contains(b, t2Air));
        Assert.All(t2Air, b => Assert.Contains(b, t3Air));
        Assert.Contains(Bp("uef_t3_strategic_bomber"), t3Air);
    }

    [Fact]
    public void EveryBlueprintLoadsWithTechCategoriesAndTargetLayers()
    {
        var db = TestUtil.Db;
        foreach (var bp in db.All)
        {
            foreach (var wpn in bp.Weapons)
                Assert.True(wpn.TargetMask != LayerMask.None, $"{bp.Id} weapon {wpn.Name} has no target layers");
            Assert.True(bp.Weapons.Length <= UnitStore.MaxWeapons, bp.Id);
            if (bp.Has(UnitCategory.Commander)) continue;
            var tierCat = bp.Tech switch { 1 => UnitCategory.Tech1, 2 => UnitCategory.Tech2, 3 => UnitCategory.Tech3, _ => UnitCategory.Experimental };
            Assert.True(bp.Has(tierCat), $"{bp.Id}: Tech {bp.Tech} but categories {bp.CategoryFlags}");
            if (bp.IsMobile) Assert.NotEqual(MotionLayer.None, bp.Movement.Layer);
            Assert.NotEqual("box", bp.Visual.Shape);
            if (bp.UpgradesToIndex >= 0)
            {
                var to = db[bp.UpgradesToIndex];
                Assert.True(bp.IsStructure && to.IsStructure, bp.Id);
                // One tier up; shield chains (Cybran ED1..ED5) may also step within a tier.
                if (bp.Has(UnitCategory.Shield)) Assert.True(to.Tech == bp.Tech || to.Tech == bp.Tech + 1, bp.Id);
                else Assert.Equal(bp.Tech + 1, to.Tech);
            }
        }
        // The upgrade chains of this wave.
        foreach (var (a, b, c) in new[]
                 {
                     ("uef_t1_land_factory", "uef_t2_land_factory", "uef_t3_land_factory"),
                     ("uef_t1_air_factory", "uef_t2_air_factory", "uef_t3_air_factory"),
                     ("uef_t1_mass_extractor", "uef_t2_mass_extractor", "uef_t3_mass_extractor"),
                     ("uef_t1_radar", "uef_t2_radar", "uef_t3_omni_sensor"),
                 })
        {
            Assert.Equal(db.IndexOf(b), db[a].UpgradesToIndex);
            Assert.Equal(db.IndexOf(c), db[b].UpgradesToIndex);
            Assert.Equal(db.IndexOf(a), db[b].UpgradesFromIndex);
        }
    }

    /// <summary>Sandbox contract: every T2/T3 blueprint spawns fully built through the debug spawn command and survives
    /// a few ticks of the full sim (modules, weapons, intel, economy initialised).</summary>
    [Fact]
    public void EveryTechBlueprintSpawnsCompleteThroughDebugSpawn()
    {
        var db = TestUtil.Db;
        int checkedCount = 0;
        for (int b = 0; b < db.Count; b++)
        {
            var bp = db[b];
            if (bp.Tech < 2) continue;
            var sim = TestUtil.NewSkirmish(42);
            var w = sim.World;
            Vector2 pos;
            if (Rts.Sim.Naval.NavalRules.IsWaterStructure(bp))
            {
                // Water structures: nearest water site (the map may have no sea at all; NavalTests cover them).
                if (!Rts.Sim.Naval.NavalRules.FindSite(w.Occupancy, w.Terrain, bp, CommanderPos(w), 400f, out pos)) continue;
            }
            else pos = bp.Economy.RequiresMassDeposit
                ? NearestDeposit(w, CommanderPos(w), DepositType.Mass)
                : bp.IsStructure ? FindSite(w, bp.Id, CommanderPos(w), 12) : CommanderPos(w) + new Vector2(8, 8);
            sim.Step([new DebugSpawnCommand(0, b, pos, 1)]);
            for (int t = 0; t < 20; t++) { Rich(w); sim.Step(); }
            int found = -1;
            for (int k = 0; k < w.Units.AliveCount; k++)
            {
                int i = w.Units.AliveList[k];
                if (w.Units.Bp[i] == b && w.Units.Army[i] == 0) { found = i; break; }
            }
            Assert.True(found >= 0, $"{bp.Id} did not spawn");
            Assert.True((w.Units.Flags[found] & UnitFlags.UnderConstruction) == 0, $"{bp.Id} spawned unfinished");
            Assert.Equal(bp.MaxHealth, w.Units.Health[found], 1);
            checkedCount++;
        }
        Assert.True(checkedCount >= 30, $"only {checkedCount} T2/T3 blueprints");
    }

    [Fact]
    public void UpgradesAreDeterministicAcrossThreadCounts()
    {
        ulong RunWith(int threads)
        {
            int old = Par.MaxThreads;
            Par.MaxThreads = threads;
            try
            {
                var (sim, f) = WithStructure("uef_t1_land_factory", seed: 7);
                var w = sim.World;
                var m = TestUtil.Spawn(sim, "uef_t1_mass_extractor", 0, NearestDeposit(w, CommanderPos(w), DepositType.Mass));
                var fp = w.Units.Pos[f.Index];
                var e1 = TestUtil.Spawn(sim, "uef_t1_engineer", 0, fp + new Vector2(0, 5.5f));
                sim.Step();
                sim.Step([
                    new FactoryEnqueueCommand(0, [f], Bp("uef_t1_tank"), 3),
                    new UpgradeCommand(0, [m]),
                    new IssueOrderCommand(0, [e1], Order.GuardUnit(m), false),
                ]);
                for (int t = 0; t < 100; t++) sim.Step();
                sim.Step([new UpgradeCommand(0, [f])]);
                for (int t = 0; t < 300; t++) sim.Step();
                return sim.StateHash();
            }
            finally { Par.MaxThreads = old; }
        }

        Assert.Equal(RunWith(1), RunWith(8));
    }
}

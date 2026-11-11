using Rts.Sim.Commander;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.Strategic;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>The Cybran roster (data/factions/cybran.json + data/blueprints/units/cybran_*.json, wave 5 l).</summary>
public class CybranRosterTests
{
    private const string F = "cybran";
    private static BlueprintDb Db => TestUtil.Db;

    private static IEnumerable<UnitBlueprint> Cybran => Db.All.Where(b => !b.IsVariant && b.Faction == F);

    private static void Rich(SimWorld w)
    {
        foreach (var a in w.Armies) { a.Eco.Mass = a.Eco.MassMax; a.Eco.Energy = a.Eco.EnergyMax; }
    }

    private static void StepRich(Simulation sim, int ticks, params Command[] first)
    {
        for (int t = 0; t < ticks; t++)
        {
            Rich(sim.World);
            sim.Step(t == 0 ? first : []);
        }
    }

    [Fact]
    public void CybranFactionLoadsAndFillsEveryRequiredRole()
    {
        var f = Db.Factions.Get(F);
        Assert.NotNull(f);
        Assert.Equal("Cybran", f!.Name);
        Assert.NotEqual(0u, f.ColorRgba);
        Assert.Equal(Db.IndexOf("cybran_commander"), f.CommanderIndex);
        foreach (var role in FactionRoles.Required)
        {
            int bp = f.Role(role);
            Assert.True(bp >= 0, $"cybran misses required role {role}");
            Assert.Equal(F, Db[bp].Faction);
        }
        // Cybran-specific optional roles; the UEF-only ones stay empty.
        foreach (var role in new[] { FactionRoles.LightAssaultT1, FactionRoles.MobileStealthT2, FactionRoles.StealthFieldT2,
                     FactionRoles.ExpAir, FactionRoles.ExpArtillery, FactionRoles.ShieldEd2T2, FactionRoles.ShieldEd3T2,
                     FactionRoles.ShieldEd4T3, FactionRoles.ShieldEd5T3 })
            Assert.True(f.HasRole(role), role);
        foreach (var role in new[] { FactionRoles.ShieldT3, FactionRoles.MobileShieldT2, FactionRoles.PdT3, FactionRoles.ExpNaval,
                     FactionRoles.FighterBomberT2, FactionRoles.SiegeT3 })
            Assert.False(f.HasRole(role), role);
        Assert.Equal(Db.IndexOf("cybran_t2_shield_ed1"), f.Role(FactionRoles.ShieldT2));
        Assert.Equal(Db.IndexOf("cybran_t1_assault_bot"), f.Role(FactionRoles.TankT1));
    }

    [Fact]
    public void EveryCybranBlueprintLoadsWithACybranShape()
    {
        var all = Cybran.ToList();
        Assert.Equal(84, all.Count); // 80 (wave 5l) + wall, T3 sonar, carrier, strategic missile sub (wave 8a)
        foreach (var b in all)
        {
            Assert.StartsWith("cybran_", b.Id);
            Assert.StartsWith("cy_", b.Visual.Shape);
            Assert.True(b.Economy.BuildCostMass > 0 && b.Economy.BuildTime > 0, b.Id);
            Assert.True(b.MaxHealth > 0, b.Id);
            // Cybran build lists stay inside the faction.
            Assert.All(b.BuildIndices, k => Assert.Equal(F, Db[k].Faction));
            if (b.UpgradesToIndex >= 0) Assert.Equal(F, Db[b.UpgradesToIndex].Faction);
        }
        // Air factories share the land factory models but are air factories.
        for (int t = 1; t <= 3; t++)
        {
            var air = Db[$"cybran_t{t}_air_factory"];
            Assert.Equal(Db[$"cybran_t{t}_land_factory"].Visual.Shape, air.Visual.Shape);
            Assert.True(air.Has(UnitCategory.Air | UnitCategory.Factory));
            Assert.Equal("cy_naval_factory", Db[$"cybran_t{t}_naval_factory"].Visual.Shape);
        }
    }

    [Fact]
    public void EveryCybranBlueprintIsReachableFromTheCommander()
    {
        int start = Db.IndexOf("cybran_commander");
        var seen = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var bp = Db[queue.Dequeue()];
            var next = new List<int>(bp.BuildIndices);
            if (bp.UpgradesToIndex >= 0) next.Add(bp.UpgradesToIndex);
            foreach (int v in bp.EnhancementVariants) next.AddRange(Db[v].BuildIndices); // T2/T3 Engineering lists
            foreach (int n in next)
                if (seen.Add(n)) queue.Enqueue(n);
        }
        var missing = Cybran.Where(b => !seen.Contains(b.Index)).Select(b => b.Id).ToList();
        Assert.True(missing.Count == 0, "not reachable from cybran_commander: " + string.Join(", ", missing));
        Assert.DoesNotContain(seen, i => Db[i].Faction != F);
    }

    [Fact]
    public void UpgradeChainsAreComplete()
    {
        void Chain(params string[] ids)
        {
            for (int k = 0; k + 1 < ids.Length; k++)
            {
                Assert.Equal(Db.IndexOf(ids[k + 1]), Db[ids[k]].UpgradesToIndex);
                Assert.Equal(Db.IndexOf(ids[k]), Db[ids[k + 1]].UpgradesFromIndex);
            }
            Assert.Equal(-1, Db[ids[^1]].UpgradesToIndex);
        }
        foreach (var kind in new[] { "land_factory", "air_factory", "naval_factory", "mass_extractor" })
            Chain($"cybran_t1_{kind}", $"cybran_t2_{kind}", $"cybran_t3_{kind}");
        Chain("cybran_t1_radar", "cybran_t2_radar", "cybran_t3_omni_sensor");
        Chain("cybran_t1_sonar", "cybran_t2_sonar", "cybran_t3_sonar");
        Chain("cybran_t2_shield_ed1", "cybran_t2_shield_ed2", "cybran_t2_shield_ed3", "cybran_t3_shield_ed4", "cybran_t3_shield_ed5");
        Assert.Equal(-1, Db["cybran_t2_shield_ed1"].UpgradesFromIndex);
        Assert.Equal(3, Db["cybran_t3_shield_ed4"].Tech);

        // ED2..ED5 are reachable only by upgrading: no builder (engineer, commander, variant) lists them.
        var edUpgrades = new[] { "cybran_t2_shield_ed2", "cybran_t2_shield_ed3", "cybran_t3_shield_ed4", "cybran_t3_shield_ed5" }
            .Select(Db.IndexOf).ToHashSet();
        foreach (var b in Db.All) Assert.DoesNotContain(b.BuildIndices, edUpgrades.Contains);
        Assert.Contains(Db.IndexOf("cybran_t2_shield_ed1"), Db["cybran_t2_engineer"].BuildIndices);
    }

    [Fact]
    public void CybranSpecificsAreInTheData()
    {
        // Seabed walkers: amphibious; the Wagner has a torpedo, the Monkeylord's guns don't fire submerged.
        var wagner = Db["cybran_t2_amphibious_tank"];
        var ml = Db["cybran_x_monkeylord"];
        Assert.Equal(MotionLayer.Amphibious, wagner.Movement.Layer);
        Assert.Equal(MotionLayer.Amphibious, ml.Movement.Layer);
        Assert.Contains(wagner.Weapons, wp => wp.ProjectileVisual == "torpedo");
        Assert.DoesNotContain(ml.Weapons, wp => wp.FiresSubmerged || wp.ProjectileVisual == "torpedo");
        Assert.Equal(4, ml.Tech);
        Assert.Equal(MotionLayer.Air, Db["cybran_x_soul_ripper"].Movement.Layer);
        Assert.True(Db["cybran_x_scathis"].IsMobile);
        // Stealth fields.
        Assert.True(Db["cybran_t2_mobile_stealth"].Intel.StealthFieldRadius > 0 && Db["cybran_t2_mobile_stealth"].Economy.MaintenanceEnergy > 0);
        Assert.True(Db["cybran_t2_stealth_field_generator"].Intel.StealthFieldRadius >= 25);
        // EMP: the Medusa's shells, and the SCU's EMP Array enhancement adds a stun weapon.
        Assert.Contains(Db["cybran_t1_artillery"].Weapons, wp => wp.StunDuration > 0);
        var scu = Db["cybran_scu"];
        int emp = EnhancementCatalog.Find(scu, "EMPCharge");
        Assert.True(emp >= 0);
        var withEmp = Db[EnhancementCatalog.WithSlot(Db, scu, scu.Enhancements[emp].Slot, emp)];
        Assert.Contains(withEmp.Weapons, wp => wp.StunDuration > 0 && wp.StunRadius > 0);
        // Cybran commander enhancements: stealth -> cloak, engineering lists.
        var acu = Db["cybran_commander"];
        foreach (var id in new[] { "StealthGenerator", "CloakingGenerator", "MicrowaveLaserGenerator", "NaniteTorpedoLauncher",
                     "CoolingUpgrade", "AdvancedEngineering", "T3Engineering", "ResourceAllocation" })
            Assert.True(EnhancementCatalog.Find(acu, id) >= 0, id);
        Assert.Equal("StealthGenerator", acu.Enhancements[EnhancementCatalog.Find(acu, "CloakingGenerator")].Prerequisite);
    }

    [Fact]
    public void ShieldUpgradesInPlaceFromEd1ToEd5()
    {
        var sim = CybranTestWorld.Flat(Db, 128);
        var w = sim.World;
        CybranTestWorld.Power(sim, 0, 4);
        CybranTestWorld.Spawn(sim, "uef_t1_mass_storage", 0, 20, 30); // storage that Rich() refills every tick
        CybranTestWorld.Spawn(sim, "uef_t1_energy_storage", 0, 24, 30);
        var h = CybranTestWorld.Spawn(sim, "cybran_t2_shield_ed1", 0, 64, 64);
        StepRich(sim, (int)(Db["cybran_t2_shield_ed1"].Shield!.RechargeTime * 10) + 10); // a new shield charges up first
        Assert.True(ShieldSystem.IsUp(w, h.Index));
        var ids = new[] { "cybran_t2_shield_ed2", "cybran_t2_shield_ed3", "cybran_t3_shield_ed4", "cybran_t3_shield_ed5" };
        foreach (var id in ids)
        {
            var before = w.BpOf(h.Index);
            Assert.True(UpgradeSystem.CanStart(w, h.Index), before.Id);
            int target = Db.IndexOf(id);
            StepRich(sim, 1, new UpgradeCommand(0, [h]));
            int ticks = 0;
            while (w.Units.Bp[h.Index] != target && ticks++ < 3000) StepRich(sim, 1);
            Assert.Equal(target, w.Units.Bp[h.Index]);
            Assert.True(w.Units.IsActive(h), "same handle after the upgrade");
            var now = w.BpOf(h.Index);
            Assert.True(now.Shield!.Radius > before.Shield!.Radius, id);
            Assert.True(now.Shield.MaxHealth > before.Shield.MaxHealth, id);
            StepRich(sim, 40);
            Assert.True(ShieldSystem.IsUp(w, h.Index), $"{id}: shield up after the upgrade");
            float hp = StrategicState.Of(w).Shield[h.Index].Hp;
            Assert.True(hp > before.Shield.MaxHealth && hp <= now.Shield.MaxHealth, $"{id}: shield HP {hp}");
        }
        Assert.False(UpgradeSystem.CanStart(w, h.Index)); // ED5 is the top
    }

    [Fact]
    public void CommanderStealthThenCloakEnhancementsHideIt()
    {
        var sim = CybranTestWorld.Flat(Db, 256);
        var w = sim.World;
        CybranTestWorld.Power(sim, 1, 3);
        CybranTestWorld.Spawn(sim, "cybran_t1_radar", 1, 40, 128);
        var acu = CybranTestWorld.Spawn(sim, "cybran_commander", 0, 120, 128);
        StepRich(sim, 3, new SetFireStateCommand(0, [acu], FireState.HoldFire));
        Assert.True(w.Intel.IsBlip(1, acu.Index), "a plain commander in radar range is a blip");

        void Install(string enh)
        {
            StepRich(sim, 1, new EnhanceCommand(0, [acu], enh));
            int ticks = 0;
            while (ticks++ < 3000 && !EnhancementCatalog.HasInstalled(w.BpOf(acu.Index), EnhancementCatalog.Find(w.BpOf(acu.Index), enh)))
                StepRich(sim, 1);
            Assert.True(EnhancementCatalog.HasInstalled(w.BpOf(acu.Index), EnhancementCatalog.Find(w.BpOf(acu.Index), enh)), enh);
            StepRich(sim, 5);
        }

        Install("StealthGenerator");
        Assert.True(w.BpOf(acu.Index).Intel.RadarStealth);
        Assert.False(w.Intel.IsDetected(1, acu.Index), "radar stealth: no blip");
        Assert.True((StealthSystem.StatusOf(w, acu.Index, 0) & UnitStatus.RadarStealth) != 0);
        var watcher = CybranTestWorld.Spawn(sim, "uef_t1_tank", 1, 110, 128);
        StepRich(sim, 3, new SetFireStateCommand(1, [watcher], FireState.HoldFire));
        Assert.True(w.Intel.IsVisible(1, acu.Index), "stealth doesn't hide from vision");

        Install("CloakingGenerator");
        Assert.True(w.BpOf(acu.Index).Intel.Cloak);
        Assert.False(w.Intel.IsVisible(1, acu.Index), "cloaked: invisible to the watcher");
        Assert.False(w.Intel.IsDetected(1, acu.Index), "cloaked: no blip either");
        Assert.True((StealthSystem.StatusOf(w, acu.Index, 0) & UnitStatus.Cloaked) != 0);
        Assert.True(w.BpOf(acu.Index).Economy.MaintenanceEnergy >= 500, "cloak has a big upkeep");
    }
}

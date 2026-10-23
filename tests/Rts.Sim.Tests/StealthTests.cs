using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.Snapshot;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>
/// Private blueprint db + flat worlds for the Cybran sim-feature tests (stealth, EMP, seabed walkers, shield chain).
/// The cybran_* JSON files are not required: UEF blueprints of a private copy get the new fields.
/// </summary>
internal static class CybranTestWorld
{
    private static BlueprintDb? _stealthDb;

    /// <summary>Private copy: mobile AA = radar stealth, T1 engineer and mass storage = cloak, energy storage =
    /// stealth field generator (radius 20, 20 E/s upkeep).</summary>
    public static BlueprintDb StealthDb => _stealthDb ??= BuildStealthDb();

    private static BlueprintDb BuildStealthDb()
    {
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        db["uef_t1_mobile_aa"].Intel.RadarStealth = true;
        db["uef_t1_engineer"].Intel.Cloak = true;
        db["uef_t1_mass_storage"].Intel.Cloak = true;
        var gen = db["uef_t1_energy_storage"];
        gen.Intel.StealthFieldRadius = 20;
        gen.Economy.MaintenanceEnergy = 20;
        return db;
    }

    /// <summary>Flat map (height 10, no water) with one army per entry of <paramref name="teams"/>.</summary>
    public static Simulation Flat(BlueprintDb db, int size, params int[] teams)
    {
        if (teams.Length == 0) teams = [0, 1];
        var heights = new float[(size + 1) * (size + 1)];
        Array.Fill(heights, 10f);
        var terrain = new Terrain(size, size, heights, 0f, [], [new Vector2(20, 20), new Vector2(size - 20, size - 20)], 1);
        var armies = teams.Select((t, a) => new Army { Index = a, Team = t, Name = $"A{a}" }).ToArray();
        return new Simulation(new SimWorld(db, terrain, armies, 1, unitCapacity: 1024, projectileCapacity: 4096));
    }

    public static UnitHandle Spawn(Simulation sim, string bp, int army, float x, float z, float heading = 0) =>
        sim.World.SpawnUnit(sim.World.Blueprints.IndexOf(bp), army, new Vector2(x, z), heading, complete: true);

    /// <summary>Power generators (20 E/s each) in a row at the army's map edge (z = 6 / height - 6 / 12).</summary>
    public static void Power(Simulation sim, int army, int pgens)
    {
        float z = army switch { 0 => 6, 1 => sim.World.Terrain.Height - 6, _ => 14 };
        for (int k = 0; k < pgens; k++) Spawn(sim, "uef_t1_power_generator", army, 8 + k * 3, z);
    }

    public static void Run(Simulation sim, int ticks)
    {
        for (int t = 0; t < ticks; t++) sim.Step();
    }

    public static UnitHandle Target(Simulation sim, UnitHandle h, int wi = 0) =>
        sim.World.Units.Weapons[h.Index * UnitStore.MaxWeapons + wi].Target;
}

public class StealthTests
{
    private static BlueprintDb Db => CybranTestWorld.StealthDb;
    private static UnitHandle Spawn(Simulation sim, string bp, int army, float x, float z) => CybranTestWorld.Spawn(sim, bp, army, x, z);

    [Fact]
    public void WithoutStealthBlueprintsNothingIsActive()
    {
        var sim = CybranTestWorld.Flat(TestUtil.Db, 128);
        Spawn(sim, "uef_t1_tank", 0, 40, 64);
        sim.Step();
        bool any = TestUtil.Db.All.Any(b => b.Intel.Cloak || b.Intel.RadarStealth || b.Intel.SonarStealth || b.Intel.StealthFieldRadius > 0);
        Assert.Equal(any, StealthSystem.AnyBlueprint(sim.World));
        if (!any) Assert.False(StealthSystem.AnyActive(sim.World));
        Assert.True(StealthSystem.AnyBlueprint(CybranTestWorld.Flat(Db, 128).World));
    }

    [Fact]
    public void RadarStealthHidesFromRadarButNotFromVision()
    {
        var sim = CybranTestWorld.Flat(Db, 256);
        var w = sim.World;
        CybranTestWorld.Power(sim, 1, 3);
        Spawn(sim, "uef_t1_radar", 1, 40, 128);
        var stealthy = Spawn(sim, "uef_t1_mobile_aa", 0, 120, 128);
        var plain = Spawn(sim, "uef_t1_tank", 0, 120, 150);
        CybranTestWorld.Run(sim, 3);
        Assert.True(w.Intel.IsBlip(1, plain.Index), "a normal unit in radar range is a blip");
        Assert.False(w.Intel.IsDetected(1, stealthy.Index), "radar stealth: no blip");
        Assert.True((StealthSystem.StatusOf(w, stealthy.Index, 0) & UnitStatus.RadarStealth) != 0, "owner sees the stealth bit");
        Assert.Equal(UnitStatus.None, StealthSystem.StatusOf(w, stealthy.Index, 1));

        Spawn(sim, "uef_t1_tank", 1, 108, 128); // vision 20
        CybranTestWorld.Run(sim, 2);
        Assert.True(w.Intel.IsVisible(1, stealthy.Index), "radar stealth doesn't hide from vision");
    }

    [Fact]
    public void CloakHidesFromVisionAndRadarButNotFromOmni()
    {
        var sim = CybranTestWorld.Flat(Db, 256);
        var w = sim.World;
        CybranTestWorld.Power(sim, 1, 8);
        Spawn(sim, "uef_t1_radar", 1, 40, 128);
        var watcher = Spawn(sim, "uef_t1_tank", 1, 110, 128);
        var eng = Spawn(sim, "uef_t1_engineer", 0, 120, 128);
        var storage = Spawn(sim, "uef_t1_mass_storage", 0, 116, 138);
        var far = Spawn(sim, "uef_t1_engineer", 0, 170, 160); // out of the watcher's vision
        CybranTestWorld.Run(sim, 20);
        Assert.False(w.Intel.IsVisible(1, eng.Index));
        Assert.False(w.Intel.IsDetected(1, eng.Index), "cloak implies radar stealth: no blip");
        Assert.True(CybranTestWorld.Target(sim, watcher).IsNone, "an undetected unit is never acquired");
        Assert.Equal(w.BpOf(eng.Index).MaxHealth, w.Units.Health[eng.Index]);
        var known = new List<GhostInfo>();
        w.Intel.GetKnownStructures(1, known);
        Assert.DoesNotContain(known, g => g.Slot == storage.Index);
        Assert.True((StealthSystem.StatusOf(w, eng.Index, 0) & UnitStatus.Cloaked) != 0);

        Spawn(sim, "uef_t3_omni_sensor", 1, 60, 128); // omni 150
        CybranTestWorld.Run(sim, 3);
        Assert.True(w.Intel.IsVisible(1, eng.Index), "omni + vision reveal a cloaked unit");
        Assert.True(w.Intel.IsBlip(1, far.Index), "omni without vision: a blip");
        known.Clear();
        w.Intel.GetKnownStructures(1, known);
        Assert.Contains(known, g => g.Slot == storage.Index);
        CybranTestWorld.Run(sim, 20);
        Assert.True(w.Units.Health[eng.Index] < w.BpOf(eng.Index).MaxHealth || !w.Units.IsActive(eng), "revealed: attacked");
    }

    [Fact]
    public void CloakedStructureInSightDropsItsGhost()
    {
        var sim = CybranTestWorld.Flat(Db, 256);
        var w = sim.World;
        CybranTestWorld.Power(sim, 0, 2);
        var storage = Spawn(sim, "uef_t1_mass_storage", 0, 120, 128);
        Spawn(sim, "uef_t1_tank", 1, 110, 128);
        // Paused (cloak off): seen and ghosted.
        sim.Step([new SetPausedCommand(0, [storage], true)]);
        CybranTestWorld.Run(sim, 3);
        var known = new List<GhostInfo>();
        w.Intel.GetKnownStructures(1, known);
        Assert.Contains(known, g => g.Slot == storage.Index);
        // Cloak back on: the structure in sight "looks gone".
        sim.Step([new SetPausedCommand(0, [storage], false)]);
        CybranTestWorld.Run(sim, 3);
        known.Clear();
        w.Intel.GetKnownStructures(1, known);
        Assert.DoesNotContain(known, g => g.Slot == storage.Index);
    }

    [Fact]
    public void StealthFieldHidesAlliedUnitsInRadiusOnly()
    {
        var sim = CybranTestWorld.Flat(Db, 256, 0, 1, 0); // army 2 is army 0's ally
        var w = sim.World;
        CybranTestWorld.Power(sim, 0, 3);
        CybranTestWorld.Power(sim, 1, 3);
        Spawn(sim, "uef_t1_radar", 1, 40, 128);
        var gen = Spawn(sim, "uef_t1_energy_storage", 0, 120, 128);
        var inside = Spawn(sim, "uef_t1_tank", 0, 132, 128);
        var allyInside = Spawn(sim, "uef_t1_tank", 2, 120, 142);
        var outside = Spawn(sim, "uef_t1_tank", 0, 120, 170);
        CybranTestWorld.Run(sim, 5);
        Assert.False(w.Intel.IsDetected(1, gen.Index), "the generator is stealthed too");
        Assert.False(w.Intel.IsDetected(1, inside.Index));
        Assert.False(w.Intel.IsDetected(1, allyInside.Index), "allies in the field are stealthed");
        Assert.True(w.Intel.IsBlip(1, outside.Index), "outside the radius: normal blip");
        Assert.True((StealthSystem.StatusOf(w, gen.Index, 0) & UnitStatus.StealthField) != 0);
        Assert.True((StealthSystem.StatusOf(w, inside.Index, 0) & UnitStatus.RadarStealth) != 0);
        Assert.True((StealthSystem.StatusOf(w, allyInside.Index, 0) & UnitStatus.RadarStealth) != 0, "allied view shows the bit");
        Assert.Equal(UnitStatus.None, StealthSystem.StatusOf(w, inside.Index, 1));
        Assert.Equal(UnitStatus.None, StealthSystem.StatusOf(w, outside.Index, 0));
    }

    [Fact]
    public void PausedOrEnergyStalledFieldGeneratorStops()
    {
        var sim = CybranTestWorld.Flat(Db, 256);
        var w = sim.World;
        CybranTestWorld.Power(sim, 1, 3);
        Spawn(sim, "uef_t1_radar", 1, 40, 128);
        var gen = Spawn(sim, "uef_t1_energy_storage", 0, 120, 128);
        var inside = Spawn(sim, "uef_t1_tank", 0, 130, 128);
        // Army 0 has no power: 20 E/s upkeep can't be paid → stalled → field off.
        CybranTestWorld.Run(sim, 5);
        Assert.True(w.Armies[0].Eco.EnergyEfficiency < StealthSystem.MinPowerEfficiency);
        Assert.True(w.Intel.IsBlip(1, inside.Index), "stalled field: no stealth");

        CybranTestWorld.Power(sim, 0, 3);
        CybranTestWorld.Run(sim, 5);
        Assert.False(w.Intel.IsDetected(1, inside.Index), "powered field: stealth");

        sim.Step([new SetPausedCommand(0, [gen], true)]);
        CybranTestWorld.Run(sim, 2);
        Assert.True(w.Intel.IsBlip(1, inside.Index), "paused field: no stealth");
        Assert.True(w.Armies[0].Eco.EnergyDemand < 1f, "a paused generator pays no upkeep");

        sim.Step([new SetPausedCommand(0, [gen], false)]);
        CybranTestWorld.Run(sim, 2);
        Assert.False(w.Intel.IsDetected(1, inside.Index));
    }

    private static Simulation Scenario()
    {
        var sim = CybranTestWorld.Flat(Db, 256, 0, 1);
        CybranTestWorld.Power(sim, 0, 4);
        CybranTestWorld.Power(sim, 1, 8);
        Spawn(sim, "uef_t1_radar", 1, 40, 128);
        Spawn(sim, "uef_t1_energy_storage", 0, 140, 128);
        Spawn(sim, "uef_t1_engineer", 0, 150, 120);
        Spawn(sim, "uef_t1_mass_storage", 0, 160, 140);
        Spawn(sim, "uef_t1_mobile_aa", 0, 170, 110);
        var defenders = new List<UnitHandle>();
        for (int k = 0; k < 6; k++) defenders.Add(Spawn(sim, "uef_t1_tank", 0, 145 + k * 4, 132));
        var attackers = new List<UnitHandle>();
        for (int k = 0; k < 8; k++) attackers.Add(Spawn(sim, "uef_t1_tank", 1, 60 + k * 3, 120 + k * 2));
        sim.Step([new IssueOrderCommand(1, [.. attackers], Order.AttackMoveTo(new Vector2(200, 128)), false)]);
        return sim;
    }

    [Fact]
    public void SaveLoadKeepsStealthState()
    {
        var a = Scenario();
        CybranTestWorld.Run(a, 60);
        var saved = WorldState.Capture(a);
        var b = WorldState.Restore(saved, a.World.Blueprints, a.World.Terrain);
        Assert.True(StealthSystem.AnyActive(b.World));
        for (int i = 0; i < a.World.Units.Capacity; i++)
        {
            if (!a.World.Units.IsActive(i)) continue;
            Assert.Equal(a.World.Intel.IsDetected(1, i), b.World.Intel.IsDetected(1, i));
            Assert.Equal(StealthSystem.BitsOf(a.World, i), StealthSystem.BitsOf(b.World, i));
        }
        Assert.True(saved.AsSpan().SequenceEqual(WorldState.Capture(b)), "reload is not byte-identical");
        for (int t = 0; t < 60; t++)
        {
            a.Step();
            b.Step();
            Assert.Equal(a.StateHash(), b.StateHash());
        }
        Assert.True(WorldState.Capture(a).AsSpan().SequenceEqual(WorldState.Capture(b)), "state differs after continuing");
    }

    [Fact]
    public void StealthIsDeterministicAcrossThreadCounts()
    {
        ulong RunWith(int threads) => PersistenceTestUtil.WithThreads(threads, () =>
        {
            var sim = Scenario();
            CybranTestWorld.Run(sim, 200);
            return WorldState.FullHash(sim);
        });
        Assert.Equal(RunWith(1), RunWith(4));
    }
}

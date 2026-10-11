using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

public class IntelTests
{
    /// <summary>Flat 128 map; armies get the given teams (default: everyone on their own team).</summary>
    private static Simulation World(params int[] teams)
    {
        if (teams.Length == 0) teams = [0, 1];
        var heights = new float[129 * 129];
        Array.Fill(heights, 10f);
        var terrain = new Terrain(128, 128, heights, 0f, [], [new Vector2(20, 20), new Vector2(108, 108)], 1);
        var armies = teams.Select((t, a) => new Army { Index = a, Team = t, Name = $"A{a}" }).ToArray();
        return new Simulation(new SimWorld(TestUtil.Db, terrain, armies, 1, unitCapacity: 1024, projectileCapacity: 4096));
    }

    private static UnitHandle Spawn(Simulation sim, string bp, int army, float x, float z) =>
        CombatWorld.Spawn(sim, bp, army, x, z);

    [Fact]
    public void UnitOutsideVisionRadiusIsNotVisible()
    {
        var sim = World();
        var intel = sim.World.Intel;
        Spawn(sim, "uef_t1_tank", 0, 30, 64);                  // vision 20
        var far = Spawn(sim, "uef_t1_tank", 1, 60, 64);        // 30 away
        var near = Spawn(sim, "uef_t1_engineer", 1, 45, 70);   // ~16 away
        sim.Step();
        Assert.False(intel.IsVisible(0, far.Index));
        Assert.False(intel.IsDetected(0, far.Index));
        Assert.True(intel.IsVisible(0, near.Index));
        Assert.True(intel.IsDetected(0, near.Index));
        // Own units are always visible to their army.
        Assert.True(intel.IsVisible(1, far.Index));
        Assert.False(intel.IsPositionVisible(0, new Vector2(100, 100)));
        Assert.True(intel.IsPositionVisible(0, new Vector2(30, 64)));
    }

    [Fact]
    public void EverythingWithinVisionRadiusIsVisible()
    {
        var sim = World();
        var w = sim.World;
        var viewer = Spawn(sim, "uef_t1_tank", 0, 63.3f, 61.7f);
        float r = TestUtil.Db["uef_t1_tank"].Intel.VisionRadius;
        var rng = new DetRandom(99);
        var probes = new List<UnitHandle>();
        for (int k = 0; k < 60; k++)
        {
            float a = rng.NextFloat() * MathF.Tau, d = r * MathF.Sqrt(rng.NextFloat()) * 0.999f;
            probes.Add(Spawn(sim, "uef_t1_engineer", 1, 63.3f + MathF.Cos(a) * d, 61.7f + MathF.Sin(a) * d));
        }
        sim.Step();
        foreach (var p in probes)
            Assert.True(w.Intel.IsVisible(0, p.Index), $"probe at {w.Units.Pos[p.Index]} is within {r} of the viewer");
        Assert.True(w.Units.IsActive(viewer));
    }

    [Fact]
    public void AircraftGiveVision()
    {
        var sim = World();
        var plane = Spawn(sim, "uef_t1_air_scout", 0, 30, 30);
        var target = Spawn(sim, "uef_t1_tank", 1, 30 + 35, 30); // air scout vision 42
        sim.Step();
        Assert.True(sim.World.Units.Elevation[plane.Index] > 12f);
        Assert.True(sim.World.Intel.IsVisible(0, target.Index));
    }

    [Fact]
    public void RadarDetectsAtLongRangeButDoesNotReveal()
    {
        var sim = World();
        var w = sim.World;
        Spawn(sim, "uef_t1_power_generator", 0, 14, 20); // pays the radar upkeep
        var radar = Spawn(sim, "uef_t1_radar", 0, 20, 20);
        var tank = Spawn(sim, "uef_t1_tank", 1, 90, 20);   // 70 away: radar 115, vision 20
        TestUtil.Run(sim, 3);
        Assert.True(w.Intel.IsDetected(0, tank.Index), "radar should detect the tank");
        Assert.False(w.Intel.IsVisible(0, tank.Index), "radar must not reveal the unit");
        Assert.True(w.Intel.IsBlip(0, tank.Index));
        Assert.True(w.Intel.IsPositionOnRadar(0, new Vector2(90, 20)));

        // The view army's snapshot: no unit, a blip at its position, no type information.
        var s = new SimSnapshot();
        sim.WriteSnapshot(s, 0);
        Assert.False(s.TryGetUnit(tank.Index, tank.Generation, out _));
        Assert.Equal(1, s.BlipCount);
        Assert.Equal(1, s.Blips[0].Army);
        Assert.True(Vector2.Distance(new Vector2(s.Blips[0].Pos.X, s.Blips[0].Pos.Z), new Vector2(90, 20)) < 0.01f);
        Assert.True(s.TryGetUnit(radar.Index, radar.Generation, out _));
        // Without fog the snapshot has everything.
        sim.WriteSnapshot(s, -1);
        Assert.True(s.TryGetUnit(tank.Index, tank.Generation, out _));
        Assert.Equal(0, s.BlipCount);

        // Paused radar: no detection.
        w.Units.Flags[radar.Index] |= UnitFlags.Paused;
        sim.Step();
        Assert.False(w.Intel.IsDetected(0, tank.Index));
    }

    [Fact]
    public void RadarNeedsPower()
    {
        var sim = World();
        Spawn(sim, "uef_t1_radar", 0, 20, 20); // no energy income or storage: the upkeep can't be paid
        var tank = Spawn(sim, "uef_t1_tank", 1, 90, 20);
        TestUtil.Run(sim, 3);
        Assert.False(sim.World.Intel.IsDetected(0, tank.Index));
    }

    [Fact]
    public void AlliesShareIntel()
    {
        var sim = World(0, 0, 1); // armies 0 and 1 allied, army 2 enemy
        var w = sim.World;
        Spawn(sim, "uef_t1_tank", 0, 10, 10);
        Spawn(sim, "uef_t1_tank", 1, 100, 100);
        var enemy = Spawn(sim, "uef_t1_engineer", 2, 110, 100);
        var allied = Spawn(sim, "uef_t1_tank", 1, 100, 100);
        sim.Step();
        Assert.True(w.Intel.IsVisible(0, enemy.Index), "army 0 sees through its ally's vision");
        Assert.True(w.Intel.IsVisible(1, enemy.Index));
        Assert.True(w.Intel.IsVisible(0, allied.Index));
        Assert.True(w.Intel.IsPositionExplored(0, new Vector2(100, 100)));
        Assert.False(w.Intel.IsPositionExplored(2, new Vector2(10, 10)));
        var s = new SimSnapshot();
        sim.WriteSnapshot(s, 0);
        Assert.True(s.TryGetUnit(enemy.Index, enemy.Generation, out _));
    }

    [Fact]
    public void ExploredAreaStaysExploredButNotVisible()
    {
        var sim = World();
        var w = sim.World;
        var scout = Spawn(sim, "uef_t1_tank", 0, 30, 30);
        sim.Step();
        var spot = new Vector2(30, 40);
        Assert.True(w.Intel.IsPositionVisible(0, spot));
        w.Units.Pos[scout.Index] = new Vector2(100, 100);
        sim.Step();
        Assert.False(w.Intel.IsPositionVisible(0, spot));
        Assert.True(w.Intel.IsPositionExplored(0, spot));
        Assert.False(w.Intel.IsPositionExplored(0, new Vector2(64, 10)));
        var fog = w.Intel.FogOf(0);
        Assert.Equal(IntelService.FogExplored, fog[w.Intel.CellIndex(spot)]);
        Assert.Equal(IntelService.FogVisible, fog[w.Intel.CellIndex(new Vector2(100, 100))]);
        Assert.Equal(IntelService.FogUnexplored, fog[w.Intel.CellIndex(new Vector2(64, 10))]);
    }

    [Fact]
    public void ArtilleryDoesNotShootWhatItCannotSee()
    {
        var sim = World();
        var w = sim.World;
        var arty = Spawn(sim, "uef_t1_artillery", 0, 36, 64); // range 30, vision 18
        var tank = Spawn(sim, "uef_t1_tank", 1, 61, 64);      // 25 away: in range, out of vision
        for (int t = 0; t < 40; t++)
        {
            sim.Step();
            Assert.True(CombatWorld.Weapon(sim, arty).Target.IsNone);
        }
        Assert.Equal(0, w.Projectiles.AliveCount);
        Assert.Equal(TestUtil.Db["uef_t1_tank"].MaxHealth, w.Units.Health[tank.Index]);

        // A spotter reveals it: the artillery opens fire.
        Spawn(sim, "uef_t1_scout", 0, 40, 72);
        for (int t = 0; t < 10; t++) sim.Step();
        Assert.Equal(tank, CombatWorld.Weapon(sim, arty).Target);
    }

    [Fact]
    public void TankDoesNotShootAnEnemyItCannotSee()
    {
        var sim = World();
        var w = sim.World;
        var tank = Spawn(sim, "uef_t1_tank", 0, 20, 64);
        var enemy = Spawn(sim, "uef_t1_engineer", 1, 70, 64); // 50 away, never seen
        // Explicit attack order on a unit outside the army's intel: the tank drives there but can't lock on
        // until the target enters its vision.
        sim.Step([new IssueOrderCommand(0, [tank], Order.AttackUnit(enemy), false)]);
        int firstLock = -1;
        for (int t = 0; t < 200 && w.Units.IsActive(enemy); t++)
        {
            bool locked = CombatWorld.Weapon(sim, tank).Target == enemy;
            bool visible = w.Intel.IsVisible(0, enemy.Index);
            if (locked && firstLock < 0) firstLock = w.Tick;
            if (locked) Assert.True(visible || w.Intel.IsDetected(0, enemy.Index));
            float d = Vector2.Distance(w.Units.Pos[tank.Index], w.Units.Pos[enemy.Index]);
            if (d > 25f) Assert.False(locked, $"locked on an unseen target at distance {d}");
            sim.Step();
        }
        Assert.True(firstLock > 0, "the tank should lock on once the target is in vision");
        Assert.False(w.Units.IsActive(enemy), "and kill it");
    }

    [Fact]
    public void GhostsRememberStructuresUntilRescouted()
    {
        var sim = World();
        var w = sim.World;
        var scout = Spawn(sim, "uef_t1_tank", 0, 30, 64);
        var pd = Spawn(sim, "uef_t1_mass_storage", 1, 40, 64); // unarmed enemy structure
        sim.Step();
        var known = new List<GhostInfo>();
        w.Intel.GetKnownStructures(0, known);
        Assert.Single(known);
        Assert.Equal(pd.Index, known[0].Slot);

        // Leave: the structure is not visible any more but remembered (shown as a ghost in the snapshot).
        w.Units.Pos[scout.Index] = new Vector2(100, 110);
        sim.Step();
        Assert.False(w.Intel.IsVisible(0, pd.Index));
        var s = new SimSnapshot();
        sim.WriteSnapshot(s, 0);
        Assert.Equal(1, s.GhostCount);
        Assert.Equal(pd.Index >= 0 ? w.Units.Bp[pd.Index] : -1, s.Ghosts[0].Bp);
        Assert.False(s.TryGetUnit(pd.Index, pd.Generation, out _));

        // Destroyed while unobserved: still remembered.
        w.KillUnit(pd.Index, UnitHandle.None);
        sim.Step();
        known.Clear();
        w.Intel.GetKnownStructures(0, known);
        Assert.Single(known);

        // Re-scouted: forgotten.
        w.Units.Pos[scout.Index] = new Vector2(32, 64);
        sim.Step();
        known.Clear();
        w.Intel.GetKnownStructures(0, known);
        Assert.Empty(known);
        sim.WriteSnapshot(s, 0);
        Assert.Equal(0, s.GhostCount);
    }

    [Fact]
    public void SnapshotHidesEnemyProjectilesAndEffectsInFog()
    {
        var sim = World(0, 1, 2);
        var w = sim.World;
        Spawn(sim, "uef_t1_engineer", 0, 10, 10); // army 0 only sees its corner
        Spawn(sim, "uef_t1_tank", 1, 90, 64);     // armies 1 and 2 fight far away, in army 0's fog
        Spawn(sim, "uef_t1_tank", 2, 104, 64);
        var fogged = new SimSnapshot();
        var full = new SimSnapshot();
        int shots = 0, effects = 0;
        for (int t = 0; t < 60; t++)
        {
            sim.Step();
            // The writer drains the effect list: count what the sim produced before filtering.
            int pending = w.Events.Effects.Count;
            sim.WriteSnapshot(fogged, 0);
            effects += pending;
            Assert.Equal(0, fogged.ProjectileCount);
            Assert.Empty(fogged.Effects);
            for (int k = 0; k < fogged.UnitCount; k++) Assert.Equal(0, fogged.Units[k].Army);
            sim.WriteSnapshot(full, -1);
            shots += full.ProjectileCount;
        }
        Assert.True(shots > 0 && effects > 0, "the fight should produce projectiles and effects");

        // An observer (army 1) sees its own fight, but not the orders of the enemy units.
        sim.Step();
        sim.WriteSnapshot(fogged, 1);
        Assert.Contains(fogged.Units.Take(fogged.UnitCount), v => v.Army == 2);
        for (int k = 0; k < fogged.UnitCount; k++)
            if (fogged.Units[k].Army != 1) Assert.Equal(0, fogged.Units[k].OrderCount);
        Assert.Equal(w.Intel.Cols * w.Intel.Rows, fogged.Fog.Length);
        Assert.Equal(1, fogged.ViewArmy);
    }

    [Fact]
    public void IntelIsDeterministicAcrossThreadCounts()
    {
        (ulong hash, ulong fog) RunWith(int threads)
        {
            int old = Par.MaxThreads;
            Par.MaxThreads = threads;
            try
            {
                var sim = TestUtil.NewSkirmish(seed: 11);
                var w = sim.World;
                string[] kinds = ["uef_t1_tank", "uef_t1_artillery", "uef_t1_scout", "uef_t1_air_scout", "uef_t1_interceptor"];
                var a = new List<UnitHandle>();
                var b = new List<UnitHandle>();
                for (int k = 0; k < 200; k++)
                {
                    int bp = TestUtil.Db.IndexOf(kinds[k % kinds.Length]);
                    a.Add(w.SpawnUnit(bp, 0, new Vector2(50 + k % 15 * 1.6f, 50 + k / 15 * 1.6f), 0));
                    b.Add(w.SpawnUnit(bp, 1, new Vector2(200 - k % 15 * 1.6f, 200 - k / 15 * 1.6f), 0));
                }
                sim.Step([
                    new IssueOrderCommand(0, a.ToArray(), Order.AttackMoveTo(new Vector2(200, 200)), false),
                    new IssueOrderCommand(1, b.ToArray(), Order.AttackMoveTo(new Vector2(50, 50)), false),
                ]);
                for (int t = 0; t < 300; t++) sim.Step();
                ulong f = 1469598103934665603UL;
                for (int army = 0; army < 2; army++)
                    foreach (byte v in w.Intel.FogOf(army)) { f ^= v; f *= 1099511628211UL; }
                return (sim.StateHash(), f);
            }
            finally { Par.MaxThreads = old; }
        }

        Assert.Equal(RunWith(1), RunWith(8));
    }
}

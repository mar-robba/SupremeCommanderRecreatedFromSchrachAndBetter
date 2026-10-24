using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Helpers for combat tests: a small flat all-land map without commanders.</summary>
public static class CombatWorld
{
    public static Simulation Flat(int size = 128, int armies = 2, ulong seed = 1)
    {
        var heights = new float[(size + 1) * (size + 1)];
        Array.Fill(heights, 10f);
        var terrain = new Terrain(size, size, heights, 0f, [], [new Vector2(20, 20), new Vector2(size - 20, size - 20)], 1);
        var arr = new Army[armies];
        for (int a = 0; a < armies; a++) arr[a] = new Army { Index = a, Team = a, Name = $"A{a}" };
        return new Simulation(new SimWorld(TestUtil.Db, terrain, arr, seed, unitCapacity: 2048, projectileCapacity: 8192));
    }

    public static UnitHandle Spawn(Simulation sim, string bp, int army, float x, float z, float heading = 0) =>
        sim.World.SpawnUnit(TestUtil.Db.IndexOf(bp), army, new Vector2(x, z), heading, complete: true);

    public static WeaponState Weapon(Simulation sim, UnitHandle h, int wi = 0) =>
        sim.World.Units.Weapons[h.Index * UnitStore.MaxWeapons + wi];

    public static bool Alive(Simulation sim, UnitHandle h) => sim.World.Units.IsActive(h);

    public static float Health(Simulation sim, UnitHandle h) => sim.World.Units.IsActive(h) ? sim.World.Units.Health[h.Index] : 0f;
}

public class CombatTests
{
    [Fact]
    public void TankVsTankFightEndsWithOneDead()
    {
        var sim = CombatWorld.Flat();
        // B starts with its turret facing away, so A gets the first shots in.
        var a = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 50, 64, heading: 0);
        var b = CombatWorld.Spawn(sim, "uef_t1_tank", 1, 64, 64, heading: 0);
        int ticks = 0;
        while (CombatWorld.Alive(sim, a) && CombatWorld.Alive(sim, b) && ticks < 600)
        {
            sim.Step();
            ticks++;
        }
        Assert.True(CombatWorld.Alive(sim, a) ^ CombatWorld.Alive(sim, b), $"after {ticks} ticks: A alive={CombatWorld.Alive(sim, a)}, B alive={CombatWorld.Alive(sim, b)}");
        Assert.True(CombatWorld.Alive(sim, a), "A had the first-shot advantage and should win");
        // 300 HP / 18 dmg per second → at least 17 s, plus some misses.
        Assert.InRange(ticks, 150, 400);
        Assert.Equal(1, sim.World.Units.Kills[a.Index]);
    }

    [Fact]
    public void ProjectileIsSpawnedAndTravels()
    {
        var sim = CombatWorld.Flat();
        var a = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 50, 64);
        CombatWorld.Spawn(sim, "uef_t1_tank", 1, 65, 64, heading: MathF.PI);
        var p = sim.World.Projectiles;
        int guard = 0;
        while (p.AliveCount == 0 && guard++ < 30) sim.Step();
        Assert.True(p.AliveCount > 0, "no projectile spawned");
        Assert.True((sim.World.Units.Flags[a.Index] & UnitFlags.Firing) != 0);
        Assert.Contains(sim.World.Events.Effects, e => e.Kind == EffectKind.MuzzleFlash);

        // Follow army 0's shell for one tick.
        int slot = -1;
        for (int k = 0; k < p.AliveCount; k++)
            if (p.Army[p.AliveList[k]] == 0) slot = p.AliveList[k];
        Assert.True(slot >= 0);
        int id = p.Id[slot];
        var before = p.Pos[slot];
        sim.Step();
        Assert.True(p.Alive[slot] && p.Id[slot] == id, "shell vanished after one tick");
        var moved = p.Pos[slot] - before;
        Assert.InRange(moved.Length(), 3.9f, 4.1f); // MuzzleVelocity 40 * 0.1 s
        Assert.True(moved.X > 2.8f, $"shell should fly toward +x, moved {moved}");

        // Eventually it hits: the enemy loses health.
        for (int t = 0; t < 10; t++) sim.Step();
        Assert.False(p.Alive[slot] && p.Id[slot] == id, "shell should have hit within 1 s");
    }

    [Fact]
    public void ArtillerySplashDamagesClusteredGroup()
    {
        var sim = CombatWorld.Flat();
        var arty = CombatWorld.Spawn(sim, "uef_t1_artillery", 0, 36, 64);
        // Fog of war: the artillery (vision 18) needs a spotter to see targets 24 units away (scout: vision 40).
        CombatWorld.Spawn(sim, "uef_t1_scout", 0, 36, 70);
        var tanks = new List<UnitHandle>();
        for (int r = 0; r < 3; r++)
        for (int c = 0; c < 3; c++)
            tanks.Add(CombatWorld.Spawn(sim, "uef_t1_tank", 1, 60 + c * 1.3f, 62.7f + r * 1.3f));
        float max = TestUtil.Db["uef_t1_tank"].MaxHealth;
        bool sawBallistic = false;
        float maxHeight = 0;
        for (int t = 0; t < 220; t++)
        {
            sim.Step();
            var p = sim.World.Projectiles;
            for (int k = 0; k < p.AliveCount; k++)
            {
                int i = p.AliveList[k];
                if ((p.Flags[i] & ProjectileFlags.Ballistic) != 0) sawBallistic = true;
                maxHeight = MathF.Max(maxHeight, p.Pos[i].Y);
            }
        }
        Assert.True(sawBallistic);
        Assert.True(maxHeight > 10f + 8f, $"artillery should lob shells on a high arc (max y {maxHeight})");
        int damaged = tanks.Count(h => CombatWorld.Health(sim, h) < max);
        Assert.True(damaged >= 3, $"splash should hit several clustered tanks, damaged={damaged}");
        Assert.True(CombatWorld.Alive(sim, arty));
    }

    [Fact]
    public void ArtilleryRespectsMinRange()
    {
        var sim = CombatWorld.Flat();
        var arty = CombatWorld.Spawn(sim, "uef_t1_artillery", 0, 60, 64);
        var close = CombatWorld.Spawn(sim, "uef_t1_engineer", 1, 63, 64);
        for (int t = 0; t < 30; t++)
        {
            sim.Step();
            Assert.NotEqual(close, CombatWorld.Weapon(sim, arty).Target);
        }
        Assert.Equal(0, sim.World.Projectiles.AliveCount);
    }

    [Fact]
    public void AntiAirOnlyTargetsAircraftAndShootsThem()
    {
        var sim = CombatWorld.Flat();
        var aa = CombatWorld.Spawn(sim, "uef_t1_mobile_aa", 0, 64, 64);
        var turret = CombatWorld.Spawn(sim, "uef_t1_aa_turret", 0, 64, 52);
        // Enemy tank inside AA range but outside its own range (it can't shoot back), and an enemy bomber.
        var tank = CombatWorld.Spawn(sim, "uef_t1_tank", 1, 85, 64);
        var bomber = CombatWorld.Spawn(sim, "uef_t1_bomber", 1, 64, 80);
        float bomberMax = TestUtil.Db["uef_t1_bomber"].MaxHealth;
        bool aaTargetedBomber = false, turretTargetedBomber = false, sawFlak = false, sawMissile = false;
        for (int t = 0; t < 150; t++)
        {
            sim.Step();
            var wa = CombatWorld.Weapon(sim, aa);
            var wt = CombatWorld.Weapon(sim, turret);
            Assert.NotEqual(tank, wa.Target);
            Assert.NotEqual(tank, wt.Target);
            aaTargetedBomber |= wa.Target == bomber;
            turretTargetedBomber |= wt.Target == bomber;
            var p = sim.World.Projectiles;
            for (int k = 0; k < p.AliveCount; k++)
            {
                int i = p.AliveList[k];
                sawFlak |= (p.Flags[i] & ProjectileFlags.ProximityFuse) != 0;
                sawMissile |= (p.Flags[i] & ProjectileFlags.Homing) != 0;
            }
        }
        Assert.True(aaTargetedBomber, "mobile AA never targeted the bomber");
        Assert.True(turretTargetedBomber, "AA turret never targeted the bomber");
        Assert.True(sawFlak && sawMissile, $"flak={sawFlak} missile={sawMissile}");
        Assert.True(CombatWorld.Health(sim, bomber) < bomberMax * 0.5f, $"bomber health {CombatWorld.Health(sim, bomber)}");
        Assert.Equal(TestUtil.Db["uef_t1_tank"].MaxHealth, CombatWorld.Health(sim, tank));
    }

    [Fact]
    public void InterceptorShootsAircraftButIgnoresGround()
    {
        var sim = CombatWorld.Flat();
        var fighter = CombatWorld.Spawn(sim, "uef_t1_interceptor", 0, 50, 64);
        var tank = CombatWorld.Spawn(sim, "uef_t1_tank", 1, 55, 64);
        var scout = CombatWorld.Spawn(sim, "uef_t1_air_scout", 1, 62, 64);
        for (int t = 0; t < 120 && CombatWorld.Alive(sim, scout); t++)
        {
            sim.Step();
            Assert.NotEqual(tank, CombatWorld.Weapon(sim, fighter).Target);
        }
        Assert.False(CombatWorld.Alive(sim, scout), "interceptor should shoot down the air scout (60 HP)");
        Assert.Equal(TestUtil.Db["uef_t1_tank"].MaxHealth, CombatWorld.Health(sim, tank));
    }

    [Fact]
    public void BomberDestroysStructureOnAttackOrder()
    {
        var sim = CombatWorld.Flat();
        var bomber = CombatWorld.Spawn(sim, "uef_t1_bomber", 0, 30, 64);
        var pgen = CombatWorld.Spawn(sim, "uef_t1_power_generator", 1, 80, 64);
        float max = TestUtil.Db["uef_t1_power_generator"].MaxHealth;
        sim.Step([new IssueOrderCommand(0, [bomber], Order.AttackUnit(pgen), false)]);
        bool sawBomb = false;
        for (int t = 0; t < 400 && CombatWorld.Alive(sim, pgen); t++)
        {
            sim.Step();
            var p = sim.World.Projectiles;
            for (int k = 0; k < p.AliveCount; k++) sawBomb |= (p.Flags[p.AliveList[k]] & ProjectileFlags.Bomb) != 0;
        }
        Assert.True(sawBomb, "no bomb dropped");
        Assert.True(CombatWorld.Health(sim, pgen) < max, "bombs missed the power generator");
    }

    [Fact]
    public void TargetingPrefersUnitsThatShootBack()
    {
        var sim = CombatWorld.Flat();
        var tank = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 50, 64);
        var engineer = CombatWorld.Spawn(sim, "uef_t1_engineer", 1, 56, 64); // closer, unarmed
        var mex = CombatWorld.Spawn(sim, "uef_t1_power_generator", 1, 54, 70); // structure
        var enemyTank = CombatWorld.Spawn(sim, "uef_t1_tank", 1, 64, 64, MathF.PI); // further, armed
        for (int t = 0; t < 5; t++) sim.Step();
        Assert.Equal(enemyTank, CombatWorld.Weapon(sim, tank).Target);
        _ = engineer; _ = mex;
    }

    [Fact]
    public void AttackGroundFiresAtPosition()
    {
        var sim = CombatWorld.Flat();
        var arty = CombatWorld.Spawn(sim, "uef_t1_artillery", 0, 40, 64);
        var spot = new Vector2(60, 64);
        sim.Step([new IssueOrderCommand(0, [arty], new Order { Type = OrderType.Attack, Pos = spot, Target = UnitHandle.None, BlueprintIndex = -1, Spawned = UnitHandle.None }, false)]);
        bool fired = false;
        for (int t = 0; t < 60 && !fired; t++)
        {
            sim.Step();
            fired = sim.World.Projectiles.AliveCount > 0;
        }
        Assert.True(fired, "artillery should shell the ground position");
    }

    [Fact]
    public void BallisticSolverHitsItsAimPoint()
    {
        var from = new Vector3(0, 1, 0);
        var to = new Vector3(25, 3, 10);
        foreach (bool high in new[] { false, true })
        {
            Assert.True(Ballistics.SolveArc(from, to, 20, Ballistics.Gravity, high, out var v, out float t));
            var p = from + v * t + new Vector3(0, -0.5f * Ballistics.Gravity * t * t, 0);
            Assert.True(Vector3.Distance(p, to) < 0.01f, $"high={high} landed at {p}");
        }
        Assert.False(Ballistics.SolveArc(from, new Vector3(100, 0, 0), 20, Ballistics.Gravity, false, out _, out _));
    }

    [Fact]
    public void CombatIsDeterministicAcrossThreadCounts()
    {
        ulong RunWith(int threads)
        {
            int old = Par.MaxThreads;
            Par.MaxThreads = threads;
            try
            {
                var sim = CombatWorld.Flat(seed: 9);
                string[] kinds = ["uef_t1_tank", "uef_t1_artillery", "uef_t1_mobile_aa", "uef_t1_interceptor", "uef_t1_bomber", "uef_t1_scout"];
                var a = new List<UnitHandle>();
                var b = new List<UnitHandle>();
                for (int k = 0; k < 150; k++)
                {
                    var bp = kinds[k % kinds.Length];
                    a.Add(CombatWorld.Spawn(sim, bp, 0, 30 + k % 12 * 1.6f, 40 + k / 12 * 1.6f));
                    b.Add(CombatWorld.Spawn(sim, bp, 1, 98 - k % 12 * 1.6f, 88 - k / 12 * 1.6f, MathF.PI));
                }
                CombatWorld.Spawn(sim, "uef_t1_point_defense", 1, 70, 70);
                CombatWorld.Spawn(sim, "uef_t1_aa_turret", 0, 50, 50);
                sim.Step([
                    new IssueOrderCommand(0, a.ToArray(), Order.AttackMoveTo(new Vector2(98, 88)), false),
                    new IssueOrderCommand(1, b.ToArray(), Order.AttackMoveTo(new Vector2(30, 40)), false),
                ]);
                for (int t = 0; t < 250; t++) sim.Step();
                Assert.True(sim.World.Units.AliveCount < 300, "armies should have taken losses");
                return sim.StateHash();
            }
            finally { Par.MaxThreads = old; }
        }

        Assert.Equal(RunWith(1), RunWith(8));
    }
}

using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Strategic;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Shields, tactical/strategic missiles, missile defense and artillery structures.</summary>
public class StrategicTests
{
    private static int Pgens = 0;

    /// <summary>Spawns T1 power generators (20 E/s each) in a far corner and a full energy storage for the army.</summary>
    private static void Power(Simulation sim, int army, float energyPerSecond)
    {
        int n = (int)MathF.Ceiling(energyPerSecond / 20f);
        float z = army == 0 ? 6 : sim.World.Terrain.Height - 6;
        for (int k = 0; k < n; k++) CombatWorld.Spawn(sim, "uef_t1_power_generator", army, 6 + (Pgens++ % 40) * 2.5f, z);
        CombatWorld.Spawn(sim, "uef_t1_energy_storage", army, sim.World.Terrain.Width - 8, z);
        sim.World.Armies[army].Eco.Energy = 5000;
    }

    private static int Count(Simulation sim, EffectKind kind) => sim.World.Events.Effects.Count(e => e.Kind == kind);

    private static ShieldState ShieldOf(Simulation sim, UnitHandle h) => StrategicState.Of(sim.World).Shield[h.Index];

    // ------------------------------------------------------------------ shields

    [Fact]
    public void ShieldAbsorbsShotsFromOutside()
    {
        var sim = CombatWorld.Flat();
        Power(sim, 0, 200);
        var gen = CombatWorld.Spawn(sim, "uef_t2_shield_generator", 0, 64, 64);
        var pgen = CombatWorld.Spawn(sim, "uef_t1_power_generator", 0, 67, 64);
        var tank = CombatWorld.Spawn(sim, "uef_t1_tank", 1, 92, 64, heading: MathF.PI);
        float maxShield = TestUtil.Db["uef_t2_shield_generator"].Shield!.MaxHealth;
        int hits = 0;
        sim.Step([new IssueOrderCommand(1, [tank], Order.AttackUnit(pgen), false)]);
        for (int t = 0; t < 300; t++)
        {
            sim.Step();
            hits += Count(sim, EffectKind.ShieldHit);
            sim.World.Events.Effects.Clear();
        }
        Assert.True(CombatWorld.Alive(sim, tank));
        Assert.True(hits > 10, $"shield hits {hits}");
        Assert.Equal(TestUtil.Db["uef_t1_power_generator"].MaxHealth, CombatWorld.Health(sim, pgen));
        var sh = ShieldOf(sim, gen);
        Assert.True(sh.Up);
        Assert.True(sh.Hp < maxShield, $"shield hp {sh.Hp}");
    }

    [Fact]
    public void ShotsFiredFromInsideADomePass()
    {
        var sim = CombatWorld.Flat();
        Power(sim, 0, 200);
        CombatWorld.Spawn(sim, "uef_t2_shield_generator", 0, 64, 64);
        // An enemy tank that got inside army 0's dome hits a structure inside it...
        var pgenIn = CombatWorld.Spawn(sim, "uef_t1_power_generator", 0, 58, 70);
        var tankIn = CombatWorld.Spawn(sim, "uef_t1_tank", 1, 58, 58, heading: MathF.PI / 2);
        // ...and an enemy tank inside the dome shooting out hits a target outside.
        var pgenOut = CombatWorld.Spawn(sim, "uef_t1_power_generator", 0, 82, 64);
        var tankOut = CombatWorld.Spawn(sim, "uef_t1_tank", 1, 70, 64, heading: 0);
        sim.Step([
            new IssueOrderCommand(1, [tankIn], Order.AttackUnit(pgenIn), false),
            new IssueOrderCommand(1, [tankOut], Order.AttackUnit(pgenOut), false),
        ]);
        for (int t = 0; t < 100; t++) sim.Step();
        float max = TestUtil.Db["uef_t1_power_generator"].MaxHealth;
        Assert.True(CombatWorld.Health(sim, pgenIn) < max, "shot from inside the dome at a target inside must pass");
        Assert.True(CombatWorld.Health(sim, pgenOut) < max, "shot from inside the dome at a target outside must pass");
    }

    [Fact]
    public void ShieldCollapsesAndRecharges()
    {
        var sim = CombatWorld.Flat();
        Power(sim, 0, 200);
        var gen = CombatWorld.Spawn(sim, "uef_t2_shield_generator", 0, 64, 64);
        var sb = TestUtil.Db["uef_t2_shield_generator"].Shield!;
        sim.Step();
        Assert.True(ShieldSystem.IsUp(sim.World, gen.Index));
        Assert.True(ShieldSystem.Damage(sim.World, gen.Index, sb.MaxHealth + 10, new Vector3(64, 20, 64), 0, 0));
        Assert.False(ShieldSystem.IsUp(sim.World, gen.Index));
        Assert.Contains(sim.World.Events.Effects, e => e.Kind == EffectKind.ShieldDown);
        // A down shield absorbs nothing.
        Assert.False(ShieldSystem.Damage(sim.World, gen.Index, 10, new Vector3(64, 20, 64), 0, 0));
        int ticks = (int)(sb.RechargeTime * SimWorld.TicksPerSecond);
        for (int t = 0; t < ticks - 5; t++) sim.Step();
        Assert.False(ShieldSystem.IsUp(sim.World, gen.Index), "recharge takes RechargeTime");
        for (int t = 0; t < 10; t++) sim.Step();
        Assert.True(ShieldSystem.IsUp(sim.World, gen.Index));
        Assert.Equal(sb.MaxHealth, ShieldOf(sim, gen).Hp);

        // Regeneration after damage (after RegenDelay).
        ShieldSystem.Damage(sim.World, gen.Index, 1000, new Vector3(64, 20, 64), 0, 0);
        float hp = ShieldOf(sim, gen).Hp;
        for (int t = 0; t < 50; t++) sim.Step();
        Assert.True(ShieldOf(sim, gen).Hp > hp + sb.RegenRate * 2.5f, $"regen: {hp} -> {ShieldOf(sim, gen).Hp}");
    }

    [Fact]
    public void ShieldDropsOnEnergyStall()
    {
        var sim = CombatWorld.Flat();
        // Army 1: no power at all → the shield collapses right away.
        var unpowered = CombatWorld.Spawn(sim, "uef_t2_shield_generator", 1, 100, 100);
        Power(sim, 0, 160);
        var gen = CombatWorld.Spawn(sim, "uef_t2_shield_generator", 0, 64, 64);
        for (int t = 0; t < 20; t++) sim.Step();
        Assert.False(ShieldSystem.IsUp(sim.World, unpowered.Index));
        Assert.True(ShieldSystem.IsUp(sim.World, gen.Index));
        // Kill army 0's power: the storage drains (150 E/s upkeep), then the shield goes down.
        for (int i = 0; i < sim.World.Units.Capacity; i++)
            if (sim.World.Units.IsActive(i) && sim.World.Units.Army[i] == 0 && sim.World.BpOf(i).Economy.ProductionEnergy > 0)
                sim.World.KillUnit(i, UnitHandle.None);
        int downAt = -1;
        for (int t = 0; t < 600 && downAt < 0; t++)
        {
            sim.Step();
            if (!ShieldSystem.IsUp(sim.World, gen.Index)) downAt = t;
        }
        Assert.True(downAt > 0, "shield should drop once the energy storage is empty");
        Assert.True(sim.World.Armies[0].Eco.Energy < 1f);
    }

    [Fact]
    public void AreaDamageFromOutsideDoesNotLeakIn()
    {
        var sim = CombatWorld.Flat();
        Power(sim, 0, 200);
        CombatWorld.Spawn(sim, "uef_t2_shield_generator", 0, 64, 64);
        var inside = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 76, 64);
        var outside = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 79, 64);
        sim.Step();
        // A blast just outside the dome (radius 13) whose area reaches both tanks.
        sim.World.Events.AreaDamage.Add(new AreaDamageEvent { Center = new Vector2(78, 64), Radius = 4, Amount = 100, SourceArmy = 1, Source = UnitHandle.None });
        Systems.DamageSystem.Run(sim.World);
        float max = TestUtil.Db["uef_t1_tank"].MaxHealth;
        Assert.Equal(max, CombatWorld.Health(sim, inside));
        Assert.True(CombatWorld.Health(sim, outside) < max);
    }

    [Fact]
    public void MobileShieldFollowsItsUnit()
    {
        var sim = CombatWorld.Flat();
        Power(sim, 0, 60);
        var ms = CombatWorld.Spawn(sim, "uef_t2_mobile_shield", 0, 40, 64);
        sim.Step([new IssueOrderCommand(0, [ms], Order.MoveTo(new Vector2(80, 64)), false)]);
        for (int t = 0; t < 250; t++) sim.Step();
        Assert.True(ShieldSystem.IsUp(sim.World, ms.Index));
        var s = StrategicState.Of(sim.World);
        Assert.Equal(1, s.Index.Count);
        Assert.True(Vector2.Distance(new Vector2(s.Index.Entries[0].Center.X, s.Index.Entries[0].Center.Z), new Vector2(80, 64)) < 3f);
    }

    // ------------------------------------------------------------------ tactical missiles

    [Fact]
    public void TmlStockpileBuildsWithEconomy()
    {
        var sim = CombatWorld.Flat();
        var bp = TestUtil.Db["uef_t2_tactical_missile_launcher"];
        var sb = bp.Silo!;
        var tml = CombatWorld.Spawn(sim, "uef_t2_tactical_missile_launcher", 0, 40, 40);
        // Army 1 has an identical launcher and no resources at all: no progress.
        var poor = CombatWorld.Spawn(sim, "uef_t2_tactical_missile_launcher", 1, 90, 90);
        CombatWorld.Spawn(sim, "uef_t1_mass_storage", 0, 20, 100);
        Power(sim, 0, 80);
        sim.World.Armies[0].Eco.Mass = 500;
        int ticks = (int)(sb.MissileBuildTime * SimWorld.TicksPerSecond) + 3;
        for (int t = 0; t < ticks; t++) sim.Step();
        var s = StrategicState.Of(sim.World);
        Assert.Equal(1, s.Silo[tml.Index].Stock);
        Assert.Equal(0, s.Silo[poor.Index].Stock);
        Assert.True(s.Silo[poor.Index].Progress < 0.01f);
        var stats = sim.World.Armies[0].Stats;
        Assert.InRange(stats.MassSpent, sb.MissileCostMass * 0.98f, sb.MissileCostMass * 1.1f);
        // Stockpile is capped.
        SiloSystem.SetStock(sim.World, tml.Index, 99);
        Assert.Equal(sb.MaxStorage, s.Silo[tml.Index].Stock);
    }

    private static Simulation BigFlat() => CombatWorld.Flat(size: 256);

    [Fact]
    public void TmlLaunchHitsGroundTarget()
    {
        var sim = BigFlat();
        var tml = CombatWorld.Spawn(sim, "uef_t2_tactical_missile_launcher", 0, 40, 40);
        var pd = CombatWorld.Spawn(sim, "uef_t1_point_defense", 1, 180, 120);
        sim.Step();
        SiloSystem.SetStock(sim.World, tml.Index, 1);
        // Not a launcher: Launch orders are ignored; a launcher ignores move orders.
        var tank = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 50, 50);
        sim.Step([
            new IssueOrderCommand(0, [tml, tank], Order.LaunchAt(sim.World.Units.Pos[pd.Index]), false),
            new IssueOrderCommand(0, [tml], Order.MoveTo(new Vector2(60, 60)), true),
        ]);
        Assert.True(sim.World.Units.Orders[tank.Index].IsEmpty);
        var s = StrategicState.Of(sim.World);
        Assert.Equal(0, s.Silo[tml.Index].Stock);
        Assert.Contains(sim.World.Events.Effects, e => e.Kind == EffectKind.MissileLaunch);
        float maxY = 0;
        int t = 0;
        while (CombatWorld.Alive(sim, pd) && t++ < 400)
        {
            sim.Step();
            var P = sim.World.Projectiles;
            for (int k = 0; k < P.AliveCount; k++) maxY = MathF.Max(maxY, P.Pos[P.AliveList[k]].Y);
        }
        Assert.False(CombatWorld.Alive(sim, pd), "2000 damage kills a T1 PD");
        Assert.True(maxY > 10f + 12f, $"cruise missile should fly high (max y {maxY})");
        Assert.InRange(t, 40, 250);
    }

    [Fact]
    public void LaunchOutOfRangeIsDroppedAndEmptyStockpileWaits()
    {
        var sim = BigFlat();
        var tml = CombatWorld.Spawn(sim, "uef_t2_tactical_missile_launcher", 0, 30, 30);
        sim.Step();
        // Too close (MinRange 15).
        sim.Step([new IssueOrderCommand(0, [tml], Order.LaunchAt(new Vector2(35, 30)), false)]);
        sim.Step();
        Assert.True(sim.World.Units.Orders[tml.Index].IsEmpty);
        // In range, empty stockpile: the order waits.
        sim.Step([new IssueOrderCommand(0, [tml], Order.LaunchAt(new Vector2(150, 30)), false)]);
        for (int t = 0; t < 5; t++) sim.Step();
        Assert.False(sim.World.Units.Orders[tml.Index].IsEmpty);
        SiloSystem.SetStock(sim.World, tml.Index, 1);
        sim.Step();
        Assert.True(sim.World.Units.Orders[tml.Index].IsEmpty);
    }

    [Fact]
    public void TmdInterceptsTacticalMissile()
    {
        var sim = BigFlat();
        var tml = CombatWorld.Spawn(sim, "uef_t2_tactical_missile_launcher", 0, 40, 40);
        var pd = CombatWorld.Spawn(sim, "uef_t1_point_defense", 1, 180, 120);
        CombatWorld.Spawn(sim, "uef_t2_tactical_missile_defense", 1, 176, 116);
        sim.Step();
        SiloSystem.SetStock(sim.World, tml.Index, 1);
        sim.Step([new IssueOrderCommand(0, [tml], Order.LaunchAt(sim.World.Units.Pos[pd.Index]), false)]);
        int intercepted = 0;
        for (int t = 0; t < 300; t++)
        {
            sim.Step();
            intercepted += Count(sim, EffectKind.MissileIntercepted);
            sim.World.Events.Effects.Clear();
        }
        Assert.Equal(1, intercepted);
        Assert.Equal(TestUtil.Db["uef_t1_point_defense"].MaxHealth, CombatWorld.Health(sim, pd));
        Assert.Empty(StrategicState.Of(sim.World).Missiles);
    }

    [Fact]
    public void TmdIsRateLimited()
    {
        var sim = BigFlat();
        var tmls = new List<UnitHandle>();
        for (int k = 0; k < 3; k++) tmls.Add(CombatWorld.Spawn(sim, "uef_t2_tactical_missile_launcher", 0, 30 + k * 4, 40));
        var pd = CombatWorld.Spawn(sim, "uef_t1_point_defense", 1, 180, 120);
        CombatWorld.Spawn(sim, "uef_t2_tactical_missile_defense", 1, 176, 116);
        sim.Step();
        foreach (var h in tmls) SiloSystem.SetStock(sim.World, h.Index, 1);
        // Three missiles arriving together: one TMD (1 shot / 2 s) can't stop them all.
        sim.Step([new IssueOrderCommand(0, tmls.ToArray(), Order.LaunchAt(sim.World.Units.Pos[pd.Index]), false)]);
        int intercepted = 0;
        for (int t = 0; t < 300; t++)
        {
            sim.Step();
            intercepted += Count(sim, EffectKind.MissileIntercepted);
            sim.World.Events.Effects.Clear();
        }
        Assert.InRange(intercepted, 1, 2);
        Assert.False(CombatWorld.Alive(sim, pd));
    }

    [Fact]
    public void ShieldStopsTacticalMissile()
    {
        var sim = BigFlat();
        Power(sim, 1, 200);
        var tml = CombatWorld.Spawn(sim, "uef_t2_tactical_missile_launcher", 0, 40, 40);
        var pd = CombatWorld.Spawn(sim, "uef_t1_point_defense", 1, 180, 120);
        var gen = CombatWorld.Spawn(sim, "uef_t2_shield_generator", 1, 184, 120);
        sim.Step();
        SiloSystem.SetStock(sim.World, tml.Index, 1);
        sim.Step([new IssueOrderCommand(0, [tml], Order.LaunchAt(sim.World.Units.Pos[pd.Index]), false)]);
        for (int t = 0; t < 300; t++) sim.Step();
        var sb = TestUtil.Db["uef_t2_shield_generator"].Shield!;
        Assert.Equal(TestUtil.Db["uef_t1_point_defense"].MaxHealth, CombatWorld.Health(sim, pd));
        Assert.True(ShieldOf(sim, gen).Hp < sb.MaxHealth - 1500 + sb.RegenRate * 25, $"shield hp {ShieldOf(sim, gen).Hp}");
    }

    // ------------------------------------------------------------------ nukes

    [Fact]
    public void NukeDamageRadiiAndShieldsIgnored()
    {
        var sim = BigFlat();
        var silo = CombatWorld.Spawn(sim, "uef_t3_strategic_missile_launcher", 0, 30, 30);
        var target = new Vector2(180, 180);
        Power(sim, 1, 350);
        var gen = CombatWorld.Spawn(sim, "uef_t3_heavy_shield_generator", 1, 176, 176);
        var center = CombatWorld.Spawn(sim, "uef_t1_point_defense", 1, target.X, target.Y + 4);
        var inner = CombatWorld.Spawn(sim, "uef_t2_artillery_installation", 1, target.X + 16, target.Y);
        var outer = CombatWorld.Spawn(sim, "uef_t2_artillery_installation", 1, target.X - 30, target.Y);
        var outerTank = CombatWorld.Spawn(sim, "uef_t1_tank", 1, target.X, target.Y - 32);
        var far = CombatWorld.Spawn(sim, "uef_t1_tank", 1, target.X, target.Y + 60);
        sim.Step();
        Assert.True(ShieldSystem.IsUp(sim.World, gen.Index));
        SiloSystem.SetStock(sim.World, silo.Index, 1);
        sim.Step([new IssueOrderCommand(0, [silo], Order.LaunchAt(target), false)]);
        var s = StrategicState.Of(sim.World);
        Assert.Equal(1, s.LaunchSerial);
        float maxY = 0;
        bool detonated = false;
        for (int t = 0; t < 900 && !detonated; t++)
        {
            sim.Step();
            var P = sim.World.Projectiles;
            for (int k = 0; k < P.AliveCount; k++) maxY = MathF.Max(maxY, P.Pos[P.AliveList[k]].Y);
            detonated = sim.World.Events.Effects.Any(e => e.Kind == EffectKind.NukeDetonation);
            sim.World.Events.Effects.Clear();
        }
        Assert.True(detonated);
        Assert.True(maxY > 60f, $"nuke should fly a high arc (max y {maxY})");
        Assert.False(CombatWorld.Alive(sim, center));
        Assert.False(CombatWorld.Alive(sim, inner), "inner ring: 40000 damage");
        float artyMax = TestUtil.Db["uef_t2_artillery_installation"].MaxHealth;
        float outerHp = CombatWorld.Health(sim, outer);
        Assert.InRange(outerHp, artyMax - 3000 - 1, artyMax - 3000 + 50); // outer ring: 3000
        Assert.False(CombatWorld.Alive(sim, outerTank));
        Assert.Equal(TestUtil.Db["uef_t1_tank"].MaxHealth, CombatWorld.Health(sim, far));
        Assert.False(ShieldSystem.IsUp(sim.World, gen.Index), "nukes knock shields down");
    }

    [Fact]
    public void AntiNukeInterceptsAndConsumesStockpile()
    {
        var sim = BigFlat();
        var silo = CombatWorld.Spawn(sim, "uef_t3_strategic_missile_launcher", 0, 30, 30);
        var target = new Vector2(180, 180);
        var anti = CombatWorld.Spawn(sim, "uef_t3_strategic_missile_defense", 1, 200, 200);
        var pd = CombatWorld.Spawn(sim, "uef_t1_point_defense", 1, target.X, target.Y);
        sim.Step();
        SiloSystem.SetStock(sim.World, silo.Index, 1);
        SiloSystem.SetStock(sim.World, anti.Index, 2);
        // Anti-nukes can't be ordered to launch.
        sim.Step([new IssueOrderCommand(1, [anti], Order.LaunchAt(new Vector2(40, 40)), false)]);
        Assert.True(sim.World.Units.Orders[anti.Index].IsEmpty);
        sim.Step([new IssueOrderCommand(0, [silo], Order.LaunchAt(target), false)]);
        int intercepted = 0, detonations = 0;
        for (int t = 0; t < 900; t++)
        {
            sim.Step();
            intercepted += Count(sim, EffectKind.MissileIntercepted);
            detonations += Count(sim, EffectKind.NukeDetonation);
            sim.World.Events.Effects.Clear();
        }
        var s = StrategicState.Of(sim.World);
        Assert.Equal(1, intercepted);
        Assert.Equal(0, detonations);
        Assert.Equal(1, s.Silo[anti.Index].Stock);
        Assert.True(CombatWorld.Alive(sim, pd));
    }

    // ------------------------------------------------------------------ artillery structures

    [Theory]
    [InlineData("uef_t2_artillery_installation", 110f, 600)]
    [InlineData("uef_t3_heavy_artillery", 330f, 1200)]
    public void ArtilleryStructureHitsAtLongRange(string bp, float distance, int ticks)
    {
        var sim = CombatWorld.Flat(size: 512);
        sim.World.Intel.Enabled = false; // targets known (intel tested elsewhere)
        var arty = CombatWorld.Spawn(sim, bp, 0, 60, 200);
        var targets = new List<UnitHandle>();
        for (int k = 0; k < 4; k++) targets.Add(CombatWorld.Spawn(sim, "uef_t1_mass_storage", 1, 60 + distance + (k % 2) * 3, 198 + (k / 2) * 3));
        for (int t = 0; t < ticks; t++) sim.Step();
        float max = TestUtil.Db["uef_t1_mass_storage"].MaxHealth;
        int damaged = targets.Count(h => CombatWorld.Health(sim, h) < max);
        Assert.True(damaged >= 1, $"{bp}: no target damaged at {distance}");
        Assert.True(CombatWorld.Alive(sim, arty));
    }

    // ------------------------------------------------------------------ snapshot + determinism

    [Fact]
    public void SnapshotShowsShieldsSilosAndGlobalLaunchWarning()
    {
        var sim = BigFlat();
        Power(sim, 1, 200);
        var silo = CombatWorld.Spawn(sim, "uef_t3_strategic_missile_launcher", 0, 30, 30);
        CombatWorld.Spawn(sim, "uef_t2_shield_generator", 1, 200, 200);
        CombatWorld.Spawn(sim, "uef_t1_radar", 1, 204, 204);
        sim.Step();
        SiloSystem.SetStock(sim.World, silo.Index, 1);
        sim.Step([new IssueOrderCommand(0, [silo], Order.LaunchAt(new Vector2(200, 200)), false)]);
        var own = new Snapshot.SimSnapshot();
        var enemy = new Snapshot.SimSnapshot();
        sim.WriteSnapshot(own, 0);
        sim.WriteSnapshot(enemy, 1);
        Assert.Equal(1, own.SiloCount);
        Assert.Equal(0, enemy.SiloCount); // enemy silos are secret
        Assert.Equal(1, enemy.ShieldCount);
        Assert.Equal(0, own.ShieldCount); // fog: army 0 can't see the shield
        Assert.Single(enemy.Launches);
        Assert.True(float.IsNaN(enemy.Launches[0].Target.X), "enemy launch target hidden");
        Assert.Equal(new Vector2(200, 200), own.Launches[0].Target);
        Assert.Equal(1, enemy.LaunchSerial);
    }

    [Fact]
    public void StrategicCombatIsDeterministicAcrossThreadCounts()
    {
        ulong RunWith(int threads)
        {
            int old = Par.MaxThreads;
            Par.MaxThreads = threads;
            try
            {
                Pgens = 0;
                var sim = BigFlat();
                var w = sim.World;
                Power(sim, 0, 400);
                Power(sim, 1, 400);
                CombatWorld.Spawn(sim, "uef_t2_shield_generator", 0, 90, 90);
                CombatWorld.Spawn(sim, "uef_t2_shield_generator", 1, 170, 170);
                CombatWorld.Spawn(sim, "uef_t2_tactical_missile_defense", 1, 166, 160);
                var tml = CombatWorld.Spawn(sim, "uef_t2_tactical_missile_launcher", 0, 60, 60);
                var arty = CombatWorld.Spawn(sim, "uef_t2_artillery_installation", 1, 200, 200);
                var a = new List<UnitHandle>();
                var b = new List<UnitHandle>();
                for (int k = 0; k < 60; k++)
                {
                    a.Add(CombatWorld.Spawn(sim, k % 3 == 0 ? "uef_t1_artillery" : "uef_t1_tank", 0, 80 + k % 10 * 1.6f, 80 + k / 10 * 1.6f));
                    b.Add(CombatWorld.Spawn(sim, k % 3 == 0 ? "uef_t1_artillery" : "uef_t1_tank", 1, 180 - k % 10 * 1.6f, 180 - k / 10 * 1.6f));
                }
                CombatWorld.Spawn(sim, "uef_t2_mobile_shield", 0, 85, 85);
                sim.Step();
                SiloSystem.SetStock(w, tml.Index, 4);
                sim.Step([
                    new IssueOrderCommand(0, a.ToArray(), Order.AttackMoveTo(new Vector2(170, 170)), false),
                    new IssueOrderCommand(1, b.ToArray(), Order.AttackMoveTo(new Vector2(90, 90)), false),
                    new IssueOrderCommand(0, [tml], Order.LaunchAt(new Vector2(170, 170)), false),
                    new IssueOrderCommand(0, [tml], Order.LaunchAt(new Vector2(200, 200)), true),
                    new IssueOrderCommand(0, [tml], Order.LaunchAt(new Vector2(166, 160)), true),
                ]);
                for (int t = 0; t < 400; t++) sim.Step();
                Assert.True(StrategicState.Of(w).Shield.Length > 0);
                _ = arty;
                return sim.StateHash();
            }
            finally { Par.MaxThreads = old; }
        }

        Assert.Equal(RunWith(1), RunWith(8));
    }

    [Fact]
    public void StrategicApiLaunchNukeWithoutSilo()
    {
        var sim = BigFlat();
        var target = new Vector2(180, 180);
        var victim = CombatWorld.Spawn(sim, "uef_t1_tank", 1, target.X, target.Y);
        sim.Step();
        Assert.Equal(-1, StrategicApi.LaunchNuke(sim.World, 99, target));
        Assert.Equal(-1, StrategicApi.LaunchNuke(sim.World, 0, new Vector2(20, 20), target, "uef_t1_tank"));
        int p = StrategicApi.LaunchNuke(sim.World, 0, new Vector2(20, 20), target);
        Assert.True(p >= 0);
        var s = StrategicState.Of(sim.World);
        Assert.Equal(1, s.LaunchSerial);
        bool detonated = false;
        for (int t = 0; t < 900 && !detonated; t++)
        {
            sim.Step();
            detonated = sim.World.Events.Effects.Any(e => e.Kind == EffectKind.NukeDetonation);
        }
        Assert.True(detonated);
        sim.Step();
        Assert.False(CombatWorld.Alive(sim, victim));
    }

    [Fact]
    public void NukeDetonationIsVisibleThroughFog()
    {
        var sim = BigFlat();
        Assert.True(sim.World.Intel.Enabled);
        CombatWorld.Spawn(sim, "uef_t1_tank", 1, 200, 200);
        sim.Step();
        Assert.True(StrategicApi.LaunchNuke(sim.World, 1, new Vector2(220, 220), new Vector2(200, 30)) >= 0);
        var snap = new Rts.Sim.Snapshot.SimSnapshot();
        bool seen = false;
        for (int t = 0; t < 900 && !seen; t++)
        {
            sim.Step();
            Rts.Sim.Snapshot.SnapshotWriter.Write(sim.World, snap, 0); // army 0 has no units: everything is fogged
            seen = snap.Effects.Any(e => e.Kind == EffectKind.NukeDetonation);
            Assert.True(snap.LaunchSerial == 1);
        }
        Assert.True(seen, "nuclear detonations are global");
    }

    [Theory]
    [InlineData("uef_t2_engineer", "uef_t2_shield_generator", true)]
    [InlineData("uef_t2_engineer", "uef_t2_tactical_missile_launcher", true)]
    [InlineData("uef_t2_engineer", "uef_t2_tactical_missile_defense", true)]
    [InlineData("uef_t2_engineer", "uef_t2_artillery_installation", true)]
    [InlineData("uef_t2_engineer", "uef_t3_strategic_missile_launcher", false)]
    [InlineData("uef_t3_engineer", "uef_t2_shield_generator", true)]
    [InlineData("uef_t3_engineer", "uef_t3_heavy_shield_generator", true)]
    [InlineData("uef_t3_engineer", "uef_t3_strategic_missile_launcher", true)]
    [InlineData("uef_t3_engineer", "uef_t3_strategic_missile_defense", true)]
    [InlineData("uef_t3_engineer", "uef_t3_heavy_artillery", true)]
    [InlineData("uef_t2_land_factory", "uef_t2_mobile_shield", true)]
    [InlineData("uef_t3_land_factory", "uef_t2_mobile_shield", true)]
    public void StrategicStructuresAreInBuildLists(string builder, string unit, bool expected)
    {
        var db = TestUtil.Db;
        Assert.Equal(expected, db[builder].BuildIndices.Contains(db.IndexOf(unit)));
    }
}

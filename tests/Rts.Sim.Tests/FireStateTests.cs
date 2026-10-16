using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Fire at will / Return fire / Hold fire (Combat/FireStates) and holding fire on captured units.</summary>
public class FireStateTests
{
    private static readonly Vector2 P = new(50, 64);

    /// <summary>Flat map; with commanders far away (storage for captures) when <paramref name="commanders"/>.</summary>
    private static Simulation World(bool commanders = false)
    {
        var sim = CombatWorld.Flat();
        if (commanders)
        {
            CombatWorld.Spawn(sim, "uef_commander", 0, 115, 10);
            CombatWorld.Spawn(sim, "uef_commander", 1, 115, 118);
        }
        return sim;
    }

    private static bool HasTarget(SimWorld w, UnitHandle h)
    {
        foreach (var ws in w.Units.WeaponsOf(h.Index)) if (!ws.Target.IsNone) return true;
        return false;
    }

    private static (Simulation sim, UnitHandle mine, UnitHandle enemy) Duel(FireState mineState, FireState enemyState)
    {
        var sim = World();
        var p = P;
        var mine = TestUtil.Spawn(sim, "uef_t1_tank", 0, p);
        var enemy = TestUtil.Spawn(sim, "uef_t1_tank", 1, p + new Vector2(10, 0), MathF.PI);
        sim.Step([new SetFireStateCommand(0, [mine], mineState), new SetFireStateCommand(1, [enemy], enemyState)]);
        return (sim, mine, enemy);
    }

    [Fact]
    public void HoldFireNeverAcquiresButAttackOrdersStillShoot()
    {
        var (sim, mine, enemy) = Duel(FireState.HoldFire, FireState.HoldFire);
        var w = sim.World;
        Assert.Equal(FireState.HoldFire, w.Units.FireStates[mine.Index]);
        float hp = w.Units.Health[enemy.Index];
        for (int t = 0; t < 60; t++)
        {
            sim.Step();
            Assert.False(HasTarget(w, mine));
            Assert.False(HasTarget(w, enemy));
        }
        Assert.Equal(hp, w.Units.Health[enemy.Index]);
        TestUtil.Run(sim, 60, new IssueOrderCommand(0, [mine], Order.AttackUnit(enemy), false));
        Assert.True(!w.Units.IsActive(enemy) || w.Units.Health[enemy.Index] < hp);
    }

    [Fact]
    public void ReturnFireWaitsUntilShotAt()
    {
        var (sim, mine, enemy) = Duel(FireState.ReturnFire, FireState.HoldFire);
        var w = sim.World;
        float myHp = w.Units.Health[mine.Index];
        float enemyHp = w.Units.Health[enemy.Index];
        for (int t = 0; t < 60; t++)
        {
            sim.Step();
            Assert.False(HasTarget(w, mine));
        }
        Assert.Equal(enemyHp, w.Units.Health[enemy.Index]);
        // The enemy opens fire: once hit, the return-fire unit answers.
        TestUtil.Run(sim, 100, new SetFireStateCommand(1, [enemy], FireState.FireAtWill));
        Assert.True(w.Units.Health[mine.Index] < myHp);
        Assert.True(!w.Units.IsActive(enemy) || w.Units.Health[enemy.Index] < enemyHp);
    }

    [Fact]
    public void FireAtWillIsTheDefaultAndForeignUnitsIgnoreTheCommand()
    {
        var sim = World();
        var w = sim.World;
        var p = P;
        var mine = TestUtil.Spawn(sim, "uef_t1_tank", 0, p);
        var enemy = TestUtil.Spawn(sim, "uef_t1_tank", 1, p + new Vector2(40, 0));
        Assert.Equal(FireState.FireAtWill, w.Units.FireStates[mine.Index]);
        sim.Step([new SetFireStateCommand(0, [mine, enemy], FireState.HoldFire)]);
        Assert.Equal(FireState.HoldFire, w.Units.FireStates[mine.Index]);
        Assert.Equal(FireState.FireAtWill, w.Units.FireStates[enemy.Index]); // not ours
        sim.Step([new SetFireStateCommand(0, [mine], (FireState)99)]); // invalid value ignored
        Assert.Equal(FireState.HoldFire, w.Units.FireStates[mine.Index]);
        var snap = new Snapshot.SimSnapshot();
        Snapshot.SnapshotWriter.Write(w, snap);
        Assert.Contains(snap.Units.Take(snap.UnitCount), v => v.Index == mine.Index && v.FireState == FireState.HoldFire);
    }

    [Fact]
    public void UnitsHoldFireOnAUnitTheirArmyIsCapturing()
    {
        var sim = World(commanders: true);
        var w = sim.World;
        var p = P;
        var pg = TestUtil.Spawn(sim, "uef_t1_power_generator", 1, p);
        var eng = TestUtil.Spawn(sim, "uef_t1_engineer", 0, p + new Vector2(0, 4));
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 0, p + new Vector2(8, 0));
        var other = TestUtil.Spawn(sim, "uef_t1_power_generator", 1, p + new Vector2(16, 0)); // control: not captured
        float hp = w.Units.Health[pg.Index];
        bool captured = false;
        for (int t = 1; t <= 300 && !captured; t++)
        {
            var e = w.Armies[0].Eco;
            e.Energy = e.EnergyMax;
            e.Mass = e.MassMax;
            sim.Step(t == 1 ? [new IssueOrderCommand(0, [eng], Order.CaptureUnit(pg), false)] : []);
            captured = w.Units.Army[pg.Index] == 0;
            if (!captured) Assert.Equal(hp, w.Units.Health[pg.Index]); // the tank next to it never shoots it
        }
        Assert.True(captured);
        Assert.True(w.Units.IsActive(tank));
        Assert.True(!w.Units.IsActive(other) || w.Units.Health[other.Index] < hp); // the tank does shoot other targets
    }

    [Fact]
    public void CaptorWithAGunDoesNotShootItsCaptureTarget()
    {
        var sim = World(commanders: true);
        var w = sim.World;
        var p = P;
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 1, p);
        var acu = TestUtil.Spawn(sim, "uef_commander", 0, p + new Vector2(25, 0));
        sim.Step([new SetFireStateCommand(1, [tank], FireState.HoldFire)]);
        float hp = w.Units.Health[tank.Index];
        sim.Step([new IssueOrderCommand(0, [acu], Order.CaptureUnit(tank), false)]);
        for (int t = 0; t < 80; t++)
        {
            var e = w.Armies[0].Eco;
            e.Energy = e.EnergyMax;
            sim.Step();
            if (w.Units.Army[tank.Index] == 0) break;
            Assert.Equal(hp, w.Units.Health[tank.Index]);
        }
    }
}

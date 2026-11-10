using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Naval;
using Rts.Sim.Orders;
using Rts.Sim.Strategic;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>
/// T3 strategic missile submarines: a mobile, diving strategic silo. It builds and launches its nuke while submerged
/// (also on the move) and fires its cruise/tactical missiles from under water (weapon json <c>FiresSubmerged</c>).
/// </summary>
public class MissileSubTests
{
    private static void Rich(SimWorld w)
    {
        foreach (var a in w.Armies) { a.Eco.Mass = a.Eco.MassMax; a.Eco.Energy = a.Eco.EnergyMax; }
    }

    private static void AssertStaysDown(SimWorld w, UnitHandle sub)
    {
        Assert.True(Submarines.IsSubmerged(w, sub.Index), "the sub surfaced");
        Assert.False(Submarines.IsSurfaced(w, sub.Index));
        Assert.Equal(LayerMask.Sub, OrderSystem.LayerOf(w, sub.Index));
        Assert.Equal(w.Terrain.WaterLevel - NavalMotion.SubDepth, w.Units.Elevation[sub.Index], 3);
    }

    /// <summary>Steps until the unit dies or no strategic missile is left; returns the highest point a missile reached.</summary>
    private static float FlyMissiles(Simulation sim, UnitHandle sub, int maxTicks, out bool sawMissile, out float firstHeight)
    {
        var w = sim.World;
        var s = StrategicState.Of(w);
        float top = float.MinValue;
        sawMissile = false;
        firstHeight = float.NaN;
        for (int t = 0; t < maxTicks; t++)
        {
            sim.Step();
            AssertStaysDown(w, sub);
            foreach (int p in s.Missiles)
            {
                if (!sawMissile) firstHeight = w.Projectiles.Pos[p].Y;
                sawMissile = true;
                top = MathF.Max(top, w.Projectiles.Pos[p].Y);
            }
            if (sawMissile && s.Missiles.Count == 0) break;
        }
        return top;
    }

    [Theory]
    [InlineData("uef_t3_strategic_missile_sub")]
    [InlineData("cybran_t3_strategic_missile_sub")]
    public void BlueprintIsADivingMobileStrategicSilo(string id)
    {
        var bp = TestUtil.Db[id];
        Assert.True(bp.IsMobile);
        Assert.True(Submarines.CanDive(bp));
        Assert.True(SiloSystem.IsLauncher(bp));
        Assert.Equal(SiloKind.Strategic, bp.Silo!.Kind);
        Assert.Equal(1, bp.Silo.MaxStorage);
        Assert.False(bp.Weapons[bp.Silo.WeaponIndex].FiresSubmerged); // the nuke is launched by the silo, not the weapon system
        Assert.Contains(bp.Weapons, wb => wb.FiresSubmerged && wb.Range >= 150);
    }

    [Theory]
    [InlineData("uef_t3_strategic_missile_sub")]
    [InlineData("cybran_t3_strategic_missile_sub")]
    public void SubmergedSubBuildsOneMissileAndLaunchesWithoutSurfacing(string id)
    {
        var sim = NavalWorld.Basin(strip: false);
        var w = sim.World;
        var s = StrategicState.Of(w);
        var sub = NavalWorld.Spawn(sim, id, 0, 20, 20);
        var target = NavalWorld.Spawn(sim, "uef_t1_frigate", 1, 100, 100); // 113 away
        var far = NavalWorld.Spawn(sim, "uef_t1_frigate", 1, 100, 30);     // 70 from the blast: untouched
        var sb = w.BpOf(sub.Index).Silo!;
        // Storage for the missile's per-tick cost (structures spawned straight on the sea: test only).
        NavalWorld.Spawn(sim, "uef_t1_energy_storage", 0, 8, 120);
        NavalWorld.Spawn(sim, "uef_t1_mass_storage", 0, 14, 120);
        sim.Step([new SetFireStateCommand(1, [target, far], FireState.HoldFire)]);
        AssertStaysDown(w, sub);

        // Builds under water with a rich economy; a Launch order given early waits for the missile.
        sim.Step([new IssueOrderCommand(0, [sub], Order.LaunchAt(w.Units.Pos[target.Index]), false)]);
        int buildTicks = (int)(sb.MissileBuildTime / SimWorld.Dt);
        int launchedAt = -1;
        for (int t = 0; t < buildTicks + 50 && launchedAt < 0; t++)
        {
            Rich(w);
            sim.Step();
            AssertStaysDown(w, sub);
            if (s.Missiles.Count > 0) launchedAt = t;
            else if (t == buildTicks / 2) Assert.InRange(s.Silo[sub.Index].Progress, 0.4f, 0.6f);
        }
        Assert.True(launchedAt >= buildTicks - 20, $"missile launched at tick {launchedAt}, build takes {buildTicks}");
        Assert.Equal(0, s.Silo[sub.Index].Stock);

        float startY = w.Projectiles.Pos[s.Missiles[0]].Y;
        Assert.InRange(startY, w.Terrain.WaterLevel - 0.5f, w.Terrain.WaterLevel + 6f); // leaves the water at the surface
        float top = FlyMissiles(sim, sub, 900, out _, out _);
        Assert.True(top > w.Terrain.WaterLevel + 30f, $"the missile should climb (top {top:F1})");
        Assert.Empty(s.Missiles);
        Assert.False(CombatWorld.Alive(sim, target), "the blast should kill the distant target");
        Assert.True(CombatWorld.Alive(sim, far));
        Assert.Equal(TestUtil.Db["uef_t1_frigate"].MaxHealth, CombatWorld.Health(sim, far));
        Assert.Equal(w.BpOf(sub.Index).MaxHealth, CombatWorld.Health(sim, sub));

        // Storage limit: the next missile is built, then the silo stops at MaxStorage (1).
        for (int t = 0; t < buildTicks + 300; t++) { Rich(w); sim.Step(); }
        Assert.Equal(1, s.Silo[sub.Index].Stock);
        Assert.Equal(0f, s.Silo[sub.Index].Progress);
        SiloSystem.SetStock(w, sub.Index, 5);
        Assert.Equal(sb.MaxStorage, s.Silo[sub.Index].Stock);
        AssertStaysDown(w, sub);
    }

    [Theory]
    [InlineData("uef_t3_strategic_missile_sub")]
    [InlineData("cybran_t3_strategic_missile_sub")]
    public void CruiseMissilesFireSubmergedAtALandTarget(string id)
    {
        var sim = NavalWorld.Basin(); // land strip x in [60, 68]
        var w = sim.World;
        var sub = NavalWorld.Spawn(sim, id, 0, 90, 64);
        var pd = NavalWorld.Spawn(sim, "uef_t1_point_defense", 1, 66, 64); // 24 away: seen, beyond MinRange
        int subBp = w.Units.Bp[sub.Index];
        float full = CombatWorld.Health(sim, pd);
        sim.Step([new IssueOrderCommand(0, [sub], Order.AttackUnit(pd), false)]);
        bool missile = false;
        for (int t = 0; t < 600 && CombatWorld.Alive(sim, pd) && CombatWorld.Health(sim, pd) >= full; t++)
        {
            sim.Step();
            AssertStaysDown(w, sub);
            for (int p = 0; p < w.Projectiles.Capacity; p++)
            {
                if (!w.Projectiles.Alive[p] || w.Projectiles.SourceBp[p] != subBp) continue;
                missile = true;
                // The nuke (weapon 0, WeaponKind.Manual) is never fired by the weapon system, attack order or not.
                Assert.NotEqual(w.BpOf(sub.Index).Silo!.WeaponIndex, w.Projectiles.WeaponIndex[p]);
            }
        }
        Assert.True(missile, "the submerged sub should fire its cruise missiles");
        Assert.True(CombatWorld.Health(sim, pd) < full, "the cruise missile should hit the structure on land");
        Assert.Equal(w.BpOf(sub.Index).MaxHealth, CombatWorld.Health(sim, sub)); // a Land/Water gun cannot answer
        Assert.Empty(StrategicState.Of(w).Missiles);
    }

    /// <summary>
    /// A Launch order that replaces a Move fires at once while the sub still coasts (launch on the move is allowed:
    /// SiloSystem has no standing-still rule); a queued Launch fires after the move.
    /// </summary>
    [Theory]
    [InlineData("uef_t3_strategic_missile_sub")]
    [InlineData("cybran_t3_strategic_missile_sub")]
    public void LaunchOnTheMoveAndQueuedAfterAMove(string id)
    {
        var sim = NavalWorld.Basin(strip: false);
        var w = sim.World;
        var s = StrategicState.Of(w);
        var sub = NavalWorld.Spawn(sim, id, 0, 20, 20);
        var queued = NavalWorld.Spawn(sim, id, 0, 20, 110);
        var target = NavalWorld.Spawn(sim, "uef_t1_frigate", 1, 110, 64);
        SiloSystem.SetStock(w, sub.Index, 1);
        SiloSystem.SetStock(w, queued.Index, 1);
        var aim = w.Units.Pos[target.Index];
        TestUtil.Run(sim, 50,
            new SetFireStateCommand(1, [target], FireState.HoldFire),
            new IssueOrderCommand(0, [sub], Order.MoveTo(new Vector2(20, 60)), false),
            new IssueOrderCommand(0, [queued], Order.MoveTo(new Vector2(50, 110)), false),
            new IssueOrderCommand(0, [queued], Order.LaunchAt(aim), true));
        Assert.True((w.Units.Flags[sub.Index] & UnitFlags.Moving) != 0);
        Assert.Empty(s.Missiles);

        TestUtil.Run(sim, 2, new IssueOrderCommand(0, [sub], Order.LaunchAt(aim), false));
        Assert.Single(s.Missiles);
        Assert.True((w.Units.Flags[sub.Index] & UnitFlags.Moving) != 0, "launched while still under way");
        Assert.Equal(0, s.Silo[sub.Index].Stock);
        Assert.Equal(1, s.Silo[queued.Index].Stock);
        AssertStaysDown(w, sub);
        var launchPos = w.Units.Pos[sub.Index];

        // The queued launch waits for its move to end, then fires; both stay submerged, the first sub just stops.
        bool second = false;
        for (int t = 0; t < 900; t++)
        {
            sim.Step();
            AssertStaysDown(w, sub);
            AssertStaysDown(w, queued);
            if (s.Silo[queued.Index].Stock == 0 && !second)
            {
                second = true;
                Assert.True(w.Units.Pos[queued.Index].X > 40f, $"queued launch fired before the move ended, at {w.Units.Pos[queued.Index]}");
            }
            if (second && s.Missiles.Count == 0) break;
        }
        Assert.True(second, "the queued Launch should fire after the move");
        Assert.Empty(s.Missiles);
        Assert.False(CombatWorld.Alive(sim, target));
        Assert.True(Vector2.Distance(launchPos, w.Units.Pos[sub.Index]) < 8f, "the sub should coast to a stop after the launch");
        Assert.True((w.Units.Flags[sub.Index] & UnitFlags.Moving) == 0);
    }
}

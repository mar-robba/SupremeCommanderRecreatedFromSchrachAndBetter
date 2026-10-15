using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.Strategic;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>The sandbox nuke fires a real strategic missile (StrategicApi.LaunchNuke); instant build fills silo stockpiles.</summary>
public class SandboxNukeTests
{
    private static Simulation FlatSandbox()
    {
        var sim = CombatWorld.Flat(size: 256);
        SandboxSystem.Enable(sim.World);
        return sim;
    }

    private static int Count(Simulation sim, EffectKind kind) => sim.World.Events.Effects.Count(e => e.Kind == kind);

    [Fact]
    public void SandboxNukeLaunchesAMissileThatDetonatesAtTheTargetAfterItsFlight()
    {
        var sim = FlatSandbox();
        var w = sim.World;
        var target = new Vector2(180, 180);
        var pd = CombatWorld.Spawn(sim, "uef_t1_point_defense", 1, target.X, target.Y + 3);
        // Outside the damage rings, but its dome reaches the outer ring: knocked down, generator unharmed.
        var gen = CombatWorld.Spawn(sim, "uef_t3_heavy_shield_generator", 1, target.X + 50, target.Y);
        sim.Step([new SandboxToggleCommand(0, 1, SandboxToggle.InfiniteResources, true)]);
        sim.Step();
        Assert.True(ShieldSystem.IsUp(w, gen.Index));

        sim.Step([new SandboxNukeCommand(0, target)]);
        int launchTick = w.Tick;
        var s = StrategicState.Of(w);
        Assert.Equal(1, s.LaunchSerial); // global "strategic launch detected" warning
        int p = Assert.Single(s.Missiles);
        Assert.Equal(MissileRole.Strategic, s.Role[p]);
        Assert.Equal(0, w.Projectiles.Army[p]);
        // No commander or silo: it takes off from the map edge (nearest to the army's start position).
        var from = s.Launches[^1].From;
        Assert.True(from.X <= 1.5f || from.Y <= 1.5f || from.X >= 254.5f || from.Y >= 254.5f, $"launch site {from}");
        var snap = new SimSnapshot();
        sim.WriteSnapshot(snap, 1); // the target's owner sees the launch warning too
        Assert.Equal(1, snap.LaunchSerial);
        Assert.True(CombatWorld.Alive(sim, pd), "nothing happens before the missile arrives");
        int flightTicks = (int)MathF.Ceiling(s.Duration[p] / SimWorld.Dt);
        Assert.True(flightTicks > 30, $"flight {flightTicks} ticks");

        int detonationTick = -1;
        Vector3 at = default;
        for (int t = 0; t < 900 && detonationTick < 0; t++)
        {
            sim.Step();
            foreach (var e in w.Events.Effects)
                if (e.Kind == EffectKind.Nuke) { detonationTick = w.Tick; at = e.Pos; }
            w.Events.Effects.Clear(); // effects pile up until a snapshot is written
        }
        Assert.True(detonationTick > 0, "the missile should detonate");
        Assert.InRange(detonationTick - launchTick, flightTicks - 2, flightTicks + 3);
        Assert.True(Vector2.Distance(new Vector2(at.X, at.Z), target) < 1.5f, $"detonation at {at}");
        Assert.False(CombatWorld.Alive(sim, pd));
        Assert.True(CombatWorld.Alive(sim, gen));
        Assert.False(ShieldSystem.IsUp(w, gen.Index), "the nuke knocks shields down");
    }

    [Fact]
    public void AnEnemyAntiNukeInRangeInterceptsTheSandboxNuke()
    {
        var sim = FlatSandbox();
        var w = sim.World;
        var target = new Vector2(180, 180);
        var pd = CombatWorld.Spawn(sim, "uef_t1_point_defense", 1, target.X, target.Y);
        var anti = CombatWorld.Spawn(sim, "uef_t3_strategic_missile_defense", 1, 200, 200);
        sim.Step();
        SiloSystem.SetStock(w, anti.Index, 1);
        sim.Step([new SandboxNukeCommand(0, target)]);
        int intercepted = 0, detonations = 0;
        for (int t = 0; t < 900; t++)
        {
            sim.Step();
            intercepted += Count(sim, EffectKind.MissileIntercepted);
            detonations += Count(sim, EffectKind.Nuke);
            w.Events.Effects.Clear(); // effects pile up until a snapshot is written
        }
        Assert.Equal(1, intercepted);
        Assert.Equal(0, detonations);
        Assert.Equal(0, StrategicState.Of(w).Silo[anti.Index].Stock);
        Assert.True(CombatWorld.Alive(sim, pd));
    }

    [Fact]
    public void InstantNukeDetonatesRightAwayAndCannotBeIntercepted()
    {
        var sim = FlatSandbox();
        var w = sim.World;
        var target = new Vector2(180, 180);
        var pd = CombatWorld.Spawn(sim, "uef_t1_point_defense", 1, target.X, target.Y);
        var anti = CombatWorld.Spawn(sim, "uef_t3_strategic_missile_defense", 1, 200, 200);
        sim.Step();
        SiloSystem.SetStock(w, anti.Index, 1);
        sim.Step([new SandboxNukeCommand(0, target, Instant: true)]);
        Assert.Equal(1, Count(sim, EffectKind.Nuke));
        Assert.Equal(0, StrategicState.Of(w).LaunchSerial);
        Assert.False(CombatWorld.Alive(sim, pd));
    }

    [Fact]
    public void LaunchSiteIsTheNearestSiloThenTheCommanderThenTheMapEdge()
    {
        var sim = FlatSandbox();
        var w = sim.World;
        var target = new Vector2(200, 128);
        var (edge, bp0) = SandboxSystem.NukeLaunchSite(w, 0, target);
        Assert.Equal("uef_t3_strategic_missile_launcher", bp0); // army 0's faction (UEF) nuke_silo role
        Assert.Equal(bp0, StrategicApi.NukeSiloOf(w, 0));
        Assert.Equal(new Vector2(1, 20), edge); // the border nearest to army 0's start position (20, 20)

        var acu = CombatWorld.Spawn(sim, "uef_commander", 0, 40, 60);
        w.Armies[0].Commander = acu;
        Assert.Equal(new Vector2(40, 60), SandboxSystem.NukeLaunchSite(w, 0, target).From);

        CombatWorld.Spawn(sim, "uef_t3_strategic_missile_launcher", 0, 30, 200);
        var near = CombatWorld.Spawn(sim, "uef_t3_strategic_missile_launcher", 0, 120, 120);
        CombatWorld.Spawn(sim, "uef_t3_strategic_missile_launcher", 1, 190, 128); // enemy silo: never used
        sim.Step();
        var (from, bp) = SandboxSystem.NukeLaunchSite(w, 0, target);
        Assert.Equal(w.Units.Pos[near.Index], from);
        Assert.Equal("uef_t3_strategic_missile_launcher", bp);
        // The sandbox missile is free: the silo's stockpile is untouched.
        sim.Step([new SandboxNukeCommand(0, target)]);
        Assert.Equal(1, StrategicState.Of(w).LaunchSerial);
        Assert.Equal(0, StrategicState.Of(w).Silo[near.Index].Stock);
    }

    [Fact]
    public void InstantBuildFillsSiloStockpilesSoANukeIsReadyAtOnce()
    {
        var sim = FlatSandbox();
        var w = sim.World;
        var silo = CombatWorld.Spawn(sim, "uef_t3_strategic_missile_launcher", 0, 40, 40);
        var anti = CombatWorld.Spawn(sim, "uef_t3_strategic_missile_defense", 0, 60, 40);
        int max = w.BpOf(silo.Index).Silo!.MaxStorage, antiMax = w.BpOf(anti.Index).Silo!.MaxStorage;
        var s = StrategicState.Of(w);

        // Infinite resources alone: the stockpile builds through the economy at full speed (minutes per nuke).
        sim.Step([new SandboxToggleCommand(0, 0, SandboxToggle.InfiniteResources, true)]);
        for (int t = 0; t < 20; t++) sim.Step();
        Assert.Equal(0, s.Silo[silo.Index].Stock);
        Assert.True(s.Silo[silo.Index].Progress > 0, "the missile is being built");

        // + instant build: every silo fills up within a few ticks.
        sim.Step([new SandboxToggleCommand(0, 0, SandboxToggle.InstantBuild, true)]);
        for (int t = 0; t < antiMax; t++) sim.Step();
        Assert.Equal(max, s.Silo[silo.Index].Stock);
        Assert.Equal(antiMax, s.Silo[anti.Index].Stock);

        // The nuke is ready: a launch order fires at once (and the stockpile refills).
        sim.Step([new IssueOrderCommand(0, [silo], Order.LaunchAt(new Vector2(200, 200)), false)]);
        Assert.Equal(1, s.LaunchSerial);
        sim.Step();
        sim.Step();
        Assert.Equal(max, s.Silo[silo.Index].Stock);
    }
}

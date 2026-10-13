using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Naval;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Submarine dive/surface toggle end to end: depth, intel, targeting, command validation.</summary>
public class SubmarineTests
{
    private static bool Surfaced(Simulation sim, UnitHandle h) => (sim.World.Units.Flags[h.Index] & UnitFlags.Surfaced) != 0;

    [Fact]
    public void SurfacedSubRidesAtTheSurfaceAndDivesBack()
    {
        var sim = NavalWorld.Basin();
        var w = sim.World;
        var sub = NavalWorld.Spawn(sim, "uef_t1_sub", 0, 100, 60);
        TestUtil.Run(sim, 5);
        Assert.Equal(w.Terrain.WaterLevel - NavalMotion.SubDepth, w.Units.Elevation[sub.Index], 3);
        Assert.Equal(LayerMask.Sub, OrderSystem.LayerOf(w, sub.Index));

        TestUtil.Run(sim, 30, new SetSubmergedCommand(0, [sub], false));
        Assert.True(Surfaced(sim, sub));
        Assert.Equal(w.Terrain.WaterLevel, w.Units.Elevation[sub.Index], 3);
        Assert.Equal(LayerMask.Water, OrderSystem.LayerOf(w, sub.Index));

        // Diving takes a moment: the sub stays on the surface layer until it is halfway down.
        sim.Step([new SetSubmergedCommand(0, [sub], true)]);
        Assert.False(Surfaced(sim, sub));
        Assert.Equal(LayerMask.Water, OrderSystem.LayerOf(w, sub.Index));
        TestUtil.Run(sim, 30);
        Assert.Equal(w.Terrain.WaterLevel - NavalMotion.SubDepth, w.Units.Elevation[sub.Index], 3);
        Assert.Equal(LayerMask.Sub, OrderSystem.LayerOf(w, sub.Index));
    }

    [Fact]
    public void SurfacedSubIsSeenByVisionAndRadarAndHiddenAgainAfterDiving()
    {
        var sim = NavalWorld.Basin(strip: true);
        var w = sim.World;
        NavalWorld.Power(sim, 1);
        NavalWorld.Spawn(sim, "uef_t1_radar", 1, 64, 64);
        var tank = NavalWorld.Spawn(sim, "uef_t1_tank", 1, 64, 20);
        var near = NavalWorld.Spawn(sim, "uef_t1_sub", 0, 74, 20);  // in the tank's vision
        var far = NavalWorld.Spawn(sim, "uef_t1_sub", 0, 104, 64);  // radar only (40 from the radar, no vision)
        TestUtil.Run(sim, 5);
        Assert.False(w.Intel.IsDetected(1, near.Index), "submerged sub seen without sonar");
        Assert.False(w.Intel.IsDetected(1, far.Index), "submerged sub on radar");

        TestUtil.Run(sim, 20, new SetSubmergedCommand(0, [near, far], false));
        Assert.True(w.Intel.IsVisible(1, near.Index), "surfaced sub should be visible to normal vision");
        Assert.False(w.Intel.IsVisible(1, far.Index));
        Assert.True(w.Intel.IsBlip(1, far.Index), "surfaced sub should be a radar blip");
        Assert.True(sim.World.Units.IsActive(tank));

        TestUtil.Run(sim, 20, new SetSubmergedCommand(0, [near, far], true));
        Assert.False(w.Intel.IsDetected(1, near.Index), "dived sub still seen without sonar");
        Assert.False(w.Intel.IsDetected(1, far.Index), "dived sub still on radar");
    }

    [Fact]
    public void FrigateGunsHitASurfacedSubButNotADivedOne()
    {
        var sim = NavalWorld.Basin(strip: false);
        var frigate = NavalWorld.Spawn(sim, "uef_t1_frigate", 1, 90, 61);
        var sub = NavalWorld.Spawn(sim, "uef_t1_sub", 0, 90, 30); // 31 away: in gauss range, out of torpedo range
        TestUtil.Run(sim, 30);
        Assert.True(CombatWorld.Weapon(sim, frigate).Target.IsNone, "frigate guns acquired a submerged sub");
        Assert.Equal(TestUtil.Db["uef_t1_sub"].MaxHealth, CombatWorld.Health(sim, sub));

        sim.Step([new SetSubmergedCommand(0, [sub], false)]);
        int k = 0;
        for (; k < 300 && CombatWorld.Health(sim, sub) >= TestUtil.Db["uef_t1_sub"].MaxHealth; k++) sim.Step();
        Assert.True(CombatWorld.Health(sim, sub) < TestUtil.Db["uef_t1_sub"].MaxHealth, "frigate should shell the surfaced sub");
        Assert.Equal(sub, CombatWorld.Weapon(sim, frigate).Target);

        TestUtil.Run(sim, 30, new SetSubmergedCommand(0, [sub], true)); // dive; shells in flight land or miss
        float hp = CombatWorld.Health(sim, sub);
        Assert.True(hp > 0);
        for (int t = 0; t < 100; t++)
        {
            sim.Step();
            Assert.True(CombatWorld.Weapon(sim, frigate).Target.IsNone, "frigate guns kept a dived sub");
        }
        Assert.Equal(hp, CombatWorld.Health(sim, sub));
    }

    [Fact]
    public void TorpedoesStillHitASurfacedSub()
    {
        var sim = NavalWorld.Basin(strip: true);
        var sub = NavalWorld.Spawn(sim, "uef_t1_sub", 0, 56, 64);
        NavalWorld.Spawn(sim, "uef_t1_torpedo_launcher", 1, 56, 84);
        sim.Step([new SetSubmergedCommand(0, [sub], false)]);
        for (int k = 0; k < 600 && CombatWorld.Health(sim, sub) >= TestUtil.Db["uef_t1_sub"].MaxHealth; k++) sim.Step();
        Assert.True(CombatWorld.Health(sim, sub) < TestUtil.Db["uef_t1_sub"].MaxHealth, "torpedoes should hit a surfaced sub");
        Assert.True(Surfaced(sim, sub));
    }

    [Fact]
    public void SurfaceCommandIgnoresForeignAndNonSubUnits()
    {
        var sim = NavalWorld.Basin();
        var sub = NavalWorld.Spawn(sim, "uef_t1_sub", 0, 100, 60);
        var frigate = NavalWorld.Spawn(sim, "uef_t1_frigate", 0, 100, 80);
        TestUtil.Run(sim, 10, new SetSubmergedCommand(1, [sub], false), new SetSubmergedCommand(0, [frigate], false));
        Assert.False(Surfaced(sim, sub), "another army surfaced my sub");
        Assert.False(Surfaced(sim, frigate), "a frigate got the surfaced flag");
        Assert.Equal(LayerMask.Sub, OrderSystem.LayerOf(sim.World, sub.Index));
        TestUtil.Run(sim, 10, new SetSubmergedCommand(0, [sub, frigate, UnitHandle.None], false));
        Assert.True(Surfaced(sim, sub));
        Assert.False(Surfaced(sim, frigate));
    }
}

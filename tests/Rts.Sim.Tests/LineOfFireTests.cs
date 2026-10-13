using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Terrain line of fire in target acquisition (units below a cliff don't lock onto targets above it).</summary>
public class LineOfFireTests
{
    /// <summary>Low ground (height 10) for x &lt; 60, a cliff up to a plateau (height 24) from x = 62.</summary>
    private static Simulation CliffWorld(bool cliff)
    {
        const int size = 128;
        var heights = new float[(size + 1) * (size + 1)];
        for (int z = 0; z <= size; z++)
        for (int x = 0; x <= size; x++)
            heights[z * (size + 1) + x] = !cliff || x <= 60 ? 10f : x >= 62 ? 24f : 17f;
        var terrain = new Terrain(size, size, heights, 0f, [], [new Vector2(20, 20), new Vector2(size - 20, size - 20)], 1);
        var arr = new[] { new Army { Index = 0, Team = 0, Name = "A0" }, new Army { Index = 1, Team = 1, Name = "A1" } };
        return new Simulation(new SimWorld(TestUtil.Db, terrain, arr, 1, unitCapacity: 256, projectileCapacity: 1024));
    }

    [Fact]
    public void ClearLineAndBlockedLineOverTerrain()
    {
        var sim = CliffWorld(cliff: true);
        var t = sim.World.Terrain;
        // Below the cliff aiming at the plateau: the rock is in the way.
        Assert.False(LineOfFire.IsClear(t, new Vector3(50, 10.5f, 64), new Vector3(68, 24.5f, 64)));
        // On the plateau, or on the low ground: clear.
        Assert.True(LineOfFire.IsClear(t, new Vector3(64, 24.5f, 64), new Vector3(80, 24.5f, 64)));
        Assert.True(LineOfFire.IsClear(t, new Vector3(30, 10.5f, 64), new Vector3(50, 10.5f, 64)));
        // Shooting down from high above the edge clears it.
        Assert.True(LineOfFire.IsClear(t, new Vector3(70, 40f, 64), new Vector3(50, 10.5f, 64)));
    }

    [Fact]
    public void TankBelowCliffDoesNotLockOntoStructureAbove()
    {
        foreach (bool cliff in new[] { true, false })
        {
            var sim = CliffWorld(cliff);
            var tank = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 50, 64);
            var pgen = CombatWorld.Spawn(sim, "uef_t1_power_generator", 1, 66, 64); // 16 away: within tank range (18)
            for (int k = 0; k < 20; k++) sim.Step();
            var target = CombatWorld.Weapon(sim, tank).Target;
            if (cliff) Assert.True(target.IsNone, "a tank under the cliff must not target the structure on the plateau");
            else Assert.Equal(pgen, target);
        }
    }

    [Fact]
    public void TankOnPlateauStillTargetsStructureOnPlateau()
    {
        var sim = CliffWorld(cliff: true);
        var tank = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 70, 64);
        var pgen = CombatWorld.Spawn(sim, "uef_t1_power_generator", 1, 84, 64);
        for (int k = 0; k < 20; k++) sim.Step();
        Assert.Equal(pgen, CombatWorld.Weapon(sim, tank).Target);
    }
}

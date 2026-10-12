using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Attack orders on targets lost in the fog: go to the last known position, then drop the order (SupCom).</summary>
public class FogAttackTests
{
    private static Simulation World()
    {
        var heights = new float[129 * 129];
        Array.Fill(heights, 10f);
        var terrain = new Terrain(128, 128, heights, 0f, [], [new Vector2(20, 20), new Vector2(108, 108)], 1);
        var armies = new[] { new Army { Index = 0, Team = 0, Name = "A0" }, new Army { Index = 1, Team = 1, Name = "A1" } };
        return new Simulation(new SimWorld(TestUtil.Db, terrain, armies, 1, unitCapacity: 1024, projectileCapacity: 4096));
    }

    [Fact]
    public void AttackOrderOnTargetLostInFogIsDroppedAtLastKnownPosition()
    {
        var sim = World();
        var w = sim.World;
        var tank = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 20, 64);
        var enemy = CombatWorld.Spawn(sim, "uef_t1_commander", 1, 36, 64); // tough, in vision (16 away)
        sim.Step();
        Assert.True(w.Intel.IsVisible(0, enemy.Index));
        sim.Step([new IssueOrderCommand(0, [tank], Order.AttackUnit(enemy), false)]);
        // The target escapes far out of every intel range.
        w.Units.Pos[enemy.Index] = new Vector2(110, 110);
        float maxX = 0;
        for (int t = 0; t < 300 && !w.Units.Orders[tank.Index].IsEmpty; t++)
        {
            sim.Step();
            maxX = MathF.Max(maxX, w.Units.Pos[tank.Index].X);
        }
        Assert.True(w.Units.Orders[tank.Index].IsEmpty, "the attack order should be dropped");
        Assert.True(w.Units.IsActive(enemy));
        Assert.True(maxX < 45f, $"the tank chased the unseen target (max x {maxX})");
        Assert.True(Vector2.Distance(w.Units.Pos[tank.Index], new Vector2(36, 64)) < 8f, "it went to the last known position");
    }

    [Fact]
    public void AttackOrderOnVisibleTargetIsKept()
    {
        var sim = World();
        var w = sim.World;
        var tank = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 20, 64);
        var enemy = CombatWorld.Spawn(sim, "uef_t1_commander", 1, 36, 64);
        sim.Step();
        sim.Step([new IssueOrderCommand(0, [tank], Order.AttackUnit(enemy), false)]);
        for (int t = 0; t < 30; t++) sim.Step();
        Assert.False(w.Units.Orders[tank.Index].IsEmpty);
        Assert.Equal(OrderType.Attack, w.Units.Orders[tank.Index].Current.Type);
    }
}

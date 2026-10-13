using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Systems;

namespace Rts.Sim.Tests;

/// <summary>Idle aircraft engage enemies their weapons acquire and return to their orbit afterwards.</summary>
public class IdleAirTests
{
    private static Vector2 Pos(Simulation sim, UnitHandle h) => sim.World.Units.Pos[h.Index];

    private static float OrbitR(string bp) => MovementSystem.OrbitRadius(TestUtil.Db[bp].Movement);

    [Fact]
    public void IdleInterceptorShootsDownANearbyBomberAndReturnsHome()
    {
        var sim = CombatWorld.Flat(256);
        var home = new Vector2(60, 128);
        var fighter = CombatWorld.Spawn(sim, "uef_t1_interceptor", 0, home.X, home.Y);
        var bomber = CombatWorld.Spawn(sim, "uef_t1_bomber", 1, 80, 128);
        float maxAway = 0;
        int k = 0;
        for (; k < 600 && CombatWorld.Alive(sim, bomber); k++)
        {
            sim.Step();
            maxAway = MathF.Max(maxAway, Vector2.Distance(Pos(sim, fighter), home));
        }
        Assert.False(CombatWorld.Alive(sim, bomber), "idle interceptor should shoot down the bomber");
        Assert.True(maxAway > 2f * OrbitR("uef_t1_interceptor"), $"the interceptor should chase (max {maxAway:F1} from home)");
        TestUtil.Run(sim, 150);
        Assert.False(sim.World.Units.Move[fighter.Index].IdleChase);
        Assert.True(Vector2.Distance(Pos(sim, fighter), home) < 2f * OrbitR("uef_t1_interceptor"),
            $"interceptor should return to its orbit, at {Pos(sim, fighter)}");
    }

    [Fact]
    public void IdleChaseIsLeashedToHome()
    {
        var sim = CombatWorld.Flat(256);
        var home = new Vector2(40, 128);
        var fighter = CombatWorld.Spawn(sim, "uef_t1_interceptor", 0, home.X, home.Y);
        var spy = CombatWorld.Spawn(sim, "uef_t3_spy_plane", 1, 58, 128);
        sim.World.Units.Health[spy.Index] = 1e7f; // keep it alive: this test is about the leash
        sim.Step([new IssueOrderCommand(1, [spy], Order.MoveTo(new Vector2(240, 128)), false)]);
        float leash = IdleAirEngage.Leash(sim.World, sim.World.Units.Bp[fighter.Index], 0);
        float maxAway = 0;
        for (int k = 0; k < 400; k++)
        {
            sim.Step();
            maxAway = MathF.Max(maxAway, Vector2.Distance(Pos(sim, fighter), home));
        }
        Assert.True(maxAway > 2f * OrbitR("uef_t1_interceptor"), $"the interceptor should chase (max {maxAway:F1})");
        Assert.True(maxAway < leash + 40f, $"chased too far: {maxAway:F1} (leash {leash:F1})");
        Assert.True(Vector2.Distance(Pos(sim, fighter), home) < 2f * OrbitR("uef_t1_interceptor"),
            $"interceptor should be back home, at {Pos(sim, fighter)}");
        Assert.True(CombatWorld.Alive(sim, spy));
    }

    [Fact]
    public void IdleBomberBombsAGroundTargetItSees()
    {
        var sim = CombatWorld.Flat(256);
        var home = new Vector2(60, 128);
        CombatWorld.Spawn(sim, "uef_t1_bomber", 0, home.X, home.Y);
        var tank = CombatWorld.Spawn(sim, "uef_t1_tank", 1, home.X + 22, home.Y); // outside the orbit, inside bomb acquire range
        float max = TestUtil.Db["uef_t1_tank"].MaxHealth;
        for (int k = 0; k < 400 && CombatWorld.Health(sim, tank) >= max; k++) sim.Step();
        Assert.True(CombatWorld.Health(sim, tank) < max, "idle bomber should bomb the tank");
    }

    [Fact]
    public void AircraftWithOrdersDoNotIdleChase()
    {
        var sim = CombatWorld.Flat(256);
        var fighter = CombatWorld.Spawn(sim, "uef_t1_interceptor", 0, 60, 128);
        CombatWorld.Spawn(sim, "uef_t3_spy_plane", 1, 70, 140);
        sim.Step([new IssueOrderCommand(0, [fighter], Order.MoveTo(new Vector2(60, 40)), false)]);
        for (int k = 0; k < 40; k++)
        {
            sim.Step();
            Assert.False(sim.World.Units.Move[fighter.Index].IdleChase);
        }
        Assert.True(Pos(sim, fighter).Y < 100, "a plain move order is not interrupted by the idle chase");
    }
}

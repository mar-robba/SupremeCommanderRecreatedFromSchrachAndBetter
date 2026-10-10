using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

public class MovementTests
{
    [Fact]
    public void AirUnitOrbitsGoalWhenIdle()
    {
        var sim = PathTestUtil.World(160, (_, _) => 5f);
        var w = sim.World;
        var plane = PathTestUtil.Spawn(sim, "uef_t1_interceptor", 0, new Vector2(30, 30));
        var dest = new Vector2(110, 100);
        sim.Step([new IssueOrderCommand(0, [plane], Order.MoveTo(dest), false)]);
        int i = plane.Index;
        int t = 0;
        while (!w.Units.Orders[i].IsEmpty && t++ < 300) sim.Step();
        Assert.True(w.Units.Orders[i].IsEmpty, "plane never reached its goal");

        var bp = w.BpOf(i);
        float orbitR = MovementSystem.OrbitRadius(bp.Movement);
        float maxDist = 0, travelled = 0, minSpeed = float.MaxValue;
        var last = w.Units.Pos[i];
        for (int k = 0; k < 300; k++)
        {
            sim.Step();
            var p = w.Units.Pos[i];
            if (k > 50) maxDist = MathF.Max(maxDist, Vector2.Distance(p, dest));
            travelled += Vector2.Distance(p, last);
            last = p;
            minSpeed = MathF.Min(minSpeed, w.Units.Move[i].Speed);
            Assert.True((w.Units.Flags[i] & UnitFlags.Moving) != 0, "idle aircraft must keep flying");
        }
        Assert.True(maxDist < orbitR * 2f, $"orbit drifted: max {maxDist} (orbit radius {orbitR})");
        Assert.True(minSpeed > bp.Movement.MaxSpeed * 0.5f, $"aircraft slowed to {minSpeed}");
        Assert.True(travelled > 300 * SimWorld.Dt * bp.Movement.MaxSpeed * 0.5f, $"travelled only {travelled}");
        float alt = w.Units.Elevation[i] - w.Terrain.GetSurfaceHeight(w.Units.Pos[i]);
        Assert.True(MathF.Abs(alt - bp.Movement.Altitude) < 1f, $"altitude {alt}");
    }

    [Fact]
    public void AirUnitIgnoresTerrainAndStructures()
    {
        // Lake and a wall of structures between start and goal: aircraft fly straight over them.
        var sim = PathTestUtil.World(96, (x, _) => x >= 40 && x <= 56 ? 0f : 5f);
        var w = sim.World;
        for (int z = 0; z < 96; z += 2) PathTestUtil.Spawn(sim, "uef_t1_power_generator", 0, new Vector2(31, z + 1));
        var plane = PathTestUtil.Spawn(sim, "uef_t1_bomber", 0, new Vector2(10, 48));
        sim.Step([new IssueOrderCommand(0, [plane], Order.MoveTo(new Vector2(85, 48)), false)]);
        int t = 0;
        float maxOff = 0;
        while (!w.Units.Orders[plane.Index].IsEmpty && t++ < 200)
        {
            sim.Step();
            maxOff = MathF.Max(maxOff, MathF.Abs(w.Units.Pos[plane.Index].Y - 48));
        }
        Assert.True(w.Units.Orders[plane.Index].IsEmpty, "bomber never arrived");
        Assert.True(maxOff < 3f, $"bomber deviated {maxOff} from the straight line");
    }

    [Fact]
    public void NavalUnitNeverLeavesDeepWater()
    {
        // Deep sea (height -5, water 0) with a land peninsula x 40..56, z 0..70: the ship must sail around it.
        var sim = PathTestUtil.World(96, (x, z) => x >= 40 && x <= 56 && z <= 70 ? 5f : -5f, waterLevel: 0f, db: PathTestUtil.NavalDb);
        var w = sim.World;
        var ship = PathTestUtil.Spawn(sim, "test_frigate", 0, new Vector2(20, 20));
        var dest = new Vector2(76, 20);
        sim.Step([new IssueOrderCommand(0, [ship], Order.MoveTo(dest), false)]);
        int i = ship.Index;
        int t = 0;
        while (!w.Units.Orders[i].IsEmpty && t++ < 1200)
        {
            sim.Step();
            var p = w.Units.Pos[i];
            Assert.True(w.Terrain.CanTraverse(MotionLayer.Water, PathTestUtil.CellX(p), PathTestUtil.CellZ(p)), $"ship left deep water at {p}");
        }
        Assert.True(w.Units.Orders[i].IsEmpty, "ship never arrived");
        Assert.True(Vector2.Distance(w.Units.Pos[i], dest) < 2f, $"ship at {w.Units.Pos[i]}");

        // Ordered onto land: it stops at the nearest deep water instead.
        sim.Step([new IssueOrderCommand(0, [ship], Order.MoveTo(new Vector2(48, 30)), false)]);
        t = 0;
        while (!w.Units.Orders[i].IsEmpty && t++ < 600)
        {
            sim.Step();
            var p = w.Units.Pos[i];
            Assert.True(w.Terrain.CanTraverse(MotionLayer.Water, PathTestUtil.CellX(p), PathTestUtil.CellZ(p)), $"ship left deep water at {p}");
        }
        Assert.True(w.Units.Orders[i].IsEmpty, "ship never gave up the unreachable order");
    }

    [Fact]
    public void FormationGroupMovesAtSlowestMembersPace()
    {
        var sim = PathTestUtil.World(200, (_, _) => 5f);
        var w = sim.World;
        var units = new List<UnitHandle>();
        for (int k = 0; k < 12; k++)
            units.Add(PathTestUtil.Spawn(sim, k % 2 == 0 ? "uef_t1_tank" : "uef_t1_artillery", 0, new Vector2(20 + k % 4 * 2.5f, 90 + k / 4 * 2.5f)));
        sim.Step([new IssueOrderCommand(0, units.ToArray(), Order.MoveTo(new Vector2(170, 95)), false)]);
        Assert.NotEqual(0, w.Units.Move[units[0].Index].GroupId);
        var arrival = new int[units.Count];
        Array.Fill(arrival, -1);
        float maxTankSpeed = 0;
        for (int t = 1; t < 900 && arrival.Any(a => a < 0); t++)
        {
            sim.Step();
            for (int k = 0; k < units.Count; k++)
            {
                int i = units[k].Index;
                if (arrival[k] < 0 && w.Units.Orders[i].IsEmpty) arrival[k] = t;
                if (k % 2 == 0 && t > 20 && t < 300) maxTankSpeed = MathF.Max(maxTankSpeed, w.Units.Move[i].Speed);
            }
        }
        Assert.All(arrival, a => Assert.True(a > 0, "a member never arrived"));
        int spread = arrival.Max() - arrival.Min();
        // Alone, tanks (3.0) would arrive ~60 ticks before the artillery (2.6) over this distance.
        Assert.True(spread < 25, $"arrival spread {spread} ticks");
        Assert.True(maxTankSpeed < 2.6f * 1.1f, $"tank ran ahead at {maxTankSpeed}");
    }

    [Fact]
    public void MovingUnitPushesIdleFriendAside()
    {
        var sim = PathTestUtil.World(64, (_, _) => 5f);
        var w = sim.World;
        var idle = PathTestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(32, 32));
        var mover = PathTestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(16, 32));
        var dest = new Vector2(48, 32);
        sim.Step([new IssueOrderCommand(0, [mover], Order.MoveTo(dest), false)]);
        int t = 0;
        while (!w.Units.Orders[mover.Index].IsEmpty && t++ < 300) sim.Step();
        Assert.True(w.Units.Orders[mover.Index].IsEmpty, "mover blocked by an idle friend");
        Assert.True(Vector2.Distance(w.Units.Pos[mover.Index], dest) < 2f);
        Assert.True(MathF.Abs(w.Units.Pos[idle.Index].Y - 32) > 0.5f, "idle unit should have stepped aside");
    }

    [Fact]
    public void UnitWalksOutOfStructurePlacedOnIt()
    {
        var sim = PathTestUtil.World(64, (_, _) => 5f);
        var w = sim.World;
        var tank = PathTestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(30.5f, 30.5f));
        sim.Step();
        PathTestUtil.Spawn(sim, "uef_t1_land_factory", 0, new Vector2(30, 30)); // 6x6 footprint over the tank
        Assert.True(w.Occupancy.IsBlocked(30, 30));
        for (int t = 0; t < 100; t++) sim.Step();
        var p = w.Units.Pos[tank.Index];
        Assert.False(w.Occupancy.IsBlocked(PathTestUtil.CellX(p), PathTestUtil.CellZ(p)), $"tank still inside the factory at {p}");
    }

    [Fact]
    public void StructuresNeverMove()
    {
        var sim = PathTestUtil.World(64, (_, _) => 5f);
        var w = sim.World;
        var pd = PathTestUtil.Spawn(sim, "uef_t1_point_defense", 0, new Vector2(32, 32));
        var start = w.Units.Pos[pd.Index];
        var tanks = new List<UnitHandle>();
        for (int k = 0; k < 10; k++) tanks.Add(PathTestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(20, 28 + k)));
        sim.Step([new IssueOrderCommand(0, tanks.ToArray(), Order.MoveTo(new Vector2(44, 32)), false)]);
        for (int t = 0; t < 200; t++) sim.Step();
        Assert.Equal(start, w.Units.Pos[pd.Index]);
    }
}

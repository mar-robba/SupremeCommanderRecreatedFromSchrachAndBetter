using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Pathfinding;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

public class PathfindingTests
{
    /// <summary>Runs until the unit's order queue is empty (or maxTicks), asserting a per-tick invariant.</summary>
    private static int RunUntilIdle(Simulation sim, UnitHandle unit, int maxTicks, Action<Vector2>? everyTick = null)
    {
        var u = sim.World.Units;
        for (int t = 0; t < maxTicks; t++)
        {
            sim.Step();
            everyTick?.Invoke(u.Pos[unit.Index]);
            if (u.Orders[unit.Index].IsEmpty && !u.Move[unit.Index].HasGoal) return t;
        }
        return maxTicks;
    }

    [Fact]
    public void LandUnitRoutesAroundLake()
    {
        // 96x96 land at height 5 with a north-south lake in the middle (x 40..56, z 16..80).
        var sim = PathTestUtil.World(96, (x, z) => x >= 40 && x <= 56 && z >= 16 && z <= 80 ? 0f : 5f);
        var w = sim.World;
        var tank = PathTestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(20, 48));
        var dest = new Vector2(76, 48);
        sim.Step([new IssueOrderCommand(0, [tank], Order.MoveTo(dest), false)]);
        Assert.True(w.Units.Move[tank.Index].PathId >= 0, "path assigned on the order tick");
        Assert.True(w.Paths.PathOf(tank.Index).Length >= 2, "route needs at least one corner");
        int ticks = RunUntilIdle(sim, tank, 900, p =>
            Assert.True(w.Terrain.CanTraverse(MotionLayer.Land, PathTestUtil.CellX(p), PathTestUtil.CellZ(p)), $"tank entered the lake at {p}"));
        Assert.True(ticks < 900, "tank never arrived");
        Assert.True(Vector2.Distance(w.Units.Pos[tank.Index], dest) < 2f, $"tank at {w.Units.Pos[tank.Index]}");
    }

    [Fact]
    public void LandUnitRoutesAroundCliff()
    {
        // A tall ridge (height 20) from z=0 to z=70 at x 40..43: its faces (cells 39 and 43) are cliffs, so it
        // cannot be crossed; the way around is the gap at the north.
        var sim = PathTestUtil.World(96, (x, z) => x >= 40 && x <= 43 && z <= 70 ? 20f : 5f);
        var w = sim.World;
        Assert.False(w.Terrain.CanTraverse(MotionLayer.Land, 39, 30));
        Assert.False(w.Terrain.CanTraverse(MotionLayer.Land, 43, 30));
        var tank = PathTestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(20, 30));
        var dest = new Vector2(70, 30);
        sim.Step([new IssueOrderCommand(0, [tank], Order.MoveTo(dest), false)]);
        int ticks = RunUntilIdle(sim, tank, 900, p =>
            Assert.True(w.Terrain.CanTraverse(MotionLayer.Land, PathTestUtil.CellX(p), PathTestUtil.CellZ(p)), $"tank on the cliff at {p}"));
        Assert.True(ticks < 900, "tank never arrived");
        Assert.True(Vector2.Distance(w.Units.Pos[tank.Index], dest) < 2f, $"tank at {w.Units.Pos[tank.Index]}");
    }

    private static void StampWall(Simulation sim, int x, int z0, int z1)
    {
        for (int z = z0; z <= z1; z += 2)
            Assert.False(PathTestUtil.Spawn(sim, "uef_t1_power_generator", 0, new Vector2(x + 1, z + 1)).IsNone);
    }

    [Fact]
    public void LandUnitRoutesAroundStructures()
    {
        var sim = PathTestUtil.World(64, (_, _) => 5f);
        var w = sim.World;
        StampWall(sim, 30, 0, 48); // wall of 2x2 structures along x 30..31, z 0..49
        var tank = PathTestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(15, 25));
        var dest = new Vector2(45, 25);
        sim.Step([new IssueOrderCommand(0, [tank], Order.MoveTo(dest), false)]);
        int ticks = RunUntilIdle(sim, tank, 900, p =>
            Assert.False(w.Occupancy.IsBlocked(PathTestUtil.CellX(p), PathTestUtil.CellZ(p)), $"tank inside a structure at {p}"));
        Assert.True(ticks < 900, "tank never arrived");
        Assert.True(Vector2.Distance(w.Units.Pos[tank.Index], dest) < 2f, $"tank at {w.Units.Pos[tank.Index]}");
    }

    [Fact]
    public void UnitRepathsWhenStructuresBlockItsRoute()
    {
        var sim = PathTestUtil.World(64, (_, _) => 5f);
        var w = sim.World;
        var tank = PathTestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(10, 25));
        var dest = new Vector2(50, 25);
        sim.Step([new IssueOrderCommand(0, [tank], Order.MoveTo(dest), false)]);
        for (int t = 0; t < 10; t++) sim.Step();
        Assert.Equal(1, w.Paths.PathOf(tank.Index).Length); // straight line while the field is open
        StampWall(sim, 34, 0, 48);
        int ticks = RunUntilIdle(sim, tank, 900, p =>
            Assert.False(w.Occupancy.IsBlocked(PathTestUtil.CellX(p), PathTestUtil.CellZ(p)), $"tank inside a structure at {p}"));
        Assert.True(ticks < 900, "tank never arrived");
        Assert.True(Vector2.Distance(w.Units.Pos[tank.Index], dest) < 2f, $"tank at {w.Units.Pos[tank.Index]}");
    }

    [Fact]
    public void UnreachableGoalStopsAtNearestReachablePoint()
    {
        // East half of the map is a lake; the goal is in the middle of it.
        var sim = PathTestUtil.World(64, (x, _) => x >= 32 ? 0f : 5f);
        var w = sim.World;
        var tank = PathTestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(10, 32));
        var dest = new Vector2(50, 32);
        sim.Step([new IssueOrderCommand(0, [tank], Order.MoveTo(dest), false)]);
        int ticks = RunUntilIdle(sim, tank, 600, p =>
            Assert.True(w.Terrain.CanTraverse(MotionLayer.Land, PathTestUtil.CellX(p), PathTestUtil.CellZ(p)), $"tank in the water at {p}"));
        Assert.True(ticks < 600, "move order never completed");
        var pos = w.Units.Pos[tank.Index];
        // Last land column is x = 30 (cell 31 touches the lake vertices at x = 32).
        Assert.True(pos.X > 28.5f && pos.X < 31f && MathF.Abs(pos.Y - 32) < 2.5f, $"tank stopped at {pos}");
        for (int t = 0; t < 30; t++) sim.Step();
        Assert.True(Vector2.Distance(w.Units.Pos[tank.Index], pos) < 0.5f, "tank keeps moving after giving up");
    }

    [Fact]
    public void UnitOnIslandStopsAtShore()
    {
        // Island (x,z in 20..40) surrounded by water; goal on the far mainland strip (x > 56).
        var sim = PathTestUtil.World(64, (x, z) => (x >= 20 && x <= 40 && z >= 20 && z <= 40) || x >= 56 ? 5f : 0f);
        var w = sim.World;
        var tank = PathTestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(30, 30));
        Assert.False(w.Paths.CanReach(MotionLayer.Land, 0.6f, new Vector2(30, 30), new Vector2(60, 30)));
        sim.Step([new IssueOrderCommand(0, [tank], Order.MoveTo(new Vector2(60, 30)), false)]);
        int ticks = RunUntilIdle(sim, tank, 400);
        Assert.True(ticks < 400, "move order never completed");
        var pos = w.Units.Pos[tank.Index];
        Assert.True(pos.X > 37f && pos.X < 40f, $"tank should wait on the east shore of the island, at {pos}");
    }

    [Fact]
    public void IncrementalNavUpdateMatchesFullRebuild()
    {
        var sim = TestUtil.NewSkirmish(seed: 5, mapSize: 256);
        var w = sim.World;
        sim.Step();
        var graph = w.Paths.GraphFor(MotionLayer.Land, 1)!;
        var graph2 = w.Paths.GraphFor(MotionLayer.Land, 2)!;
        // Place and remove structures in a few spots, then compare against grids/graphs built from scratch.
        var rng = new DetRandom(9);
        var spawned = new List<UnitHandle>();
        for (int k = 0; k < 40; k++)
        {
            var p = new Vector2(rng.Range(10f, 246f), rng.Range(10f, 246f));
            var h = TestUtil.Spawn(sim, k % 2 == 0 ? "uef_t1_land_factory" : "uef_t1_power_generator", 0, p);
            spawned.Add(h);
        }
        sim.Step();
        for (int k = 0; k < spawned.Count; k += 3) w.KillUnit(spawned[k].Index, UnitHandle.None);
        sim.Step(); // cleanup frees the structures
        sim.Step(); // nav update sees the cleared cells

        foreach (var layer in new[] { MotionLayer.Land, MotionLayer.Hover, MotionLayer.Amphibious, MotionLayer.Water })
        {
            var fresh = new NavGrid(w.Terrain, w.Occupancy, layer);
            Assert.Equal(fresh.Clear, w.Paths.GridFor(layer)!.Clear);
        }
        foreach (var g in new[] { graph, graph2 })
        {
            var fresh = new NavGraph(g.Grid, g.Req);
            Assert.Equal(fresh.RegionCount, g.RegionCount);
            Assert.Equal(fresh.Local, g.Local);
            Assert.Equal(fresh.RegComp.AsSpan(0, fresh.RegionCount).ToArray(), g.RegComp.AsSpan(0, g.RegionCount).ToArray());
            Assert.Equal(fresh.EdgeStart.AsSpan(0, fresh.RegionCount + 1).ToArray(), g.EdgeStart.AsSpan(0, g.RegionCount + 1).ToArray());
        }
    }

    [Fact]
    public void ClearanceKeepsLargeUnitsOutOfNarrowGaps()
    {
        // Wall with a 1-cell gap at z = 30: small units (clearance 1) pass, clearance-2 units must not.
        var sim = PathTestUtil.World(64, (_, _) => 5f);
        var w = sim.World;
        StampWall(sim, 30, 0, 28);  // z 0..29
        StampWall(sim, 30, 31, 61); // z 31..62 -> gap at z = 30 (and z = 63 at the map edge is the border)
        sim.Step();
        Assert.True(w.Paths.CanReach(MotionLayer.Land, 0.6f, new Vector2(10, 30), new Vector2(50, 30)));
        Assert.False(w.Paths.CanReach(MotionLayer.Land, 1.2f, new Vector2(10, 30), new Vector2(50, 30)));
    }

    [Fact]
    public void BigSimultaneousOrderIsSpreadOverTicksAndShared()
    {
        var sim = TestUtil.NewSkirmish(seed: 11, mapSize: 256);
        var w = sim.World;
        var units = new List<UnitHandle>();
        for (int k = 0; k < 600; k++)
            units.Add(TestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(40 + k % 25 * 1.6f, 40 + k / 25 * 1.6f)));
        sim.Step([new IssueOrderCommand(0, units.ToArray(), Order.MoveTo(new Vector2(200, 200)), false)]);
        int maxSolves = w.Paths.Stats.LastSolves;
        for (int t = 0; t < 15; t++)
        {
            sim.Step();
            maxSolves = Math.Max(maxSolves, w.Paths.Stats.LastSolves);
        }
        Assert.True(maxSolves <= w.Paths.MaxSolvesPerTick);
        int withPath = units.Count(h => w.Units.Move[h.Index].PathId >= 0 || !w.Units.Move[h.Index].HasGoal);
        Assert.Equal(units.Count, withPath);
        // Formation members share leader paths: far fewer A* solves than units.
        Assert.True(w.Paths.Stats.TotalSolves < units.Count / 3, $"solves={w.Paths.Stats.TotalSolves}");
    }

    [Fact]
    public void PathingIsDeterministicAcrossThreadCountsWithStructureChanges()
    {
        ulong RunWith(int threads)
        {
            int old = Par.MaxThreads;
            Par.MaxThreads = threads;
            try
            {
                var sim = PathTestUtil.World(128, (x, z) => x >= 50 && x <= 70 && z >= 20 && z <= 100 ? 0f : 5f, seed: 3);
                var w = sim.World;
                var a = new List<UnitHandle>();
                var b = new List<UnitHandle>();
                for (int k = 0; k < 200; k++)
                {
                    a.Add(PathTestUtil.Spawn(sim, k % 3 == 0 ? "uef_t1_artillery" : "uef_t1_tank", 0, new Vector2(10 + k % 10 * 1.5f, 40 + k / 10 * 1.5f)));
                    b.Add(PathTestUtil.Spawn(sim, k % 4 == 0 ? "uef_commander" : "uef_t1_mobile_aa", 1, new Vector2(118 - k % 10 * 1.5f, 40 + k / 10 * 1.5f)));
                }
                sim.Step([
                    new IssueOrderCommand(0, a.ToArray(), Order.MoveTo(new Vector2(110, 60)), false),
                    new IssueOrderCommand(1, b.ToArray(), Order.MoveTo(new Vector2(15, 60)), false),
                ]);
                for (int t = 0; t < 300; t++)
                {
                    if (t == 40) StampWall(sim, 30, 30, 90);
                    sim.Step();
                }
                return sim.StateHash();
            }
            finally { Par.MaxThreads = old; }
        }

        Assert.Equal(RunWith(1), RunWith(8));
    }
}

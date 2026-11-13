using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Pathfinding;
using Rts.Sim.Persistence;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>(11a-1) Exact clearance (a unit's real radius in the path search and the local movement), per-unit slope
/// classes, and the nav-format reset of old saves. ARCHITECTURE "Pathing: clearance, slope classes, channels".</summary>
public class PathClearanceTests
{
    /// <summary>Flat land at 5 with a cliff ridge (x vertices 40..43, every z) and a gap of `gap` free cells
    /// centered on z = 48 (0 = no gap).</summary>
    private static Simulation RidgeWorld(int gap, BlueprintDb? db = null)
    {
        int g0 = 48 - gap / 2;
        return PathTestUtil.World(96, (x, z) => x >= 40 && x <= 43 && !(gap > 0 && z >= g0 && z <= g0 + gap) ? 20f : 5f, db: db);
    }

    private static void Order(Simulation sim, UnitHandle[] units, Vector2 dest) =>
        sim.Step([new IssueOrderCommand(0, units, Rts.Sim.Orders.Order.MoveTo(dest), false)]);

    private static bool Fits(SimWorld w, UnitHandle h) => w.Paths.Fits(w.BpOf(h.Index), w.Units.Pos[h.Index]);

    [Fact]
    public void ClearanceRequirementMatchesRadius()
    {
        Assert.Equal(1, NavGrid.ClearanceFor(0.5f)); // any free cell, as before
        Assert.Equal(1, NavGrid.ClearanceFor(0.6f));
        Assert.Equal(2, NavGrid.ClearanceFor(0.8f)); // the 4 edge neighbours free
        Assert.Equal(9, NavGrid.ClearanceFor(1.2f)); // the 3x3 block free (the ACU)
        Assert.Equal(34, NavGrid.ClearanceFor(3.0f)); // Fatboy, battleships: the cap
        int prev = 0;
        for (float r = 0.5f; r <= 3.0f; r += 0.05f)
        {
            int c = NavGrid.ClearanceFor(r);
            Assert.True(c >= prev, $"not monotonic at r {r}");
            prev = c;
        }
        // Every mobile surface unit is below the cap (a radius > 3.0 needs MaxClearance raised: DEV_GUIDE).
        foreach (var bp in TestUtil.Db.All)
            if (bp.IsMobile && bp.Movement.Layer != MotionLayer.Air)
                Assert.True(NavGrid.ClearanceFor(bp.Radius) <= NavGrid.MaxClearance, $"{bp.Id} radius {bp.Radius} above the clearance cap");
    }

    [Theory]
    [InlineData("uef_x_fatboy", 5, false)] // radius 3.0: a 5-cell gap is narrower than the unit
    [InlineData("uef_x_fatboy", 7, true)]
    [InlineData("uef_t1_tank", 2, true)] // radius 0.6
    public void UnitTakesOnlyGapsItFits(string unit, int gap, bool passes)
    {
        var sim = RidgeWorld(gap);
        var w = sim.World;
        var h = PathTestUtil.Spawn(sim, unit, 0, new Vector2(20.5f, 48.5f));
        var dest = new Vector2(70.5f, 48.5f);
        Order(sim, [h], dest);
        float maxX = 0;
        for (int t = 0; t < 1500 && (w.Units.Move[h.Index].HasGoal || !w.Units.Orders[h.Index].IsEmpty); t++)
        {
            sim.Step();
            var p = w.Units.Pos[h.Index];
            maxX = MathF.Max(maxX, p.X);
            Assert.True(Fits(w, h), $"{unit} at {p} (tick {t}) stands where it does not fit");
        }
        if (passes) Assert.True(Vector2.Distance(w.Units.Pos[h.Index], dest) < 3f, $"{unit} did not pass the {gap}-cell gap: at {w.Units.Pos[h.Index]}");
        else Assert.True(maxX < 39f, $"{unit} squeezed into the {gap}-cell gap: max x {maxX}");
    }

    [Fact]
    public void WideUnitsAreNotPushedIntoCliffsOrShores()
    {
        // Fatboy idle 4 cells from a cliff (the closest it fits), 16 tanks driven through it to the cliff foot.
        var sim = RidgeWorld(0);
        var w = sim.World;
        var fat = PathTestUtil.Spawn(sim, "uef_x_fatboy", 0, new Vector2(35.5f, 48.5f));
        var tanks = new List<UnitHandle>();
        for (int k = 0; k < 16; k++) tanks.Add(PathTestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(22 + k % 4 * 1.5f, 45 + k / 4 * 1.5f)));
        sim.Step(); // grids are built on the first tick
        Assert.True(Fits(w, fat), "Fatboy start cell");
        Order(sim, tanks.ToArray(), new Vector2(37.5f, 48.5f));
        for (int t = 0; t < 400; t++)
        {
            sim.Step();
            Assert.True(Fits(w, fat), $"Fatboy pushed to {w.Units.Pos[fat.Index]} (tick {t})");
        }

        // A battleship idle 4 cells off a shore, frigates driven through it to the shore.
        var sea = PathTestUtil.World(96, (x, z) => x <= 39 ? 0f : 5f, db: PathTestUtil.NavalDb);
        var sw = sea.World;
        var bs = PathTestUtil.Spawn(sea, "uef_t3_battleship", 0, new Vector2(35.5f, 48.5f));
        sea.Step();
        Assert.True(Fits(sw, bs), "battleship start cell");
        var frigates = new List<UnitHandle>();
        for (int k = 0; k < 8; k++) frigates.Add(PathTestUtil.Spawn(sea, "test_frigate", 0, new Vector2(18 + k % 4 * 2.5f, 44 + k / 4 * 2.5f)));
        Order(sea, frigates.ToArray(), new Vector2(37.5f, 48.5f));
        for (int t = 0; t < 400; t++)
        {
            sea.Step();
            Assert.True(Fits(sw, bs), $"battleship pushed to {sw.Units.Pos[bs.Index]} (tick {t})");
        }
    }

    /// <summary>A ridge of 1.0 slopes (z &lt;= 80) between x 44 and 52; the way around is at the north.</summary>
    private static Simulation SlopeWorld() =>
        PathTestUtil.World(96, (x, z) => z <= 80 ? 5f + MathF.Max(0, 4 - MathF.Abs(x - 48)) : 5f);

    [Fact]
    public void WalkerCrossesSlopeThatTankGoesAround()
    {
        var sim = SlopeWorld();
        var w = sim.World;
        var bot = PathTestUtil.Spawn(sim, "uef_t1_mech_marine", 0, new Vector2(20.5f, 30.5f));
        var tank = PathTestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(20.5f, 32.5f));
        Assert.True(w.BpOf(bot.Index).Movement.MaxSlope > 1.0f && w.BpOf(tank.Index).Movement.MaxSlope < 1.0f);
        Assert.False(w.Terrain.CanTraverse(MotionLayer.Land, 46, 30)); // too steep at the default slope
        var botDest = new Vector2(76.5f, 30.5f);
        var tankDest = new Vector2(76.5f, 32.5f);
        sim.Step([new IssueOrderCommand(0, [bot], Rts.Sim.Orders.Order.MoveTo(botDest), false),
                  new IssueOrderCommand(0, [tank], Rts.Sim.Orders.Order.MoveTo(tankDest), false)]);
        float botMaxZ = 0, tankMaxZ = 0;
        for (int t = 0; t < 2500; t++)
        {
            sim.Step();
            var bp = w.Units.Pos[bot.Index];
            var tp = w.Units.Pos[tank.Index];
            botMaxZ = MathF.Max(botMaxZ, bp.Y);
            tankMaxZ = MathF.Max(tankMaxZ, tp.Y);
            Assert.True(w.Terrain.CanTraverse(MotionLayer.Land, PathTestUtil.CellX(tp), PathTestUtil.CellZ(tp)), $"tank on a steep cell at {tp}");
            Assert.True(NavGrid.Traversable(w.Terrain, MotionLayer.Land, 1.2f, PathTestUtil.CellX(bp), PathTestUtil.CellZ(bp)), $"bot on a cliff at {bp}");
            if (Vector2.Distance(bp, botDest) < 2f && Vector2.Distance(tp, tankDest) < 2f) break;
        }
        Assert.True(Vector2.Distance(w.Units.Pos[bot.Index], botDest) < 2f, $"bot at {w.Units.Pos[bot.Index]}");
        Assert.True(Vector2.Distance(w.Units.Pos[tank.Index], tankDest) < 2f, $"tank at {w.Units.Pos[tank.Index]}");
        Assert.True(botMaxZ < 40f, $"bot went around (max z {botMaxZ})");
        Assert.True(tankMaxZ > 78f, $"tank crossed the slope (max z {tankMaxZ})");
    }

    [Fact]
    public void SlopeClassesComeFromBlueprints()
    {
        var classes = PathService.BuildSlopeClasses(TestUtil.Db);
        Assert.Equal(Terrain.DefaultMaxSlope, classes[0]); // nothing stricter than the generator's guarantee
        Assert.InRange(classes.Length, 1, PathService.SlopeSlots);
        var sim = SlopeWorld();
        var paths = sim.World.Paths;
        var db = sim.World.Blueprints;
        Assert.Equal(0, paths.SlopeClassOf(db["uef_t1_tank"]));
        Assert.True(paths.SlopeClassOf(db["uef_t1_mech_marine"]) > 0);
    }

    [Fact]
    public void OldNavFormatIsRebuiltAfterLoad()
    {
        // Same game twice; in b the nav state is marked as an old format at tick 30 and AfterLoad drops it.
        var a = SlopeWorld();
        var b = SlopeWorld();
        var dest = new Vector2(76.5f, 30.5f);
        UnitHandle ha = default, hb = default;
        foreach (var (sim, set) in new[] { (a, 0), (b, 1) })
        {
            var h = PathTestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(20.5f, 30.5f));
            Order(sim, [h], dest);
            if (set == 0) ha = h; else hb = h;
        }
        TestUtil.Run(a, 30);
        TestUtil.Run(b, 30);
        b.World.Paths.NavFormat = 0;
        b.World.Paths.AfterLoad();
        Assert.Equal(PathService.CurrentNavFormat, b.World.Paths.NavFormat);
        TestUtil.Run(a, 30);
        TestUtil.Run(b, 30);
        var bp = a.World.BpOf(ha.Index);
        Assert.Equal(a.World.Paths.GridFor(bp)!.Clear, b.World.Paths.GridFor(bp)!.Clear); // rebuilt grid = fresh one
        TestUtil.Run(b, 1500);
        Assert.True(Vector2.Distance(b.World.Units.Pos[hb.Index], dest) < 2f, $"tank after the reset at {b.World.Units.Pos[hb.Index]}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void SaveLoadContinueKeepsWideAndSlopeUnitsInSync(int threads)
    {
        PersistenceTestUtil.WithThreads(threads, () =>
        {
            var a = SlopeWorld();
            var units = new[]
            {
                PathTestUtil.Spawn(a, "uef_x_fatboy", 0, new Vector2(20.5f, 20.5f)),
                PathTestUtil.Spawn(a, "uef_t1_mech_marine", 0, new Vector2(20.5f, 30.5f)),
                PathTestUtil.Spawn(a, "uef_t1_tank", 0, new Vector2(20.5f, 34.5f)),
                PathTestUtil.Spawn(a, "uef_commander", 0, new Vector2(20.5f, 40.5f)),
            };
            Order(a, units, new Vector2(76.5f, 30.5f));
            TestUtil.Run(a, 60);
            var b = WorldState.Restore(WorldState.Capture(a), a.World.Blueprints, a.World.Terrain);
            for (int t = 0; t < 4; t++)
            {
                TestUtil.Run(a, 100);
                TestUtil.Run(b, 100);
                Assert.True(a.StateHash() == b.StateHash(), $"StateHash differs {100 * (t + 1)} ticks after the load");
            }
            return 0;
        });
    }
}

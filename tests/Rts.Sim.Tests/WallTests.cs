using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>T1 wall sections (Combat/Walls): they block paths, absorb direct shots and beams, and are never auto-targeted.</summary>
public class WallTests
{
    private const string Wall = "uef_t1_wall";

    private static UnitHandle WallAt(Simulation sim, int army, int cx, int cz, string bp = Wall) =>
        CombatWorld.Spawn(sim, bp, army, cx + 0.5f, cz + 0.5f);

    private static List<UnitHandle> WallLine(Simulation sim, int army, int x0, int z0, int dx, int dz, int n)
    {
        var r = new List<UnitHandle>();
        for (int k = 0; k < n; k++) r.Add(WallAt(sim, army, x0 + dx * k, z0 + dz * k));
        return r;
    }

    [Theory]
    [InlineData(Wall)]
    [InlineData("cybran_t1_wall")]
    public void WallBlueprintsAreOneCellStructures(string id)
    {
        var bp = TestUtil.Db[id];
        Assert.True(bp.IsStructure && Walls.IsWall(bp));
        Assert.Equal(1, bp.FootprintX);
        Assert.Equal(1, bp.FootprintZ);
        Assert.Empty(bp.Weapons);
    }

    /// <summary>A tank boxed in by a square (straight lines) or a diamond (diagonal lines, corner-touching cells) of
    /// walls cannot get out: A* never cuts a corner and the movement never slips through one.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnclosureOfWallsKeepsATankIn(bool diamond)
    {
        var sim = CombatWorld.Flat();
        var w = sim.World;
        const int c = 64, r = 5;
        for (int x = c - r; x <= c + r; x++)
        for (int z = c - r; z <= c + r; z++)
        {
            int d = diamond ? Math.Abs(x - c) + Math.Abs(z - c) : Math.Max(Math.Abs(x - c), Math.Abs(z - c));
            if (d == r) WallAt(sim, 0, x, z);
        }
        var tanks = new[] { CombatWorld.Spawn(sim, "uef_t1_tank", 0, c + 0.5f, c + 0.5f), CombatWorld.Spawn(sim, "uef_t1_tank", 0, c + 2.5f, c + 0.5f) };
        sim.Step();
        TestUtil.Run(sim, 400, new IssueOrderCommand(0, tanks, Order.MoveTo(new Vector2(110, 90)), false));
        foreach (var t in tanks)
        {
            var p = w.Units.Pos[t.Index];
            float dx = MathF.Abs(MathF.Floor(p.X) - c), dz = MathF.Abs(MathF.Floor(p.Y) - c);
            float d = diamond ? dx + dz : MathF.Max(dx, dz);
            Assert.True(d < r, $"a tank got out of the {(diamond ? "diamond" : "square")} at {p}");
        }
    }

    [Fact]
    public void TanksPathAroundAWallLine()
    {
        var sim = CombatWorld.Flat();
        var w = sim.World;
        WallLine(sim, 0, 64, 44, 0, 1, 40); // x = 64, z 44..83
        var tank = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 56, 64);
        sim.Step();
        bool crossedAtTheEnd = false;
        TestUtil.Run(sim, 1, new IssueOrderCommand(0, [tank], Order.MoveTo(new Vector2(72, 64)), false));
        for (int t = 0; t < 900; t++)
        {
            var p0 = w.Units.Pos[tank.Index];
            sim.Step();
            var p1 = w.Units.Pos[tank.Index];
            if ((p0.X < 64.5f) != (p1.X < 64.5f)) crossedAtTheEnd |= p1.Y < 44f || p1.Y > 84f;
            Assert.False(MathF.Floor(p1.X) == 64 && p1.Y >= 44f && p1.Y < 84f, $"the tank is inside the wall line at {p1}");
            if (Vector2.Distance(p1, new Vector2(72, 64)) < 2f) break;
        }
        Assert.True(crossedAtTheEnd, "the tank should go around the end of the line");
        Assert.True(Vector2.Distance(w.Units.Pos[tank.Index], new Vector2(72, 64)) < 2f, $"the tank should arrive, at {w.Units.Pos[tank.Index]}");
    }

    /// <summary>A tank shooting an enemy behind an enemy wall hits the wall; a friendly wall lets the shots through.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void WallAbsorbsDirectShots(int wallArmy)
    {
        var sim = CombatWorld.Flat();
        var w = sim.World;
        var walls = WallLine(sim, wallArmy, 55, 59, 0, 1, 11); // x = 55, z 59..69
        var shooter = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 50.5f, 64.5f);
        var target = CombatWorld.Spawn(sim, "uef_t1_tank", 1, 60.5f, 64.5f);
        TestUtil.Run(sim, 1, new SetFireStateCommand(1, [target], FireState.HoldFire));
        float full = CombatWorld.Health(sim, target);
        TestUtil.Run(sim, 200);
        float wallDamage = walls.Sum(h => w.BpOf(h.Index).MaxHealth - CombatWorld.Health(sim, h));
        if (wallArmy == 1)
        {
            Assert.Equal(target, CombatWorld.Weapon(sim, shooter).Target); // aims at the tank, never at the wall
            Assert.Equal(full, CombatWorld.Health(sim, target));
            Assert.True(wallDamage > 0f, "the enemy wall should take the shots");
        }
        else
        {
            Assert.True(CombatWorld.Health(sim, target) < full, "shots pass through a friendly wall");
            Assert.Equal(0f, wallDamage);
        }
    }

    /// <summary>Shots crossing a diagonal wall line exactly at a corner between two cells still hit a wall (boxes, not spheres).</summary>
    [Fact]
    public void DiagonalWallLineHasNoGaps()
    {
        var sim = CombatWorld.Flat();
        var w = sim.World;
        var walls = WallLine(sim, 1, 51, 60, 1, 1, 10); // cells (51+k, 60+k): corner (56, 65) between (55,64) and (56,65)
        var shooter = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 51f, 70f);
        var target = CombatWorld.Spawn(sim, "uef_t1_tank", 1, 61f, 60f);
        TestUtil.Run(sim, 1, new SetFireStateCommand(1, [target], FireState.HoldFire));
        float full = CombatWorld.Health(sim, target);
        TestUtil.Run(sim, 200);
        Assert.Equal(target, CombatWorld.Weapon(sim, shooter).Target);
        Assert.Equal(full, CombatWorld.Health(sim, target));
        Assert.True(walls.Sum(h => w.BpOf(h.Index).MaxHealth - CombatWorld.Health(sim, h)) > 0f);
    }

    [Fact]
    public void ArtilleryShellsFlyOverWalls()
    {
        var sim = CombatWorld.Flat();
        var w = sim.World;
        var walls = WallLine(sim, 1, 60, 54, 0, 1, 21);
        var spotter = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 64.5f, 50.5f); // the target is beyond the artillery's vision
        var arty = CombatWorld.Spawn(sim, "uef_t1_artillery", 0, 46.5f, 64.5f);
        var target = CombatWorld.Spawn(sim, "uef_t1_mass_storage", 1, 70.5f, 64.5f);
        sim.Step([new SetFireStateCommand(0, [spotter], FireState.HoldFire)]);
        float full = CombatWorld.Health(sim, target);
        for (int t = 0; t < 1500 && CombatWorld.Health(sim, target) >= full; t++) sim.Step(); // long, high arcs
        Assert.True(CombatWorld.Health(sim, target) < full, "shells arc over the wall");
        Assert.All(walls, h => Assert.Equal(w.BpOf(h.Index).MaxHealth, CombatWorld.Health(sim, h)));
    }

    /// <summary>The Monkeylord's beam (instant, MuzzleVelocity 0) stops at an enemy wall in front of its target.</summary>
    [Fact]
    public void BeamsStopAtWalls()
    {
        var sim = CombatWorld.Flat();
        var w = sim.World;
        var walls = WallLine(sim, 1, 58, 58, 0, 1, 13);
        var ml = CombatWorld.Spawn(sim, "cybran_x_monkeylord", 0, 40.5f, 64.5f);
        var target = CombatWorld.Spawn(sim, "uef_t1_tank", 1, 60.5f, 64.5f);
        TestUtil.Run(sim, 1, new SetFireStateCommand(1, [target], FireState.HoldFire));
        float full = CombatWorld.Health(sim, target);
        TestUtil.Run(sim, 20); // the beam (300) would kill the tank (305 HP) in two shots; the wall lasts 3 s
        Assert.True(CombatWorld.Alive(sim, target));
        Assert.Equal(full, CombatWorld.Health(sim, target));
        Assert.True(walls.Sum(h => w.BpOf(h.Index).MaxHealth - CombatWorld.Health(sim, h)) > 0f, "the beam should burn the wall");
    }

    [Fact]
    public void WallsAreNeverAutoTargetedButAnAttackOrderWorks()
    {
        var sim = CombatWorld.Flat();
        var w = sim.World;
        var wall = WallAt(sim, 1, 60, 64);
        var tank = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 52.5f, 64.5f);
        TestUtil.Run(sim, 100);
        Assert.True(CombatWorld.Weapon(sim, tank).Target.IsNone);
        Assert.Equal(w.BpOf(wall.Index).MaxHealth, CombatWorld.Health(sim, wall));
        TestUtil.Run(sim, 150, new IssueOrderCommand(0, [tank], Order.AttackUnit(wall), false));
        Assert.True(CombatWorld.Health(sim, wall) < w.BpOf(wall.Index).MaxHealth, "a forced attack should hit the wall");
    }
}

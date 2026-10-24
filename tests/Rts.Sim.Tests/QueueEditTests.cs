using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Transport;

namespace Rts.Sim.Tests;

/// <summary>(7b) Waypoint move/delete and line formation commands (Orders/QueueEditCommands.cs).</summary>
public class QueueEditTests
{
    private static readonly Vector2 A = new(100, 60), B = new(100, 100), C = new(60, 100);

    private static (Simulation sim, UnitHandle[] units) Group(int n = 3)
    {
        var sim = TestUtil.NewSkirmish(seed: 3);
        var units = new UnitHandle[n];
        for (int k = 0; k < n; k++) units[k] = TestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(60 + k * 2.5f, 60));
        sim.Step([
            new IssueOrderCommand(0, units, Order.MoveTo(A), false),
            new IssueOrderCommand(0, units, Order.MoveTo(B), true),
            new IssueOrderCommand(0, units, Order.MoveTo(C), true),
        ]);
        return (sim, units);
    }

    private static OrderQueue Q(Simulation sim, UnitHandle h) => sim.World.Units.Orders[h.Index];

    [Fact]
    public void MovingAMiddleWaypointMovesItForTheWholeGroupKeepingOffsets()
    {
        var (sim, units) = Group();
        var offsets = units.Select(h => Q(sim, h).At(1).FormationOffset).ToArray();
        var to = new Vector2(120, 110);
        sim.Step([new MoveWaypointCommand(0, units, B, to)]);
        for (int k = 0; k < units.Length; k++)
        {
            var q = Q(sim, units[k]);
            Assert.Equal(3, q.Count);
            Assert.Equal(A, q.At(0).Pos);
            Assert.Equal(to, q.At(1).Pos);
            Assert.Equal(offsets[k], q.At(1).FormationOffset);
            Assert.Equal(C, q.At(2).Pos);
        }
    }

    [Fact]
    public void MovingTheFrontWaypointSendsTheUnitThere()
    {
        var sim = TestUtil.NewSkirmish(seed: 3);
        var t = TestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(60, 60));
        TestUtil.Run(sim, 20, new IssueOrderCommand(0, [t], Order.MoveTo(A), false));
        var to = new Vector2(60, 100);
        TestUtil.Run(sim, 400, new MoveWaypointCommand(0, [t], A, to));
        Assert.True(Vector2.Distance(sim.World.Units.Pos[t.Index], to) < 2f, $"at {sim.World.Units.Pos[t.Index]}");
        Assert.True(Q(sim, t).IsEmpty);
    }

    [Fact]
    public void DeletingMiddleAndFrontWaypoints()
    {
        var (sim, units) = Group(1);
        var t = units[0];
        sim.Step([new DeleteWaypointCommand(0, units, B)]);
        Assert.Equal(2, Q(sim, t).Count);
        Assert.Equal(C, Q(sim, t).At(1).Pos);
        // Removing the front order behaves like finishing it: no stale goal, the next order is executed.
        QueueEditSystem.Apply(sim.World, new DeleteWaypointCommand(0, units, A));
        Assert.False(sim.World.Units.Move[t.Index].HasGoal);
        Assert.Equal(C, Q(sim, t).Current.Pos);
        TestUtil.Run(sim, 500);
        Assert.True(Vector2.Distance(sim.World.Units.Pos[t.Index], C) < 2f, $"at {sim.World.Units.Pos[t.Index]}");
    }

    [Fact]
    public void PatrolChainFollowsAMovedPoint()
    {
        var sim = TestUtil.NewSkirmish(seed: 3);
        var t = TestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(60, 60));
        sim.Step([new IssueOrderCommand(0, [t], Order.PatrolTo(B), false)]);
        var to = new Vector2(110, 90);
        sim.Step([new MoveWaypointCommand(0, [t], B, to)]);
        var q = Q(sim, t);
        Assert.Equal(2, q.Count);
        Assert.Equal(to, q.At(0).Pos);
        Assert.Equal(to, q.At(1).Aux); // the leg back starts at the moved point
        sim.Step([new DeleteWaypointCommand(0, [t], to)]);
        Assert.Equal(1, q.Count);
        Assert.Equal(OrderType.Patrol, q.Current.Type);
    }

    [Fact]
    public void FerryBeaconAndDropCanBeMovedAndDeleted()
    {
        var sim = TestUtil.NewSkirmish(seed: 3);
        var w = sim.World;
        var t = TestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(60, 60));
        var q = Q(sim, t);
        q.Enqueue(TransportOrders.Ferry(A, B));
        QueueEditSystem.Apply(w, new MoveWaypointCommand(0, [t], B, C)); // drop point
        Assert.Equal(A, q.Current.Pos);
        Assert.Equal(C, q.Current.Aux);
        var beacon = new Vector2(80, 70);
        QueueEditSystem.Apply(w, new MoveWaypointCommand(0, [t], A, beacon));
        Assert.Equal(beacon, q.Current.Pos);
        QueueEditSystem.Apply(w, new DeleteWaypointCommand(0, [t], C)); // the route ends
        Assert.True(q.IsEmpty);
    }

    [Fact]
    public void OtherArmiesUnitsAreUntouched()
    {
        var (sim, units) = Group(1);
        var enemy = TestUtil.Spawn(sim, "uef_t1_tank", 1, new Vector2(150, 150));
        sim.Step([new IssueOrderCommand(1, [enemy], Order.MoveTo(B), false)]);
        var before = Q(sim, enemy).Current.Pos;
        sim.Step([new MoveWaypointCommand(0, [enemy], B, C), new DeleteWaypointCommand(0, [enemy], B),
                  new FormationMoveCommand(0, [enemy], OrderType.Move, A, C, Vector2.UnitX, false)]);
        Assert.Equal(1, Q(sim, enemy).Count);
        Assert.Equal(before, Q(sim, enemy).Current.Pos);
        Assert.Equal(3, Q(sim, units[0]).Count);
    }

    [Fact]
    public void FormationMoveLaysUnitsOnTheLineAndFacesThem()
    {
        var sim = TestUtil.NewSkirmish(seed: 3);
        var units = new UnitHandle[5];
        for (int k = 0; k < units.Length; k++) units[k] = TestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(60 + k * 3f, 60));
        var from = new Vector2(100, 80); var to = new Vector2(100, 104);
        sim.Step([new FormationMoveCommand(0, units, OrderType.Move, from, to, new Vector2(2, 0), false)]);
        var q0 = Q(sim, units[0]);
        Assert.Equal(new Vector2(1, 0), q0.Current.Aux);
        Assert.Equal((from + to) * 0.5f, q0.Current.Pos);
        TestUtil.Run(sim, 600);
        var u = sim.World.Units;
        var zs = new List<float>();
        foreach (var h in units)
        {
            var p = u.Pos[h.Index];
            Assert.True(MathF.Abs(p.X - 100) < 3f && p.Y > from.Y - 2 && p.Y < to.Y + 2, $"unit at {p}");
            Assert.True(MathF.Abs(DMath.AngleDiff(u.Heading[h.Index], 0)) < 0.1f, $"heading {u.Heading[h.Index]}");
            Assert.True(Q(sim, h).IsEmpty);
            zs.Add(p.Y);
        }
        zs.Sort();
        Assert.True(zs[^1] - zs[0] > 15f, "spread along the line");
    }

    [Fact]
    public void QueueEditingIsDeterministicAcrossThreadCounts()
    {
        ulong RunWith(int threads)
        {
            int old = Par.MaxThreads;
            Par.MaxThreads = threads;
            try
            {
                var (sim, units) = Group(12);
                TestUtil.Run(sim, 30, new MoveWaypointCommand(0, units, B, new Vector2(130, 120)));
                TestUtil.Run(sim, 30, new DeleteWaypointCommand(0, units, A));
                TestUtil.Run(sim, 300, new FormationMoveCommand(0, units, OrderType.AttackMove, new Vector2(90, 140), new Vector2(130, 150), new Vector2(0, 1), true));
                return sim.StateHash();
            }
            finally { Par.MaxThreads = old; }
        }
        Assert.Equal(RunWith(1), RunWith(4));
    }
}

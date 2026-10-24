using System.Numerics;
using Rts.Client.Input;
using Rts.Sim.Core;
using Rts.Sim.Orders;

namespace Rts.Client.Tests;

/// <summary>(7b) Pure helpers of the formation drag and waypoint editing (Input/OrderEditMath.cs).</summary>
public class OrderEditMathTests
{
    // Camera looking north (+z) on the ground: screen right = +x, screen up = +z.
    private static readonly Vector2 Right = new(1, 0), Forward = new(0, 1);

    [Fact]
    public void DragLeftToRightFacesAwayFromTheCamera()
    {
        var f = OrderEditMath.FacingFromDrag(new(100, 300), new(300, 300), new(0, 0), new(10, 0), Right, Forward);
        Assert.Equal(0, f.X, 4);
        Assert.Equal(1, f.Y, 4);
    }

    [Fact]
    public void DragRightToLeftFacesTowardsTheCamera()
    {
        var f = OrderEditMath.FacingFromDrag(new(300, 300), new(100, 300), new(10, 0), new(0, 0), Right, Forward);
        Assert.Equal(0, f.X, 4);
        Assert.Equal(-1, f.Y, 4);
    }

    [Fact]
    public void FacingIsTheLineNormalForARotatedCamera()
    {
        // Camera looking east (+x): screen right = -z (south), forward = +x. A screen left→right drag lays the
        // line along -z; facing away from the camera = +x.
        var f = OrderEditMath.FacingFromDrag(new(100, 300), new(300, 300), new(50, 20), new(50, 0),
            new Vector2(0, -1), new Vector2(1, 0));
        Assert.Equal(1, f.X, 4);
        Assert.Equal(0, f.Y, 4);
        Assert.Equal(1, f.Length(), 4);
    }

    [Fact]
    public void DegenerateLineFacesTheCameraForward()
    {
        var f = OrderEditMath.FacingFromDrag(new(100, 300), new(300, 300), new(5, 5), new(5, 5), Right, new Vector2(0, 3));
        Assert.Equal(new Vector2(0, 1), f);
        var g = OrderEditMath.FacingFromDrag(new(100, 300), new(300, 300), new(5, 5), new(5, 5), Right, Vector2.Zero);
        Assert.Equal(Vector2.UnitX, g);
    }

    [Fact]
    public void IsDragUsesTheEightPixelThreshold()
    {
        Assert.False(OrderEditMath.IsDrag(new(100, 100), new(100, 100)));
        Assert.False(OrderEditMath.IsDrag(new(100, 100), new(108, 100))); // exactly 8 px: still a click
        Assert.True(OrderEditMath.IsDrag(new(100, 100), new(108.5f, 100)));
        Assert.True(OrderEditMath.IsDrag(new(100, 100), new(106, 106))); // ~8.49 px diagonal
    }

    private static OrderEditMath.WaypointRef Ref(int unit, Vector2 p, OrderType t = OrderType.Move, bool drop = false) =>
        new(new UnitHandle(unit, 1), t, p, drop, Vector2.Zero, null);

    [Fact]
    public void ClusterMergesPointsWithinEpsilonAndListsEachUnitOnce()
    {
        var refs = new[]
        {
            Ref(1, new(10, 10)), Ref(2, new(10.03f, 10)), Ref(1, new(10, 10.02f)), // one marker, units 1 and 2
            Ref(3, new(10.2f, 10)), // beyond 0.05: its own marker
            Ref(1, new(40, 40), OrderType.Patrol),
        };
        var m = OrderEditMath.Cluster(refs);
        Assert.Equal(3, m.Count);
        Assert.Equal(new Vector2(10, 10), m[0].Point); // first-seen point
        Assert.Equal([new UnitHandle(1, 1), new UnitHandle(2, 1)], m[0].Units);
        Assert.Equal([new UnitHandle(3, 1)], m[1].Units);
        Assert.Equal(OrderType.Patrol, m[2].Type);
    }

    [Fact]
    public void FerryPickupAndDropAreSeparateMarkers()
    {
        var refs = new[]
        {
            Ref(7, new(20, 20), OrderType.Ferry), Ref(7, new(80, 60), OrderType.Ferry, drop: true),
            Ref(8, new(80.01f, 60), OrderType.Ferry, drop: true),
        };
        var m = OrderEditMath.Cluster(refs);
        Assert.Equal(2, m.Count);
        Assert.False(m[0].IsDrop);
        Assert.True(m[1].IsDrop);
        Assert.Equal(2, m[1].Units.Count);
    }

    [Fact]
    public void ClusterCapsTheMarkerCount()
    {
        var refs = Enumerable.Range(0, 10).Select(k => Ref(k, new(k * 5, 0))).Append(Ref(99, new(0, 0)));
        var m = OrderEditMath.Cluster(refs, maxMarkers: 4);
        Assert.Equal(4, m.Count);
        Assert.Contains(new UnitHandle(99, 1), m[0].Units); // still joins an existing marker
    }

    [Fact]
    public void NearestPicksTheClosestOnScreenMarkerWithinRadius()
    {
        var screen = new Vector2?[] { new(100, 100), null, new(106, 100), new(300, 300) };
        Assert.Equal(2, OrderEditMath.Nearest(screen, new(105, 100), 10));
        Assert.Equal(0, OrderEditMath.Nearest(screen, new(97, 100), 10));
        Assert.Equal(-1, OrderEditMath.Nearest(screen, new(200, 200), 10));
        Assert.Equal(-1, OrderEditMath.Nearest(Array.Empty<Vector2?>(), new(0, 0), 10));
    }
}

using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;

namespace Rts.Client.Input;

/// <summary>
/// (7b) Pure helpers of the formation drag and waypoint editing (Input/FormationDrag.cs, Input/WaypointEditor.cs),
/// kept free of raylib so they are unit-tested (tests/Rts.Client.Tests/OrderEditMathTests.cs).
/// </summary>
public static class OrderEditMath
{
    /// <summary>Right-button travel (screen pixels) that turns a right click into a formation drag.</summary>
    public const float DragThresholdPx = 8f;
    /// <summary>Screen distance (pixels, before UI scale) within which the cursor hovers a waypoint marker.</summary>
    public const float HoverRadiusPx = 10f;

    public static bool IsDrag(Vector2 pressScreen, Vector2 nowScreen) =>
        Vector2.DistanceSquared(pressScreen, nowScreen) > DragThresholdPx * DragThresholdPx;

    /// <summary>
    /// Facing of a line formation dragged on screen from <paramref name="screenFrom"/> to <paramref name="screenTo"/>
    /// (screen y down) whose ground line runs from <paramref name="worldFrom"/> to <paramref name="worldTo"/>. The
    /// screen drag vector rotated by -90° (left→right = screen up) is mapped to the ground with the camera's ground
    /// right / forward directions; the result is the ground-line normal on that side (normalized), or the camera
    /// forward when the line is degenerate. Left→right faces away from the camera, right→left towards it.
    /// </summary>
    public static Vector2 FacingFromDrag(Vector2 screenFrom, Vector2 screenTo, Vector2 worldFrom, Vector2 worldTo,
        Vector2 cameraRightXZ, Vector2 cameraForwardXZ)
    {
        var s = screenTo - screenFrom;
        var upScreen = new Vector2(s.Y, -s.X); // (1,0) right → (0,-1) up
        var want = cameraRightXZ * upScreen.X + cameraForwardXZ * -upScreen.Y;
        var seg = worldTo - worldFrom;
        if (seg.LengthSquared() < 1e-6f)
            return cameraForwardXZ.LengthSquared() > 1e-8f ? Vector2.Normalize(cameraForwardXZ) : Vector2.UnitX;
        var d = Vector2.Normalize(seg);
        var n = new Vector2(-d.Y, d.X);
        return Vector2.Dot(n, want) >= 0 ? n : -n;
    }

    /// <summary>One waypoint marker: a group point shared by the selected units' queued orders.</summary>
    public sealed class Marker
    {
        public Vector2 Point;
        public OrderType Type;
        /// <summary>True when the point is a Ferry drop point (Order.Aux) rather than an order point.</summary>
        public bool IsDrop;
        public readonly List<UnitHandle> Units = new();
        /// <summary>Neighbors on the first unit's route (previous point or the unit itself; next point if any), for the drag preview.</summary>
        public Vector2 Prev;
        public Vector2? Next;
    }

    /// <summary>One queued waypoint of one unit: its group point and the route neighbors.</summary>
    public readonly record struct WaypointRef(UnitHandle Unit, OrderType Type, Vector2 Point, bool IsDrop, Vector2 Prev, Vector2? Next);

    /// <summary>
    /// Groups waypoints whose points lie within <paramref name="eps"/> (the sim's
    /// <see cref="QueueEditSystem.MatchEpsilon"/>) into one marker each, in first-seen order; a unit is listed once per
    /// marker. At most <paramref name="maxMarkers"/> markers (the rest are ignored).
    /// </summary>
    public static List<Marker> Cluster(IEnumerable<WaypointRef> refs, float eps = QueueEditSystem.MatchEpsilon, int maxMarkers = 256)
    {
        var result = new List<Marker>();
        float e2 = eps * eps;
        foreach (var r in refs)
        {
            Marker? m = null;
            foreach (var c in result)
                if (Vector2.DistanceSquared(c.Point, r.Point) <= e2) { m = c; break; }
            if (m == null)
            {
                if (result.Count >= maxMarkers) continue;
                m = new Marker { Point = r.Point, Type = r.Type, IsDrop = r.IsDrop, Prev = r.Prev, Next = r.Next };
                result.Add(m);
            }
            if (!m.Units.Contains(r.Unit)) m.Units.Add(r.Unit);
        }
        return result;
    }

    /// <summary>Index of the marker nearest to <paramref name="mouse"/> within <paramref name="radius"/> pixels
    /// (<paramref name="screen"/>: marker screen positions, null = off screen), or -1.</summary>
    public static int Nearest(IReadOnlyList<Vector2?> screen, Vector2 mouse, float radius)
    {
        int best = -1;
        float bestD = radius * radius;
        for (int k = 0; k < screen.Count; k++)
        {
            if (screen[k] is not Vector2 s) continue;
            float d = Vector2.DistanceSquared(s, mouse);
            if (d <= bestD) { bestD = d; best = k; }
        }
        return best;
    }

    /// <summary>Order types whose point is a waypoint (the sim's <see cref="QueueEditSystem.IsWaypointType"/>).</summary>
    public static bool IsWaypoint(OrderType t) => QueueEditSystem.IsWaypointType(t);
}

using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Sim.Orders;

/// <summary>
/// (7b) Drag a waypoint of the selected units' order queues from <paramref name="From"/> to <paramref name="To"/>
/// (SupCom waypoint editing). Matches queued position orders (Move, AttackMove, Patrol, Ferry, Unload) whose group
/// point <see cref="Order.Pos"/> is within <see cref="QueueEditSystem.MatchEpsilon"/> of From; Ferry orders whose
/// drop point (<see cref="Order.Aux"/>) matches are moved too. Only the sender's own units.
/// </summary>
public sealed record MoveWaypointCommand(int Army, UnitHandle[] Units, Vector2 From, Vector2 To) : Command(Army);

/// <summary>(7b) Remove the waypoint at <paramref name="At"/> (same matching as <see cref="MoveWaypointCommand"/>;
/// a Ferry order matching on its beacon or its drop point is removed: the route ends for those units).</summary>
public sealed record DeleteWaypointCommand(int Army, UnitHandle[] Units, Vector2 At) : Command(Army);

/// <summary>
/// (7b) Right-drag line formation: Move / AttackMove (other types are ignored) with the units spread along the
/// segment From→To (extra rows behind, opposite to Facing). Every order gets Pos = segment center,
/// FormationOffset = slot − center and Aux = Facing (normalized; zero = no facing). Units turn to face Aux when such
/// an order finishes and it was their last one.
/// </summary>
public sealed record FormationMoveCommand(int Army, UnitHandle[] Units, OrderType Type, Vector2 From, Vector2 To,
    Vector2 Facing, bool Queue) : Command(Army);

/// <summary>Applies the queue-editing and line-formation commands (sequential, via CommandSystem).</summary>
public static class QueueEditSystem
{
    /// <summary>Distance within which a queued order's point matches the edited waypoint.</summary>
    public const float MatchEpsilon = 0.05f;
    /// <summary>Ticks a unit may spend turning to its arrival facing before the order completes anyway.</summary>
    public const int MaxFacingTicks = 60;

    public static bool IsWaypointType(OrderType t) =>
        t is OrderType.Move or OrderType.AttackMove or OrderType.Patrol or OrderType.Ferry or OrderType.Unload;

    private static bool Near(Vector2 a, Vector2 b) => Vector2.DistanceSquared(a, b) <= MatchEpsilon * MatchEpsilon;
    private static bool Finite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);

    /// <summary>Owned units of the command, deduplicated, sorted by slot index (deterministic).</summary>
    private static List<int> OwnedUnits(SimWorld w, int army, UnitHandle[] handles)
    {
        var units = new List<int>(handles.Length);
        foreach (var h in handles)
            if (Systems.CommandSystem.Owns(w, army, h) && !units.Contains(h.Index)) units.Add(h.Index);
        units.Sort();
        return units;
    }

    public static void Apply(SimWorld w, MoveWaypointCommand c)
    {
        if (!Finite(c.From) || !Finite(c.To)) return;
        var to = w.Terrain.Clamp(c.To);
        var moved = new List<(Vector2 Old, Vector2 New)>();
        foreach (int i in OwnedUnits(w, c.Army, c.Units))
        {
            var q = w.Units.Orders[i];
            moved.Clear();
            bool frontChanged = false;
            for (int j = 0; j < q.Count; j++)
            {
                ref var o = ref q.At(j);
                if (!IsWaypointType(o.Type)) continue;
                bool changed = false;
                if (Near(o.Pos, c.From))
                {
                    var old = o.Destination;
                    o.Pos = to;
                    moved.Add((old, o.Destination));
                    changed = true;
                }
                if (o.Type == OrderType.Ferry && Near(o.Aux, c.From)) { o.Aux = to; changed = true; }
                if (!changed) continue;
                if (o.Type is OrderType.Move or OrderType.AttackMove) o.State = 0; // was turning to its facing: go again
                if (j == 0) frontChanged = true;
            }
            // Patrol chain: Aux = the point the patrol leg starts from (the previous waypoint's destination).
            if (moved.Count > 0)
                for (int j = 0; j < q.Count; j++)
                {
                    ref var o = ref q.At(j);
                    if (o.Type != OrderType.Patrol) continue;
                    foreach (var (old, nw) in moved)
                        if (Near(o.Aux, old)) { o.Aux = nw; break; }
                }
            // The front order is turned into Move.Goal by OrderSystem every tick; force a new path for the new point.
            ref var mv = ref w.Units.Move[i];
            if (frontChanged && mv.HasGoal) mv.RepathRequested = true;
        }
    }

    public static void Apply(SimWorld w, DeleteWaypointCommand c)
    {
        if (!Finite(c.At)) return;
        var removed = new List<Vector2>();
        foreach (int i in OwnedUnits(w, c.Army, c.Units))
        {
            var q = w.Units.Orders[i];
            removed.Clear();
            bool frontRemoved = false, frontPatrolJob = false;
            for (int j = q.Count - 1; j >= 0; j--)
            {
                var o = q.At(j);
                if (!IsWaypointType(o.Type)) continue;
                if (!Near(o.Pos, c.At) && !(o.Type == OrderType.Ferry && Near(o.Aux, c.At))) continue;
                removed.Add(o.Destination);
                if (j == 0) { frontRemoved = true; frontPatrolJob = o.Type == OrderType.Patrol && o.State != 0; }
                q.RemoveAt(j);
            }
            if (removed.Count == 0) continue;
            // Patrol chain: a leg that started at a removed point now starts at the previous remaining point.
            for (int j = 0; j < q.Count; j++)
            {
                ref var o = ref q.At(j);
                if (o.Type != OrderType.Patrol) continue;
                foreach (var r in removed)
                {
                    if (!Near(o.Aux, r)) continue;
                    o.Aux = j > 0 ? q.At(j - 1).Destination
                        : q.Count > 1 && q.At(q.Count - 1).Type == OrderType.Patrol ? q.At(q.Count - 1).Destination
                        : w.Units.Pos[i];
                    break;
                }
            }
            if (frontRemoved)
            {
                // Like finishing the order: no stale goal (the next front order sets a new one next tick).
                w.Units.Move[i].HasGoal = false;
                if (frontPatrolJob) Systems.ConstructionSystem.StopBuilder(w, i); // engineer busy with a patrol job
            }
        }
    }

    public static void Apply(SimWorld w, FormationMoveCommand c)
    {
        if (c.Type is not (OrderType.Move or OrderType.AttackMove)) return;
        if (!Finite(c.From) || !Finite(c.To)) return;
        var units = OwnedUnits(w, c.Army, c.Units);
        if (units.Count == 0) return;
        var from = w.Terrain.Clamp(c.From);
        var to = w.Terrain.Clamp(c.To);
        var center = (from + to) * 0.5f;
        var facing = Finite(c.Facing) && c.Facing.LengthSquared() > 1e-6f ? Vector2.Normalize(c.Facing) : Vector2.Zero;
        var offsets = LineOffsets(w, units, from, to, facing, center);
        var order = c.Type == OrderType.Move ? Order.MoveTo(center) : Order.AttackMoveTo(center);
        order.Aux = facing;
        // Same path as a normal group move (AssignGroup, StopUnit when not queued, enqueue), with our slots.
        Systems.CommandSystem.ApplyIssueOrder(w, new IssueOrderCommand(c.Army, c.Units, order, c.Queue), offsets);
    }

    /// <summary>
    /// Line slots for <paramref name="units"/> (sorted by index, as ApplyIssueOrder collects them). See the pure
    /// overload (also used by the client's drag preview, so preview = result).
    /// </summary>
    public static Vector2[] LineOffsets(SimWorld w, List<int> units, Vector2 from, Vector2 to, Vector2 facing, Vector2 center)
    {
        var pos = new Vector2[units.Count];
        var radius = new float[units.Count];
        var structure = new bool[units.Count];
        for (int k = 0; k < units.Count; k++)
        {
            var bp = w.BpOf(units[k]);
            pos[k] = w.Units.Pos[units[k]];
            radius[k] = bp.Radius;
            structure[k] = bp.IsStructure;
        }
        return LineOffsets(pos, radius, structure, units, from, to, facing, center);
    }

    /// <summary>
    /// Pure line-formation slots (offsets from <paramref name="center"/>) for units at <paramref name="pos"/> (one entry
    /// per unit, ordered by <paramref name="ids"/> ascending; ids also break ties). Mobile units spread along from→to
    /// (spacing ≥ maxRadius*2+1.2, extra rows behind = opposite to facing, or away from the line towards the group when
    /// there is no facing); structures (factory rally points) get offset 0. Rows: front-most units (along the facing)
    /// first. Within a row units are matched to the slots left to right by their side position across the travel
    /// direction (group centroid → center), so the paths don't cross even when the group stands perpendicular to the
    /// line; when the group travels along the line, by their position along it.
    /// </summary>
    public static Vector2[] LineOffsets(IReadOnlyList<Vector2> pos, IReadOnlyList<float> radius, IReadOnlyList<bool> structure,
        IReadOnlyList<int> ids, Vector2 from, Vector2 to, Vector2 facing, Vector2 center)
    {
        int n = pos.Count;
        var result = new Vector2[n];
        var mobile = new List<int>(n); // positions k in the inputs
        var centroid = Vector2.Zero;
        float maxR = 0.5f;
        for (int k = 0; k < n; k++)
        {
            if (structure[k]) continue;
            mobile.Add(k);
            centroid += pos[k];
            maxR = MathF.Max(maxR, radius[k]);
        }
        int m = mobile.Count;
        if (m == 0) return result;
        centroid /= m;
        float spacing = maxR * 2f + 1.2f;
        bool hasFacing = facing != Vector2.Zero;

        var seg = to - from;
        float len = seg.Length();
        Vector2 dir, back;
        if (len > 1e-3f)
        {
            dir = seg / len;
            var perp = new Vector2(-dir.Y, dir.X);
            back = hasFacing ? -facing : Vector2.Dot(centroid - center, perp) >= 0 ? perp : -perp;
        }
        else
        {
            var fwd = hasFacing ? facing : center - centroid;
            fwd = fwd.LengthSquared() > 1e-4f ? Vector2.Normalize(fwd) : new Vector2(1, 0);
            dir = new Vector2(fwd.Y, -fwd.X);
            back = -fwd;
            len = 0;
        }

        // Row ordering axis: across the travel direction, oriented like dir, blended with dir (dominant when the group
        // travels along the line, and a tie-break otherwise).
        var side = dir;
        var travel = center - centroid;
        if (travel.LengthSquared() > 1f)
        {
            travel = Vector2.Normalize(travel);
            var across = new Vector2(-travel.Y, travel.X);
            if (Vector2.Dot(across, dir) < 0) across = -across;
            side = Vector2.Normalize(across + dir * 0.25f);
        }

        int cols = Math.Clamp((int)MathF.Floor(len / spacing) + 1, 1, m);
        int rows = (m + cols - 1) / cols;
        float step = cols > 1 ? len / (cols - 1) : 0f;

        // Front-most first (along -back), ties by id; then each row left to right.
        mobile.Sort((a, b) =>
        {
            int cmp = Vector2.Dot(pos[b], -back).CompareTo(Vector2.Dot(pos[a], -back));
            return cmp != 0 ? cmp : ids[a].CompareTo(ids[b]);
        });
        for (int r = 0; r < rows; r++)
        {
            int start = r * cols, inRow = Math.Min(cols, m - start);
            var row = mobile.GetRange(start, inRow);
            row.Sort((a, b) =>
            {
                int cmp = Vector2.Dot(pos[a], side).CompareTo(Vector2.Dot(pos[b], side));
                return cmp != 0 ? cmp : ids[a].CompareTo(ids[b]);
            });
            for (int c = 0; c < inRow; c++)
            {
                float x = (c - (inRow - 1) * 0.5f) * step;
                result[row[c]] = dir * x + back * (r * spacing);
            }
        }
        return result;
    }

    // ---- Arrival facing (FormationMove orders: Aux = facing direction) ----

    /// <summary>
    /// OrderSystem, Move/AttackMove order reached (only called when Aux != 0): true while the unit still has to turn to
    /// face <see cref="Order.Aux"/> (last order only, not aircraft); the turn itself is done by MovementSystem
    /// (<see cref="SteerFacing"/>). State counts the turning ticks (capped by <see cref="MaxFacingTicks"/>).
    /// </summary>
    internal static bool StillTurning(SimWorld w, int i, Data.UnitBlueprint bp, OrderQueue q, ref Order o)
    {
        if (q.Count != 1 || bp.Movement.Layer == Data.MotionLayer.Air || o.State >= MaxFacingTicks) return false;
        if (MathF.Abs(DMath.AngleDiff(w.Units.Heading[i], DMath.HeadingOf(o.Aux))) < 0.03f) return false;
        o.State++;
        return true;
    }

    /// <summary>MovementSystem (surface units without a goal): desired heading = the facing of a finished FormationMove
    /// order still turning (the unit's own queue only, parallel-safe).</summary>
    internal static void SteerFacing(SimWorld w, int i, ref float desiredHeading)
    {
        var q = w.Units.Orders[i];
        if (q.Count != 1) return;
        ref var o = ref q.Current;
        if (o.State > 0 && o.Aux != Vector2.Zero && o.Type is OrderType.Move or OrderType.AttackMove)
            desiredHeading = DMath.HeadingOf(o.Aux);
    }
}

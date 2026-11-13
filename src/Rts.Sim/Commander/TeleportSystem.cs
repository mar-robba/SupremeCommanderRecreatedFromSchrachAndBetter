using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Commander;

/// <summary>Per-world state of <see cref="TeleportSystem"/> (plain data, saved with the world).</summary>
public sealed class TeleportState
{
    private static readonly ConditionalWeakTable<SimWorld, TeleportState> Table = new();

    public static TeleportState Of(SimWorld w) => Table.GetValue(w, static x => new TeleportState(x));
    public static bool TryGet(SimWorld w, [NotNullWhen(true)] out TeleportState? s) => Table.TryGetValue(w, out s);

    /// <summary>Registered units with a teleporter (spawn/enhancement order). Dead units are pruned in Run.</summary>
    public readonly List<UnitHandle> Units = new();
    /// <summary>Energy received so far for the current jump, per unit slot.</summary>
    public readonly float[] Spent;
    /// <summary>Total energy of the current jump (fixed when the charge starts), per unit slot. 0 = not charging.</summary>
    public readonly float[] Cost;
    /// <summary>Energy/s requested last tick for the charge (paid by EconomySystem this tick), per unit slot.</summary>
    public readonly float[] Request;
    /// <summary>Order position the current charge was started for, per unit slot.</summary>
    public readonly Vector2[] Dest;

    private TeleportState(SimWorld w)
    {
        Spent = new float[w.Units.Capacity];
        Cost = new float[w.Units.Capacity];
        Request = new float[w.Units.Capacity];
        Dest = new Vector2[w.Units.Capacity];
    }
}

/// <summary>
/// SupCom Personal Teleporter (data: <see cref="UnitBlueprint.Teleport"/>, an ACU Back-slot enhancement; order:
/// <see cref="OrderType.Teleport"/> via <see cref="Order.TeleportTo"/>). SEQUENTIAL, right after ConstructionSystem
/// (which resets builders' energy requests every tick, so the charge request is added afterwards).
/// <list type="bullet">
/// <item>Front order Teleport: the unit stands still (OrderSystem clears its goal). The charge starts when the order
///   reaches the front (order State 0 → 1): Cost = EnergyBase + EnergyPerDistance × distance to the (map-clamped,
///   MaxRange-limited) destination; <see cref="EffectKind.TeleportCharge"/> is raised once.</item>
/// <item>Each tick the energy requested last tick is received × the unit's economy efficiency (a stall slows the
///   charge); the request is Cost / ChargeTime per second. Paused / stunned / carried / incomplete: no request, the
///   charge waits.</item>
/// <item>Fully charged: the arrival point is resolved (map clamp, nearest cell where the unit's layer and clearance can
///   stand), the unit moves there (path, goal and velocity cleared), the order is popped,
///   <see cref="EffectKind.TeleportOut"/> (origin) and <see cref="EffectKind.TeleportIn"/> (arrival) are raised.</item>
/// <item>Any other front order (or a new Teleport order) cancels: the energy already spent is lost.</item>
/// </list>
/// </summary>
public static class TeleportSystem
{
    /// <summary>Spawn hook (UnitLifecycle.OnSpawn; re-run on in-place blueprint changes, so idempotent). Sequential.</summary>
    public static void InitUnit(SimWorld w, int i, UnitBlueprint bp)
    {
        bool needs = bp.Teleport != null;
        TeleportState? s;
        if (needs) s = TeleportState.Of(w);
        else if (!TeleportState.TryGet(w, out s)) return;
        var h = w.Units.HandleOf(i);
        bool registered = s.Units.Contains(h);
        if (registered && needs) return;
        if (registered) s.Units.Remove(h);
        Reset(s, i);
        if (needs) s.Units.Add(h);
    }

    /// <summary>0..1 charge of unit i's current jump, -1 = not charging. Sequential (snapshot writer / tests).</summary>
    public static float Progress(SimWorld w, int i)
    {
        if (!TeleportState.TryGet(w, out var s) || (uint)i >= (uint)s.Cost.Length || s.Cost[i] <= 0) return -1;
        return Math.Clamp(s.Spent[i] / s.Cost[i], 0f, 1f);
    }

    /// <summary>Energy received so far for unit i's current jump (0 = not charging).</summary>
    public static float SpentOf(SimWorld w, int i) => TeleportState.TryGet(w, out var s) && (uint)i < (uint)s.Spent.Length ? s.Spent[i] : 0;

    /// <summary>Total energy of unit i's current jump (0 = not charging).</summary>
    public static float CostOf(SimWorld w, int i) => TeleportState.TryGet(w, out var s) && (uint)i < (uint)s.Cost.Length ? s.Cost[i] : 0;

    private static void Reset(TeleportState s, int i)
    {
        s.Spent[i] = 0;
        s.Cost[i] = 0;
        s.Request[i] = 0;
        s.Dest[i] = Vector2.Zero;
    }

    /// <summary>Destination of a jump from <paramref name="from"/> towards the order point (map clamp, MaxRange).</summary>
    public static Vector2 Target(SimWorld w, TeleportBlueprint tp, Vector2 from, Vector2 orderPos)
    {
        var p = w.Terrain.Clamp(orderPos, 1f);
        if (tp.MaxRange > 0)
        {
            var d = p - from;
            float len = d.Length();
            if (len > tp.MaxRange) p = w.Terrain.Clamp(from + d * (tp.MaxRange / len), 1f);
        }
        return p;
    }

    public static void Run(SimWorld w)
    {
        if (!TeleportState.TryGet(w, out var s) || s.Units.Count == 0) return;
        var u = w.Units;
        int n = 0;
        for (int k = 0; k < s.Units.Count; k++)
            if (u.IsActive(s.Units[k])) s.Units[n++] = s.Units[k]; // (a reused slot is reset by InitUnit)
        s.Units.RemoveRange(n, s.Units.Count - n);

        for (int k = 0; k < s.Units.Count; k++)
        {
            int i = s.Units[k].Index;
            var bp = w.BpOf(i);
            var tp = bp.Teleport;
            var q = u.Orders[i];
            if (tp == null || q.IsEmpty || q.Current.Type != OrderType.Teleport)
            {
                if (s.Cost[i] > 0) Reset(s, i); // cancelled: energy spent is lost
                continue;
            }
            ref var o = ref q.Current;
            if (o.State == 0 || s.Cost[i] <= 0 || s.Dest[i] != o.Pos)
            {
                // A new jump: price it from here.
                Reset(s, i);
                var dest = Target(w, tp, u.Pos[i], o.Pos);
                s.Cost[i] = MathF.Max(1f, tp.Cost(Vector2.Distance(u.Pos[i], dest)));
                s.Dest[i] = o.Pos;
                o.State = 1;
                CombatFx.Add(w, EffectKind.TeleportCharge, DMath.To3(u.Pos[i], u.Elevation[i]), bp.Radius, u.Army[i], bp.Index);
            }

            s.Spent[i] = MathF.Min(s.Cost[i], s.Spent[i] + s.Request[i] * SimWorld.Dt * u.Econ[i].Efficiency);
            if (s.Spent[i] >= s.Cost[i] - 1e-3f)
            {
                Jump(w, s, i, bp, tp, q);
                continue;
            }
            bool working = u.IsComplete(i) && (u.Flags[i] & (UnitFlags.Paused | UnitFlags.Stunned | UnitFlags.Carried)) == 0;
            float rate = s.Cost[i] / MathF.Max(tp.ChargeTime, SimWorld.Dt);
            float req = working ? MathF.Min(rate, (s.Cost[i] - s.Spent[i]) / SimWorld.Dt) : 0;
            if (req < 1e-3f) req = 0;
            s.Request[i] = req;
            // Builders reset/add their own requests every tick (ConstructionSystem ran just before); others only have this one.
            if (bp.Economy.BuildRate > 0) u.Econ[i].EnergyRequest += req;
            else u.Econ[i].EnergyRequest = req;
        }
    }

    private static void Jump(SimWorld w, TeleportState s, int i, UnitBlueprint bp, TeleportBlueprint tp, OrderQueue q)
    {
        var u = w.Units;
        var from = u.Pos[i];
        var p = Arrival(w, bp, Target(w, tp, from, q.Current.Pos));
        Reset(s, i);
        q.Pop();
        CombatFx.Add(w, EffectKind.TeleportOut, DMath.To3(from, u.Elevation[i]), bp.Radius, u.Army[i], bp.Index);
        u.Pos[i] = p;
        u.Elevation[i] = bp.Movement.Layer is MotionLayer.Land or MotionLayer.Amphibious
            ? w.Terrain.GetHeight(p) : w.Terrain.GetSurfaceHeight(p);
        ResetMove(w, i);
        CombatFx.Add(w, EffectKind.TeleportIn, DMath.To3(p, u.Elevation[i]), bp.Radius, u.Army[i], bp.Index);
    }

    /// <summary>Where a unit of this blueprint lands for destination p: p if it can stand there (terrain, structures,
    /// clearance), else the nearest such cell center. Deterministic. SEQUENTIAL.</summary>
    public static Vector2 Arrival(SimWorld w, UnitBlueprint bp, Vector2 p)
    {
        p = w.Terrain.Clamp(p, 1f);
        var layer = bp.Movement.Layer;
        if (layer == MotionLayer.Air) return p;
        w.Paths.EnsureGrids();
        var grid = w.Paths.GridFor(bp); // (11a-1) its slope class and radius (PathService.Fits)
        if (grid == null) return p;
        int req = PathService.ClearanceOf(bp);
        int cx = (int)MathF.Floor(p.X), cz = (int)MathF.Floor(p.Y);
        if (grid.Passable(cx, cz, req)) return p;
        int max = Math.Max(grid.Width, grid.Height);
        if (grid.FindNearestPassable(cx, cz, req, max, out int ox, out int oz)
            || grid.FindNearestPassable(cx, cz, 1, max, out ox, out oz))
            return new Vector2(ox + 0.5f, oz + 0.5f);
        return p;
    }

    private static void ResetMove(SimWorld w, int j)
    {
        ref var mv = ref w.Units.Move[j];
        mv.Velocity = Vector2.Zero;
        mv.Speed = 0;
        mv.HasGoal = false;
        mv.Arrived = false;
        mv.PathId = -1;
        mv.PathRequestTick = 0;
        mv.RepathRequested = false;
        mv.PathPartial = false;
        mv.PathToNearest = false;
        mv.StuckTime = 0;
        mv.BlockedTime = 0;
        mv.StuckRepaths = 0;
        mv.GroupId = 0;
        mv.LeaveTime = 0;
        mv.OrbitCenter = w.Units.Pos[j];
    }

    public static ulong Hash(SimWorld w)
    {
        if (!TeleportState.TryGet(w, out var s)) return 0;
        ulong h = 1469598103934665603UL;
        void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
        foreach (var uh in s.Units)
        {
            int i = uh.Index;
            Mix((ulong)i);
            Mix(BitConverter.SingleToUInt32Bits(s.Spent[i]));
            Mix(BitConverter.SingleToUInt32Bits(s.Cost[i]));
            Mix(BitConverter.SingleToUInt32Bits(s.Request[i]));
            Mix(BitConverter.SingleToUInt32Bits(s.Dest[i].X));
            Mix(BitConverter.SingleToUInt32Bits(s.Dest[i].Y));
        }
        return h;
    }
}

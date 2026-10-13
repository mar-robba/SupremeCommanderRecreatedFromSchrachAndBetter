using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Transport;

/// <summary>
/// Issuing transport orders (called by CommandSystem for Load / Unload / TransportTo / Ferry / Dock IssueOrderCommands,
/// after ownership validation). SEQUENTIAL. Each selected unit only gets the orders that make sense for it:
/// <list type="bullet">
/// <item>Load, Target = own land transport: land units walk to it and board (as many as fit); aircraft given Load on
///   an air carrier dock instead. Target = own land unit: the first selected transport with room picks it up.</item>
/// <item>Unload (Pos): carriers drop/launch their cargo at Pos (several transports spread out).</item>
/// <item>TransportTo (Pos): land units are distributed among the selected transports (largest first, each to the
///   transport with most free slots), the transports pick them up and unload at Pos.</item>
/// <item>Ferry (Pos = beacon, Aux = drop): transports shuttle; land units walk to the beacon and wait.</item>
/// <item>Dock (Target = air carrier, None = nearest): aircraft dock to repair / refuel.</item>
/// </list>
/// </summary>
public static class TransportOrders
{
    public static bool Handles(OrderType t) =>
        t is OrderType.Load or OrderType.Unload or OrderType.TransportTo or OrderType.Ferry or OrderType.Dock;

    public static Order Load(UnitHandle target) => new() { Type = OrderType.Load, Target = target, BlueprintIndex = -1, Spawned = UnitHandle.None };
    public static Order Unload(Vector2 pos) => new() { Type = OrderType.Unload, Pos = pos, Target = UnitHandle.None, BlueprintIndex = -1, Spawned = UnitHandle.None };
    public static Order TransportTo(Vector2 pos) => new() { Type = OrderType.TransportTo, Pos = pos, Target = UnitHandle.None, BlueprintIndex = -1, Spawned = UnitHandle.None };
    /// <summary>Ferry route: transports shuttle from <paramref name="beacon"/> to <paramref name="drop"/>; land units
    /// given this order walk to the beacon (drop ignored).</summary>
    public static Order Ferry(Vector2 beacon, Vector2 drop) => new() { Type = OrderType.Ferry, Pos = beacon, Aux = drop, Target = UnitHandle.None, BlueprintIndex = -1, Spawned = UnitHandle.None };
    public static Order Dock(UnitHandle carrier) => new() { Type = OrderType.Dock, Target = carrier, BlueprintIndex = -1, Spawned = UnitHandle.None };

    public static void Issue(SimWorld w, IssueOrderCommand io, List<int> units)
    {
        var u = w.Units;
        units.RemoveAll(i => (u.Flags[i] & UnitFlags.Carried) != 0 || !u.IsComplete(i));
        if (units.Count == 0) return;
        if (!io.Queue) MovementSystem.AssignGroup(w, units, false);
        var o = io.Order;
        switch (o.Type)
        {
            case OrderType.Load: IssueLoad(w, io, units); break;
            case OrderType.Unload: IssueUnload(w, io, units, o.Pos); break;
            case OrderType.TransportTo: IssueTransportTo(w, io, units, o.Pos); break;
            case OrderType.Ferry:
                foreach (int i in units)
                {
                    var bp = w.BpOf(i);
                    if (bp.IsVtol) Give(w, i, Ferry(o.Pos, o.Aux), io.Queue);
                    else if (IsLandCargo(bp)) Give(w, i, Ferry(o.Pos, o.Pos), io.Queue);
                }
                break;
            case OrderType.Dock:
                foreach (int i in units)
                {
                    var bp = w.BpOf(i);
                    if (bp.CargoKind != CargoLayer.Air || bp.CargoSize <= 0) continue;
                    var t = IsAirCarrier(w, o.Target, io.Army) ? o.Target : TransportSystem.FindAirCarrier(w, i, bp, needRoom: false);
                    if (!t.IsNone && t.Index != i) Give(w, i, Dock(t), io.Queue);
                }
                break;
        }
    }

    private static void IssueLoad(SimWorld w, IssueOrderCommand io, List<int> units)
    {
        var u = w.Units;
        var t = io.Order.Target;
        if (!CommandSystem.Owns(w, io.Army, t) || (u.Flags[t.Index] & UnitFlags.Carried) != 0) return;
        var tbp = w.BpOf(t.Index);
        if (tbp.Cargo != null)
        {
            if (tbp.Cargo.Layer == CargoLayer.Air)
            {
                foreach (int i in units)
                {
                    var bp = w.BpOf(i);
                    if (i != t.Index && bp.CargoKind == CargoLayer.Air && bp.CargoSize > 0) Give(w, i, Dock(t), io.Queue);
                }
                return;
            }
            // Land units board the transport, as many as fit (in index order).
            int free = tbp.Cargo.Capacity - w.Cargo.UsedSlots(w, t.Index);
            foreach (int i in units)
            {
                var bp = w.BpOf(i);
                if (i == t.Index || !IsLandCargo(bp) || bp.CargoSize > free) continue;
                free -= bp.CargoSize;
                Give(w, i, Load(t), io.Queue);
            }
            return;
        }
        // Target is a land unit: the first selected transport with room picks it up.
        if (!IsLandCargo(tbp)) return;
        foreach (int i in units)
        {
            var bp = w.BpOf(i);
            if (!bp.IsVtol || i == t.Index) continue;
            if (w.Cargo.UsedSlots(w, i) + tbp.CargoSize > bp.Cargo!.Capacity) continue;
            Give(w, i, Load(t), io.Queue);
            return;
        }
    }

    private static void IssueUnload(SimWorld w, IssueOrderCommand io, List<int> units, Vector2 pos)
    {
        var carriers = new List<int>();
        foreach (int i in units)
            if (w.BpOf(i).Cargo != null) carriers.Add(i);
        if (carriers.Count == 0) return;
        var offsets = SpreadOffsets(w, carriers, pos);
        for (int k = 0; k < carriers.Count; k++) Give(w, carriers[k], Unload(pos + offsets[k]), io.Queue);
    }

    private static void IssueTransportTo(SimWorld w, IssueOrderCommand io, List<int> units, Vector2 dest)
    {
        var transports = new List<int>();
        var cargo = new List<int>();
        foreach (int i in units)
        {
            var bp = w.BpOf(i);
            if (bp.IsVtol) transports.Add(i);
            else if (IsLandCargo(bp)) cargo.Add(i);
        }
        if (transports.Count == 0)
        {
            // Nothing to ride: walk instead.
            foreach (int i in cargo) Give(w, i, Order.MoveTo(dest), io.Queue);
            return;
        }
        // Distribution: biggest units first; each goes to the transport with the most free slots that fits it.
        var free = new int[transports.Count];
        var assigned = new List<int>[transports.Count];
        for (int k = 0; k < transports.Count; k++)
        {
            free[k] = w.BpOf(transports[k]).Cargo!.Capacity - w.Cargo.UsedSlots(w, transports[k]);
            assigned[k] = new List<int>();
        }
        cargo.Sort((a, b) =>
        {
            int c = w.BpOf(b).CargoSize.CompareTo(w.BpOf(a).CargoSize);
            return c != 0 ? c : a.CompareTo(b);
        });
        foreach (int i in cargo)
        {
            int size = w.BpOf(i).CargoSize, best = -1;
            for (int k = 0; k < transports.Count; k++)
                if (free[k] >= size && (best < 0 || free[k] > free[best])) best = k;
            if (best < 0) continue; // doesn't fit anywhere: left alone
            free[best] -= size;
            assigned[best].Add(i);
        }
        var offsets = SpreadOffsets(w, transports, dest);
        for (int k = 0; k < transports.Count; k++)
        {
            int t = transports[k];
            var th = w.Units.HandleOf(t);
            if (assigned[k].Count == 0 && w.Cargo.Count(t) == 0)
            {
                Give(w, t, Order.MoveTo(dest + offsets[k]), io.Queue);
                continue;
            }
            assigned[k].Sort();
            bool first = true;
            foreach (int c in assigned[k])
            {
                Give(w, t, Load(w.Units.HandleOf(c)), io.Queue || !first);
                first = false;
                Give(w, c, Load(th), io.Queue);
            }
            Give(w, t, Unload(dest + offsets[k]), io.Queue || !first);
        }
    }

    /// <summary>Spread drop points of several carriers (formation slots, wide spacing so their drop rings don't overlap).</summary>
    private static Vector2[] SpreadOffsets(SimWorld w, List<int> carriers, Vector2 pos)
    {
        if (carriers.Count <= 1) return new Vector2[carriers.Count];
        var off = CommandSystem.FormationOffsets(w, carriers, pos);
        for (int k = 0; k < off.Length; k++) off[k] *= 2.5f;
        return off;
    }

    private static void Give(SimWorld w, int i, in Order o, bool queue)
    {
        if (!queue) CommandSystem.StopUnit(w, i);
        w.Units.Orders[i].Enqueue(o);
    }

    public static bool IsLandCargo(UnitBlueprint bp) => bp.CargoKind == CargoLayer.Land && bp.CargoSize > 0 && bp.Cargo == null;

    private static bool IsAirCarrier(SimWorld w, UnitHandle t, int army)
    {
        if (!CommandSystem.Owns(w, army, t)) return false;
        var c = w.BpOf(t.Index).Cargo;
        return c != null && c.Layer == CargoLayer.Air;
    }
}

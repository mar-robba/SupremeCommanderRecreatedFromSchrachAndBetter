using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Changing a unit's army (capture, sandbox "give"). SEQUENTIAL. The unit keeps its slot and handle, health,
/// veterancy and construction state; its orders, build work, weapon targets, formation, pause flag and factory
/// queue are cleared; an in-place upgrade is cancelled (the old owner is refunded like a cancelled build); a
/// factory's unfinished unit goes with it, and so does a carrier's cargo (transport / air staging / Atlantis hold, in
/// hold order). Commanders are never transferred (the assassination rule tracks them per army), so a carrier holding
/// one can't be captured (<see cref="HoldsCommander"/>). Adjacency bonuses follow automatically (AdjacencySystem sees
/// the army change).
/// </summary>
public static class UnitTransfer
{
    /// <summary>Gives unit i to <paramref name="army"/>. Returns false if invalid, already owned, or a commander.</summary>
    public static bool Transfer(SimWorld w, int i, int army)
    {
        var u = w.Units;
        if ((uint)army >= (uint)w.Armies.Length || !u.IsActive(i)) return false;
        int old = u.Army[i];
        if (old == army || w.BpOf(i).Has(UnitCategory.Commander)) return false;
        UpgradeSystem.Cancel(w, i); // refund the old owner; no-op without an upgrade
        CommandSystem.StopUnit(w, i);
        u.Move[i].GroupId = 0;
        foreach (ref var ws in u.WeaponsOf(i)) ws.Target = UnitHandle.None;
        u.Flags[i] &= ~UnitFlags.Paused;
        ref var b = ref u.Builder[i];
        b.CaptureProgress = 0;
        b.CaptureReserved = 0;
        u.Army[i] = army;
        w.Armies[old].UnitCount--;
        w.Armies[army].UnitCount++;
        ref var f = ref u.Factory[i];
        f.Queue?.Clear();
        f.Repeat = false;
        if (u.IsActive(f.Current)) Transfer(w, f.Current.Index, army); // the unit inside goes with the factory
        var holds = w.Cargo.Holds[i];
        if (holds != null)
            for (int k = 0; k < holds.Count; k++) // the cargo goes with its carrier
                if (u.IsActive(holds[k])) Transfer(w, holds[k].Index, army);
        return true;
    }

    /// <summary>Does carrier i hold a commander (directly or inside a docked transport)?</summary>
    public static bool HoldsCommander(SimWorld w, int i)
    {
        var holds = w.Cargo.Holds[i];
        if (holds == null) return false;
        foreach (var h in holds)
            if (w.Units.IsActive(h) && (w.BpOf(h.Index).Has(UnitCategory.Commander) || HoldsCommander(w, h.Index))) return true;
        return false;
    }
}

using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Input;

/// <summary>Pure selection helpers (unit-tested): camera focus for a group, idle engineers / factories.</summary>
public static class SelectionMath
{
    /// <summary>Camera focus for a group: the point nearest to the centroid (a spread-out group still centers on a
    /// unit, not on empty ground between two clusters). Null for an empty list.</summary>
    public static Vector2? FocusPoint(IReadOnlyList<Vector2> pts)
    {
        if (pts.Count == 0) return null;
        var c = Vector2.Zero;
        foreach (var p in pts) c += p;
        c /= pts.Count;
        var best = pts[0];
        float bd = float.MaxValue;
        foreach (var p in pts)
        {
            float d = Vector2.DistanceSquared(p, c);
            if (d < bd) { bd = d; best = p; }
        }
        return best;
    }

    /// <summary>A finished engineer (not the commander) with no orders and no work.</summary>
    public static bool IsIdleEngineer(UnitBlueprint bp, in UnitView v) =>
        bp.Has(UnitCategory.Engineer) && !bp.Has(UnitCategory.Commander) && bp.IsMobile
        && (v.Flags & UnitFlags.UnderConstruction) == 0 && v.OrderCount == 0 && v.WorkMode == BuilderMode.None;

    /// <summary>A finished factory with an empty queue that is not building or upgrading.</summary>
    public static bool IsIdleFactory(UnitBlueprint bp, in UnitView v) =>
        bp.Has(UnitCategory.Factory) && (v.Flags & UnitFlags.UnderConstruction) == 0 && v.QueueCount == 0
        && v.WorkProgress < 0 && v.UpgradeBp < 0;

    /// <summary>Next unit to select when cycling through idle units: the one after <paramref name="last"/> in the
    /// list (wrapping), or the first. Returns -1 for an empty list.</summary>
    public static int NextIndex(IReadOnlyList<long> ids, long last)
    {
        if (ids.Count == 0) return -1;
        for (int i = 0; i < ids.Count; i++)
            if (ids[i] == last) return (i + 1) % ids.Count;
        return 0;
    }
}

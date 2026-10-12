using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Naval;

/// <summary>
/// Submarine dive/surface toggle (SupCom). Units with <c>Movement.Layer == Sub</c> are submerged by default;
/// <see cref="UnitFlags.Surfaced"/> puts them on the Water layer (targetable by surface weapons, seen by normal
/// vision/radar, drawn at the surface).
/// </summary>
public static class Submarines
{
    public static bool CanDive(UnitBlueprint bp) => bp.Movement.Layer == MotionLayer.Sub;

    public static bool IsSurfaced(SimWorld w, int i) => (w.Units.Flags[i] & UnitFlags.Surfaced) != 0;

    public static void Apply(SimWorld w, SetSubmergedCommand c)
    {
        var u = w.Units;
        foreach (var h in c.Units)
        {
            if (!CommandSystem.Owns(w, c.Army, h) || !CanDive(w.BpOf(h.Index))) continue;
            if (c.Submerged) u.Flags[h.Index] &= ~UnitFlags.Surfaced;
            else u.Flags[h.Index] |= UnitFlags.Surfaced;
        }
    }
}

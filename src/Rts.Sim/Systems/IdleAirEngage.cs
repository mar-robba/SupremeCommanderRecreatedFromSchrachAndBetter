using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Idle aircraft (no orders, orbiting) engage what their weapons acquire (SupCom): fighters chase enemy air,
/// bombers/gunships fly at the ground targets they can hit. The chase is leashed to the orbit center it started
/// from (<see cref="MoveState.IdleHome"/>): when the target dies or moves farther than <see cref="Leash"/> from home,
/// the aircraft returns to orbit there. Called by OrderSystem (PARALLEL): writes only unit i's MoveState.
/// </summary>
public static class IdleAirEngage
{
    /// <summary>Leash radius around home for weapon `wi`: twice its acquisition range (bombs: incl. release
    /// distance), at least the range plus the orbit radius so targets seen from the orbit can always be chased.</summary>
    public static float Leash(SimWorld w, int bpIndex, int wi)
    {
        float acquire = Combat.CombatState.Of(w).Catalog.Units[bpIndex].Weapons[wi].AcquireRange;
        return MathF.Max(2f * acquire, acquire + MovementSystem.OrbitRadius(w.Blueprints[bpIndex].Movement));
    }

    /// <summary>True if unit i is chasing (goal set); false = no chase, the caller lets it orbit.</summary>
    public static bool Update(SimWorld w, int i, UnitBlueprint bp, ref MoveState mv)
    {
        if (bp.Movement.Layer != MotionLayer.Air || bp.Weapons.Length == 0) { mv.IdleChase = false; return false; }
        var u = w.Units;
        var home = mv.IdleChase ? mv.IdleHome : mv.OrbitCenter;
        var ws = u.WeaponsOf(i);
        for (int wi = 0; wi < bp.Weapons.Length && wi < UnitStore.MaxWeapons; wi++)
        {
            var t = ws[wi].Target;
            if (!u.IsActive(t)) continue;
            var tp = u.Pos[t.Index];
            float leash = Leash(w, u.Bp[i], wi);
            if (Vector2.DistanceSquared(home, tp) > leash * leash) continue;
            if (!mv.IdleChase) { mv.IdleChase = true; mv.IdleHome = home; }
            OrderSystem.SetGoal(ref mv, tp, OrderSystem.ArriveRadius(bp));
            return true;
        }
        if (mv.IdleChase)
        {
            mv.IdleChase = false;
            mv.OrbitCenter = mv.IdleHome; // fly back and orbit where we were
        }
        return false;
    }
}

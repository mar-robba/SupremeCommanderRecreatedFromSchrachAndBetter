using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Executes the front order of each mobile unit: translates Move/AttackMove/Patrol/Attack/Guard into movement
/// goals and forced weapon targets. Build/Repair/Reclaim/Capture are executed by ConstructionSystem.
/// PARALLEL: each unit only writes its own order queue, MoveState and WeaponState.
/// </summary>
public static class OrderSystem
{
    public const float DefaultArriveRadius = 1.0f;

    public static void Run(SimWorld w)
    {
        var u = w.Units;
        Combat.CombatState.Of(w); // create the combat cache before the parallel loop (idle air engage reads its catalog)
        Par.For(u.AliveCount, 256, w, static (w, _, start, end) =>
        {
            var u = w.Units;
            for (int k = start; k < end; k++)
            {
                int i = u.AliveList[k];
                if (!u.IsComplete(i)) continue;
                var bp = w.BpOf(i);
                if (bp.IsStructure) continue; // factory rally orders are consumed by FactorySystem
                ExecuteFront(w, i, bp);
            }
        });
    }

    private static void ExecuteFront(SimWorld w, int i, UnitBlueprint bp)
    {
        var u = w.Units;
        var q = u.Orders[i];
        ref var mv = ref u.Move[i];
        var weapons = u.WeaponsOf(i);

        // Clear forced targets each tick; Attack orders re-assert them.
        for (int wi = 0; wi < bp.Weapons.Length && wi < UnitStore.MaxWeapons; wi++) weapons[wi].HasGroundTarget = false;

        if (q.IsEmpty)
        {
            if (IdleAirEngage.Update(w, i, bp, ref mv)) return;
            mv.HasGoal = false;
            return;
        }
        mv.IdleChase = false;

        ref var o = ref q.Current;
        switch (o.Type)
        {
            case OrderType.Move:
            case OrderType.AttackMove:
            {
                var dest = w.Terrain.Clamp(o.Destination);
                // ArrivedAt: also true at the nearest reachable point of an unreachable destination (and for air units).
                if (Vector2.DistanceSquared(u.Pos[i], dest) <= Sq(ArriveRadius(bp)) || MovementSystem.ArrivedAt(mv, dest))
                {
                    q.Pop();
                    mv.HasGoal = false;
                    return;
                }
                // Attack-move: hold position while any weapon is engaging.
                if (o.Type == OrderType.AttackMove && AnyWeaponEngaged(u, i, bp))
                {
                    if (!ChaseEngagedTarget(w, i, bp, ref mv)) mv.HasGoal = false;
                    return;
                }
                SetGoal(ref mv, dest, ArriveRadius(bp));
                return;
            }
            case OrderType.Patrol:
            {
                // Builder busy with a patrol job (State != 0: repair/assist/reclaim, Economy/EngineerPatrol): it drives
                // movement (ConstructionSystem); don't advance the patrol meanwhile.
                if (o.State != 0) return;
                var dest = w.Terrain.Clamp(o.Destination);
                if (AnyWeaponEngaged(u, i, bp)) { if (!ChaseEngagedTarget(w, i, bp, ref mv)) mv.HasGoal = false; return; }
                if (Vector2.DistanceSquared(u.Pos[i], dest) <= Sq(ArriveRadius(bp) + 1f) || MovementSystem.ArrivedAt(mv, dest))
                {
                    var again = o;
                    q.Pop();
                    q.Enqueue(again);
                    return;
                }
                SetGoal(ref mv, dest, ArriveRadius(bp));
                return;
            }
            case OrderType.Attack:
            {
                if (bp.Weapons.Length == 0) { q.Pop(); return; }
                Vector2 targetPos;
                if (!o.Target.IsNone)
                {
                    if (!u.IsActive(o.Target)) { q.Pop(); mv.HasGoal = false; return; }
                    // Target lost in the fog (not seen, not on radar/sonar): go to its last known position (o.Pos,
                    // State 1 = recorded) and drop the order there if it is still not found (SupCom).
                    if (o.State == 0 || Combat.CombatState.CanSee(w, u.Army[i], o.Target.Index))
                    {
                        o.Pos = u.Pos[o.Target.Index];
                        o.State = 1;
                    }
                    else
                    {
                        float near = MathF.Max(3f, bp.Radius * 2f);
                        bool there = Vector2.DistanceSquared(u.Pos[i], o.Pos) <= near * near
                                     || mv.Arrived && Vector2.DistanceSquared(mv.Goal, o.Pos) < 0.01f; // unreachable: nearest point
                        if (there) { q.Pop(); mv.HasGoal = false; return; }
                        SetGoal(ref mv, o.Pos, near * 0.5f);
                        return;
                    }
                    targetPos = u.Pos[o.Target.Index];
                    for (int wi = 0; wi < bp.Weapons.Length && wi < UnitStore.MaxWeapons; wi++)
                        if (CanTargetLayer(w, bp.Weapons[wi], o.Target.Index)) weapons[wi].Target = o.Target;
                }
                else
                {
                    targetPos = o.Pos;
                    for (int wi = 0; wi < bp.Weapons.Length && wi < UnitStore.MaxWeapons; wi++)
                    {
                        weapons[wi].HasGroundTarget = true;
                        weapons[wi].GroundTarget = o.Pos;
                    }
                }
                float range = MinWeaponRange(bp) * 0.9f;
                if (Vector2.DistanceSquared(u.Pos[i], targetPos) <= range * range) mv.HasGoal = false;
                else SetGoal(ref mv, targetPos, range * 0.8f);
                return;
            }
            case OrderType.Guard:
            {
                if (!u.IsActive(o.Target)) { q.Pop(); mv.HasGoal = false; return; }
                var tp = u.Pos[o.Target.Index];
                float follow = 4f + w.BpOf(o.Target.Index).Radius;
                if (Vector2.DistanceSquared(u.Pos[i], tp) > Sq(follow * 1.5f)) SetGoal(ref mv, tp, follow);
                else if (!bp.Has(UnitCategory.Engineer) && !bp.Has(UnitCategory.Commander)) mv.HasGoal = false;
                return; // engineers additionally assist via ConstructionSystem
            }
            case OrderType.Teleport: // stand still while Commander/TeleportSystem charges; no teleporter (removed): drop it
                mv.HasGoal = false;
                if (bp.Teleport == null) q.Pop();
                return;
            default:
                return; // Build / Repair / Reclaim / Capture: ConstructionSystem
        }
    }

    public static void SetGoal(ref MoveState mv, Vector2 goal, float arrive)
    {
        mv.Goal = goal;
        mv.HasGoal = true;
        mv.ArriveRadius = arrive;
    }

    public static float ArriveRadius(UnitBlueprint bp) => MathF.Max(DefaultArriveRadius, bp.Radius);

    internal static bool AnyWeaponEngaged(UnitStore u, int i, UnitBlueprint bp)
    {
        var ws = u.WeaponsOf(i);
        for (int wi = 0; wi < bp.Weapons.Length && wi < UnitStore.MaxWeapons; wi++)
            if (u.IsActive(ws[wi].Target)) return true;
        return false;
    }

    /// <summary>Aircraft can't hold position while engaging: they fly at their target (bombing runs, dogfights).</summary>
    private static bool ChaseEngagedTarget(SimWorld w, int i, UnitBlueprint bp, ref MoveState mv)
    {
        if (bp.Movement.Layer != MotionLayer.Air) return false;
        var ws = w.Units.WeaponsOf(i);
        for (int wi = 0; wi < bp.Weapons.Length && wi < UnitStore.MaxWeapons; wi++)
        {
            if (!w.Units.IsActive(ws[wi].Target)) continue;
            SetGoal(ref mv, w.Units.Pos[ws[wi].Target.Index], ArriveRadius(bp));
            return true;
        }
        return false;
    }

    public static float MinWeaponRange(UnitBlueprint bp)
    {
        float r = float.MaxValue;
        foreach (var wbp in bp.Weapons)
            if (!Naval.Torpedoes.IsInterceptor(wbp)) r = MathF.Min(r, wbp.Range); // skip torpedo defense
        return r == float.MaxValue ? 0 : r;
    }

    public static bool CanTargetLayer(SimWorld w, WeaponBlueprint wbp, int target) =>
        (wbp.TargetMask & LayerOf(w, target)) != 0;

    /// <summary>The layer a unit currently occupies (for targeting).</summary>
    public static LayerMask LayerOf(SimWorld w, int i)
    {
        var bp = w.BpOf(i);
        return bp.Movement.Layer switch
        {
            MotionLayer.Air => w.Units.IsComplete(i) && w.Units.Elevation[i] > w.Terrain.GetSurfaceHeight(w.Units.Pos[i]) + 1f ? LayerMask.Air : LayerMask.Land,
            MotionLayer.Water => LayerMask.Water,
            MotionLayer.Sub => Naval.Submarines.LayerOf(w, i), // dive toggle (by depth: halfway up = surface layer)
            MotionLayer.Hover => w.Terrain.IsUnderwater(w.Units.Pos[i]) ? LayerMask.Water : LayerMask.Land,
            MotionLayer.Amphibious => w.Terrain.IsUnderwater(w.Units.Pos[i]) ? LayerMask.Sub : LayerMask.Land,
            _ => bp.HasAny(UnitCategory.Naval) ? LayerMask.Water : LayerMask.Land,
        };
    }

    private static float Sq(float v) => v * v;
}

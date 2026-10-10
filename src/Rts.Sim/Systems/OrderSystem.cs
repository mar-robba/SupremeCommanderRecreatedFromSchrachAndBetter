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
        Par.For(u.AliveCount, 256, (_, start, end) =>
        {
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
            mv.HasGoal = false;
            return;
        }

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
                    mv.HasGoal = false;
                    return;
                }
                SetGoal(ref mv, dest, ArriveRadius(bp));
                return;
            }
            case OrderType.Patrol:
            {
                var dest = w.Terrain.Clamp(o.Destination);
                if (AnyWeaponEngaged(u, i, bp)) { mv.HasGoal = false; return; }
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

    private static bool AnyWeaponEngaged(UnitStore u, int i, UnitBlueprint bp)
    {
        var ws = u.WeaponsOf(i);
        for (int wi = 0; wi < bp.Weapons.Length && wi < UnitStore.MaxWeapons; wi++)
            if (u.IsActive(ws[wi].Target)) return true;
        return false;
    }

    public static float MinWeaponRange(UnitBlueprint bp)
    {
        float r = float.MaxValue;
        foreach (var wbp in bp.Weapons) r = MathF.Min(r, wbp.Range);
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
            MotionLayer.Sub => LayerMask.Sub,
            MotionLayer.Hover => w.Terrain.IsUnderwater(w.Units.Pos[i]) ? LayerMask.Water : LayerMask.Land,
            MotionLayer.Amphibious => w.Terrain.IsUnderwater(w.Units.Pos[i]) ? LayerMask.Sub : LayerMask.Land,
            _ => bp.HasAny(UnitCategory.Naval) ? LayerMask.Water : LayerMask.Land,
        };
    }

    private static float Sq(float v) => v * v;
}

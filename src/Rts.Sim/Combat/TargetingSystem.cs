using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Weapons acquire targets. PARALLEL over Units.AliveList: each unit only writes its own WeaponState.
/// Per weapon, in order:
/// 1. Attack-ground (HasGroundTarget, set by OrderSystem): no unit target.
/// 2. Forced target (front order is Attack on the unit OrderSystem put in WeaponState.Target): kept while alive
///    and detected by the army's intel, even out of range (the unit is moving to it).
/// 3. Current target kept while valid (alive enemy, targetable layer, visible, within range and outside MinRange);
///    every <see cref="EngagedScanInterval"/> ticks a re-scan switches to a strictly higher-priority target.
/// 4. Otherwise a staggered scan (every <see cref="IdleScanInterval"/>..+2 ticks, immediately after losing a target)
///    of the spatial grid picks the best target: priority tier (see <see cref="Tier"/>), then distance, then index.
/// Non-forced targets must be hittable from where the unit stands (<see cref="LineOfFire"/>: terrain line of fire for
/// straight shots/beams, an arc solution for shells), checked on acquisition and on every engaged re-scan.
/// </summary>
public static class TargetingSystem
{
    public const int IdleScanInterval = 3;
    public const int EngagedScanInterval = 15;
    private const int Chunk = 128;

    public static void Run(SimWorld w)
    {
        var s = CombatState.Of(w);
        s.Prepare(w);
        var u = w.Units;
        Par.For(u.AliveCount, Chunk, w, static (w, _, start, end) =>
        {
            var s = CombatState.Of(w);
            var u = w.Units;
            for (int k = start; k < end; k++) UpdateUnit(w, s, u.AliveList[k]);
        });
    }

    private static void UpdateUnit(SimWorld w, CombatState s, int i)
    {
        var u = w.Units;
        if (!u.IsActive(i) || !u.IsComplete(i)) return;
        var info = s.Catalog.Units[u.Bp[i]];
        if (info.Weapons.Length == 0) return;
        var bp = w.Blueprints[u.Bp[i]];

        var forced = UnitHandle.None;
        var q = u.Orders[i];
        if (!q.IsEmpty && q.Current.Type == OrderType.Attack) forced = q.Current.Target;

        int army = u.Army[i];
        var pos = u.Pos[i];
        var myLayer = s.Layer[i];
        for (int wi = 0; wi < info.Weapons.Length; wi++)
        {
            ref var ws = ref u.Weapons[i * UnitStore.MaxWeapons + wi];
            ref readonly var winfo = ref info.Weapons[wi];
            var wbp = bp.Weapons[wi];
            if (winfo.Kind == WeaponKind.Manual) continue; // silo / missile defense (Strategic module)
            if (ws.RetargetTimer > 0) ws.RetargetTimer--;
            if (winfo.Kind == WeaponKind.AntiTorpedo) { ws.Target = UnitHandle.None; continue; } // torpedo defense: never targets units

            if (ws.HasGroundTarget) { ws.Target = UnitHandle.None; continue; }
            // Forced targets are kept while alive and detected by the army (vision or radar); lost from intel -> dropped.
            if (!forced.IsNone && ws.Target == forced && u.IsActive(forced) && CombatState.CanSee(w, army, forced.Index)) continue;

            bool had = !ws.Target.IsNone;
            // Line of fire is re-checked on the engaged re-scan cadence (a target behind a cliff is dropped).
            if (had && u.IsActive(ws.Target) && CanEngage(w, s, army, pos, wbp.TargetMask, winfo.AcquireRange, wbp.MinRange, ws.Target.Index)
                && (ws.RetargetTimer > 0 || LineOfFire.CanHit(w, s, i, winfo, wbp, ws.Target.Index)))
            {
                if (ws.RetargetTimer <= 0)
                {
                    ws.RetargetTimer = EngagedScanInterval + i % 5;
                    int cur = ws.Target.Index;
                    int curTier = Tier(s, info, myLayer, cur, u);
                    if (curTier > 0)
                    {
                        int best = Scan(w, s, i, info, myLayer, winfo, wbp, out int bestTier);
                        if (best >= 0 && bestTier < curTier) ws.Target = u.HandleOf(best);
                    }
                }
                continue;
            }

            ws.Target = UnitHandle.None;
            if (had) ws.RetargetTimer = 0; // lost the target: look for a new one right away
            if (ws.RetargetTimer > 0) continue;
            ws.RetargetTimer = IdleScanInterval + (i + wi) % 3;
            int found = Scan(w, s, i, info, myLayer, winfo, wbp, out _);
            if (found >= 0) ws.Target = u.HandleOf(found);
        }
    }

    private static bool CanEngage(SimWorld w, CombatState s, int army, Vector2 pos, LayerMask mask, float range, float minRange, int j)
    {
        var u = w.Units;
        if (!s.IsEnemyArmy(army, u.Army[j])) return false;
        if ((s.Layer[j] & mask) == 0) return false;
        float d2 = Vector2.DistanceSquared(pos, u.Pos[j]);
        float r = range + s.Catalog.Units[u.Bp[j]].TargetRadius;
        if (d2 > r * r || d2 < minRange * minRange) return false;
        return CombatState.CanSee(w, army, j);
    }

    /// <summary>
    /// Target priority tier (lower = preferred), SupCom-like: units that can shoot back at the shooter's layer
    /// first (mobile before structures; artillery prefers armed structures), then other armed units, unarmed
    /// mobile units (engineers, scouts), armed structures that can't hurt us, and finally other structures.
    /// Units under construction count as unarmed.
    /// </summary>
    private static int Tier(CombatState s, UnitCombatInfo shooter, LayerMask shooterLayer, int j, UnitStore u)
    {
        var t = s.Catalog.Units[u.Bp[j]];
        bool complete = u.IsComplete(j);
        bool threat = complete && (t.ThreatMask & shooterLayer) != 0;
        if (threat)
        {
            if (shooter.PrefersStructures) return t.IsStructure ? 0 : 1;
            return t.IsStructure ? 1 : 0;
        }
        bool armed = complete && t.Armed;
        if (t.IsStructure) return armed ? 4 : 5;
        return armed ? 2 : 3;
    }

    private static int Scan(SimWorld w, CombatState s, int i, UnitCombatInfo info, LayerMask myLayer, in WeaponInfo winfo, WeaponBlueprint wbp, out int tier)
    {
        var u = w.Units;
        int army = u.Army[i];
        var v = new ScanVisitor
        {
            W = w,
            S = s,
            Shooter = info,
            ShooterIndex = i,
            WInfo = winfo,
            WBp = wbp,
            ShooterLayer = myLayer,
            Army = army,
            Pos = u.Pos[i],
            Mask = wbp.TargetMask,
            Range = winfo.AcquireRange,
            MinRange2 = wbp.MinRange * wbp.MinRange,
            Best = -1,
            BestTier = int.MaxValue,
            BestD2 = float.MaxValue,
        };
        s.ForEachInRadius(w, v.Pos, winfo.AcquireRange + s.Catalog.MaxRadius + CombatState.GridSlack, s.EnemyArmies[army], ref v);
        tier = v.BestTier;
        return v.Best;
    }

    private struct ScanVisitor : IUnitVisitor
    {
        public SimWorld W;
        public CombatState S;
        public UnitCombatInfo Shooter;
        public int ShooterIndex;
        public WeaponInfo WInfo;
        public WeaponBlueprint WBp;
        public LayerMask ShooterLayer;
        public int Army;
        public Vector2 Pos;
        public LayerMask Mask;
        public float Range;
        public float MinRange2;
        public int Best;
        public int BestTier;
        public float BestD2;

        public bool Visit(int j)
        {
            var u = W.Units;
            if (!S.IsEnemyArmy(Army, u.Army[j])) return true;
            if ((S.Layer[j] & Mask) == 0) return true;
            if (!u.IsActive(j)) return true;
            float d2 = Vector2.DistanceSquared(u.Pos[j], Pos);
            float r = Range + S.Catalog.Units[u.Bp[j]].TargetRadius;
            if (d2 > r * r || d2 < MinRange2) return true;
            int tier = Tier(S, Shooter, ShooterLayer, j, u);
            if (tier > BestTier) return true;
            if (tier == BestTier && (d2 > BestD2 || (d2 == BestD2 && j > Best))) return true;
            if (!CombatState.CanSee(W, Army, j)) return true;
            if (!LineOfFire.CanHit(W, S, ShooterIndex, WInfo, WBp, j)) return true;
            Best = j;
            BestTier = tier;
            BestD2 = d2;
            return true;
        }
    }
}

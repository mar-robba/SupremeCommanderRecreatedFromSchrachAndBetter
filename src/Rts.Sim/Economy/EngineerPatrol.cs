using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Engineer patrol automation (SupCom): a mobile builder (BuildRate &gt; 0: engineers, commanders, SCUs) on a Patrol
/// order, while walking its route, repairs damaged allied units in range &gt; assists (finishes) unfinished allied
/// structures in range &gt; reclaims props (wrecks) in range, then resumes the patrol. Never reclaims units.
/// Called by <see cref="ConstructionSystem"/> (SEQUENTIAL) for the front Patrol order of a working builder.
/// <para>The job lives in the Patrol order itself (fields Patrol doesn't otherwise use, saved with the queue):
/// <see cref="Order.State"/> = job kind (<see cref="JobNone"/> = walking), <see cref="Order.Target"/> = unit
/// (repair/assist), <see cref="Order.Spawned"/> = prop (reclaim), <see cref="Order.BlueprintIndex"/> = no new scan
/// before this tick (after an unreachable job; -1 by default). While State != 0, OrderSystem leaves the patrol
/// alone (no waypoint rotation) and the job steers the unit; State 0 = OrderSystem walks the route.</para>
/// </summary>
public static class EngineerPatrol
{
    /// <summary>Targets whose edge is within this distance of the builder (+ its half size) are picked up:
    /// about twice the engineer build range (5 + target half size), so it detours a little off its route.</summary>
    public const float ScanRadius = 12f;
    /// <summary>A walking builder scans every this many ticks, staggered by unit index ((tick + i) % N == 0).</summary>
    public const int ScanInterval = 5;
    /// <summary>A job is dropped when its target gets farther than this multiple of the scan range (target moved away).</summary>
    public const float LeashFactor = 1.5f;
    /// <summary>Only units below this fraction of their max health are worth a repair detour.</summary>
    public const float RepairBelowFraction = 0.999f;
    /// <summary>After giving up an unreachable target (stopped at the nearest reachable point, out of range):
    /// no new scan for this many ticks, so the builder walks on instead of re-picking it.</summary>
    public const int UnreachableCooldownTicks = 50;
    /// <summary>Extra radius of the unit query (centers) so big structures whose EDGE is in scan range are found.</summary>
    private const float QueryPad = 8f;

    public const int JobNone = 0, JobRepair = 1, JobAssist = 2, JobReclaim = 3;

    // Scratch, cleared before use (per thread: several SimWorlds may step at once in one process).
    [ThreadStatic, Persistence.NoSave] private static List<int>? t_tmp;
    private static List<int> _tmp => t_tmp ??= new();

    public static float ScanRange(UnitBlueprint bp) => ScanRadius + ConstructionSystem.HalfSize(bp);

    internal static void Exec(SimWorld w, int i, UnitBlueprint bp, ref Order o)
    {
        if (o.State != JobNone && !Step(w, i, bp, ref o, fresh: false)) EndJob(w, i, ref o);
        if (o.State != JobNone) return;
        if ((w.Tick + i) % ScanInterval == 0 && w.Tick >= o.BlueprintIndex && Scan(w, i, bp, ref o))
        {
            if (Step(w, i, bp, ref o, fresh: true)) return;
            EndJob(w, i, ref o);
        }
        ConstructionSystem.Idle(w, i); // walking: OrderSystem moves the unit along the route
    }

    /// <summary>Forgets the current job (the order goes back to plain walking).</summary>
    public static void ClearJob(ref Order o)
    {
        o.State = JobNone;
        o.Target = UnitHandle.None;
        o.Spawned = UnitHandle.None;
    }

    private static void EndJob(SimWorld w, int i, ref Order o)
    {
        ClearJob(ref o);
        ConstructionSystem.Idle(w, i);
        w.Units.Move[i].HasGoal = false; // OrderSystem re-targets the patrol waypoint next tick
    }

    /// <summary>One tick of the current job. False = the job is over (done, target gone/moved away/unreachable).</summary>
    private static bool Step(SimWorld w, int i, UnitBlueprint bp, ref Order o, bool fresh)
    {
        var u = w.Units;
        float leash = ScanRange(bp) * LeashFactor;
        if (o.State == JobReclaim)
        {
            var props = w.Props;
            if (!props.IsActive(o.Spawned)) return false;
            int p = o.Spawned.Index;
            if (Vector2.Distance(u.Pos[i], props.Pos[p]) - props.Radius[p] > leash) return false;
            var r = ConstructionSystem.ReclaimProp(w, i, bp, p);
            if (r == ConstructionSystem.PropReclaim.Done) return false;
            if (r == ConstructionSystem.PropReclaim.Moving && !fresh && Unreachable(w, i, props.Pos[p])) { o.BlueprintIndex = w.Tick + UnreachableCooldownTicks; return false; }
            return true;
        }

        var t = o.Target;
        if (!u.IsActive(t) || t.Index == i) return false;
        int ti = t.Index;
        if ((u.Flags[ti] & UnitFlags.Carried) != 0 || !w.IsAlly(u.Army[i], u.Army[ti])) return false;
        var tbp = w.BpOf(ti);
        var mode = u.IsComplete(ti) ? BuilderMode.Repair : BuilderMode.Build; // repairing a site finishes it
        if (mode == BuilderMode.Repair && u.Health[ti] >= Combat.Veterancy.MaxHealth(w, ti)) return false;
        if (Vector2.Distance(u.Pos[i], u.Pos[ti]) - ConstructionSystem.HalfSize(tbp) > leash) return false;
        if (!ConstructionSystem.MoveIntoRange(w, i, ConstructionSystem.BuildRange(bp, tbp), u.Pos[ti]))
        {
            if (!fresh && Unreachable(w, i, u.Pos[ti])) { o.BlueprintIndex = w.Tick + UnreachableCooldownTicks; return false; }
            ConstructionSystem.Idle(w, i);
            return true;
        }
        ConstructionSystem.Work(w, i, t, mode);
        return true;
    }

    /// <summary>The last movement step stopped at the nearest reachable point of this goal (out of range: unreachable).
    /// Not valid on the job's first tick (Arrived may still refer to the previous goal).</summary>
    private static bool Unreachable(SimWorld w, int i, Vector2 point) =>
        MovementSystem.ArrivedAt(w.Units.Move[i], w.Terrain.Clamp(point));

    /// <summary>Picks the nearest job by priority: repair &gt; assist &gt; reclaim (ties: lowest index). Order independent.</summary>
    private static bool Scan(SimWorld w, int i, UnitBlueprint bp, ref Order o)
    {
        var u = w.Units;
        var pos = u.Pos[i];
        int army = u.Army[i];
        float scan = ScanRange(bp);
        int repair = -1, assist = -1;
        float dRepair = float.MaxValue, dAssist = float.MaxValue;
        var tmp = _tmp;
        tmp.Clear();
        w.Grid.QueryRadius(u, pos, scan + QueryPad, tmp);
        foreach (int j in tmp)
        {
            if (j == i || !u.IsActive(j) || (u.Flags[j] & UnitFlags.Carried) != 0 || !w.IsAlly(army, u.Army[j])) continue;
            var jbp = w.BpOf(j);
            float d = Vector2.Distance(pos, u.Pos[j]) - ConstructionSystem.HalfSize(jbp);
            if (d > scan) continue;
            if (u.IsComplete(j))
            {
                if (u.Health[j] >= Combat.Veterancy.MaxHealth(w, j) * RepairBelowFraction) continue;
                if (OrderSystem.LayerOf(w, j) == LayerMask.Air) continue; // flying aircraft: not chased (air staging repairs them)
                if (d < dRepair || (d == dRepair && j < repair)) { dRepair = d; repair = j; }
            }
            else if (jbp.IsStructure && (d < dAssist || (d == dAssist && j < assist))) { dAssist = d; assist = j; }
        }
        tmp.Clear();
        int pick = repair >= 0 ? repair : assist;
        if (pick >= 0)
        {
            o.State = repair >= 0 ? JobRepair : JobAssist;
            o.Target = u.HandleOf(pick);
            return true;
        }
        int p = w.Props.FindNearest(pos, scan);
        if (p < 0) return false;
        o.State = JobReclaim;
        o.Spawned = w.Props.HandleOf(p);
        return true;
    }
}

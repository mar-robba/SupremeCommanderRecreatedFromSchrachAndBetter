using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.AI;

public sealed partial class AiMilitary
{
    /// <summary>
    /// Creeping advance (wave 5k, stall goal (a)): attack-move stops a unit while any weapon has a target, so an army
    /// whose long-range members (T2 missile launchers range 60, T3 artillery 80) always see something (fresh units,
    /// a point defense) never walks in: the front fights at the base edge while the rest stands 40-80 behind, and the
    /// game stalls although one side is many times stronger. When the group clearly dominates what is known around
    /// its objective and its front, every AdvanceInterval the members that stopped (holding on attack-move, or idle)
    /// move on: those at the front AdvanceStep closer to the objective, those far behind the front up to just behind
    /// it (they fire on the move), then attack-move to the objective again.
    /// </summary>
    private void Advance(AiGroup g)
    {
        if (Cfg.AdvanceInterval <= 0 || g.Kind != AiGroupKind.Army) return;
        if (W.Tick - g.AdvanceTick < Cfg.AdvanceInterval * SimWorld.TicksPerSecond) return;
        g.AdvanceTick = W.Tick;
        var u = W.Units;
        // Front: the direct-fire member closest to the objective.
        int front = -1;
        float dFront = float.MaxValue;
        foreach (int i in g.Members)
        {
            var bp = W.BpOf(i);
            if (bp.Weapons.Length == 0 || bp.HasAny(UnitCategory.IndirectFire | UnitCategory.Artillery)) continue;
            if ((AiPerception.TargetsOf(bp) & LayerMask.Land) == 0) continue;
            float d = Vector2.Distance(u.Pos[i], g.Target);
            if (d < dFront) { dFront = d; front = i; }
        }
        if (front < 0) return;
        var fp = u.Pos[front];
        float enemy = MathF.Max(V.ThreatAt(g.Target, 32f), V.ThreatNowAt(fp, 28f));
        if (g.Strength < Cfg.AdvanceMargin * enemy) return;
        var dir = dFront > 1f ? (g.Target - fp) / dFront : Vector2.Zero;
        var goal = dFront > Cfg.AdvanceStep + 5f ? _b.FindStandable(fp + dir * Cfg.AdvanceStep, MotionLayer.Land) : g.Target;
        var rear = _b.FindStandable(fp - dir * 10f, MotionLayer.Land);
        _tmp.Clear();
        _tmp2.Clear();
        foreach (int i in g.Members)
        {
            var q = u.Orders[i];
            bool holding = q.IsEmpty || (q.Current.Type == OrderType.AttackMove && !u.Move[i].HasGoal);
            if (!holding) continue;
            float d = Vector2.Distance(u.Pos[i], g.Target);
            if (d > dFront + 25f) _tmp2.Add(i); // stopped far behind the front: come up
            else _tmp.Add(i);
        }
        if (_tmp.Count == 0 && _tmp2.Count == 0) return;
        if (_b.Tracing) _b.Trace($"advance g{g.Id} n={g.Members.Count} front={_tmp.Count} rear={_tmp2.Count} dFront={dFront:F0} st={g.Strength:F0} enemy={enemy:F0}");
        if (_tmp.Count > 0)
        {
            _b.Cmd.Order(_tmp, Order.MoveTo(goal));
            _b.Cmd.Order(_tmp, Order.AttackMoveTo(g.Target), queue: true);
        }
        if (_tmp2.Count > 0)
        {
            _b.Cmd.Order(_tmp2, Order.MoveTo(rear));
            _b.Cmd.Order(_tmp2, Order.AttackMoveTo(g.Target), queue: true);
        }
    }
}

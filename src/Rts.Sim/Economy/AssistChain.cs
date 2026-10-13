using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Engineer assist chains (SupCom): assisting (= guarding) a builder that is itself assisting someone resolves to the
/// root of the chain, so A → B → factory F makes A work on F's queue even while B is still walking. Cycles and
/// chains longer than <see cref="MaxDepth"/> stop at the last valid link.
/// </summary>
public static class AssistChain
{
    public const int MaxDepth = 8;

    /// <summary>Follows Guard orders of allied builders (engineers, commanders) from <paramref name="target"/>.</summary>
    public static UnitHandle Root(SimWorld w, UnitHandle target, int self)
    {
        var u = w.Units;
        var t = target;
        for (int depth = 0; depth < MaxDepth; depth++)
        {
            if (!u.IsActive(t)) return target;
            int ti = t.Index;
            var bp = w.BpOf(ti);
            if (bp.IsStructure || bp.Economy.BuildRate <= 0 || !u.IsComplete(ti)) return t;
            var q = u.Orders[ti];
            if (q.IsEmpty || q.Current.Type != OrderType.Guard) return t;
            var next = q.Current.Target;
            if (!u.IsActive(next) || next.Index == self || next.Index == ti || !w.IsAlly(u.Army[ti], u.Army[next.Index])) return t;
            t = next;
        }
        return t;
    }
}

using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>Applies queued damage events in deterministic order and kills units at 0 HP. Sequential.</summary>
public static class DamageSystem
{
    [Persistence.NoSave] private static readonly List<int> _tmp = new(); // scratch, cleared before use

    public static void Run(SimWorld w)
    {
        var u = w.Units;
        Strategic.MissileSystem.ApplyDetonations(w); // nuke blasts (ignore shields)
        foreach (var a in w.Events.AreaDamage)
        {
            _tmp.Clear();
            w.Grid.QueryRadius(u, a.Center, a.Radius + 2f, _tmp);
            foreach (int j in _tmp)
            {
                if (!u.IsActive(j)) continue;
                if (a.Layers != LayerMask.None && (OrderSystem.LayerOf(w, j) & a.Layers) == 0) continue;
                float r = a.Radius + w.BpOf(j).Radius;
                float d = Vector2.Distance(u.Pos[j], a.Center);
                if (d > r) continue;
                if (Strategic.ShieldSystem.ProtectsFromArea(w, a.SourceArmy, a.Center, j)) continue; // blast outside an enemy dome
                // Linear falloff to 50% at the edge.
                float f = 1f - 0.5f * (d / MathF.Max(r, 0.01f));
                Apply(w, j, a.Amount * f, a.Source);
            }
        }
        foreach (var d in w.Events.Damage)
        {
            if (u.IsActive(d.Target)) Apply(w, d.Target.Index, d.Amount, d.Source);
        }
        w.Events.Damage.Clear();
        w.Events.AreaDamage.Clear();

        // Regeneration.
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            if (!u.IsActive(i) || !u.IsComplete(i)) continue;
            var bp = w.BpOf(i);
            if (bp.RegenRate > 0) u.Health[i] = MathF.Min(bp.MaxHealth, u.Health[i] + bp.RegenRate * SimWorld.Dt);
        }
    }

    public static void Apply(SimWorld w, int target, float amount, UnitHandle source)
    {
        var u = w.Units;
        if (!u.IsActive(target) || amount <= 0) return;
        u.Health[target] -= amount;
        u.LastHitTick[target] = w.Tick;
        if (u.Health[target] > 0) return;
        if (u.IsActive(source) && w.IsEnemy(u.Army[source.Index], u.Army[target])) w.Armies[u.Army[source.Index]].Stats.UnitsKilled++;
        w.KillUnit(target, source);
    }
}

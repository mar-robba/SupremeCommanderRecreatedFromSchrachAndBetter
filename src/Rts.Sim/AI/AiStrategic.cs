using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Strategic;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Missile launchers of a brain (military think): tactical launchers fire at the most valuable known enemy structure
/// in range (defenses and economy first, TMD-covered ones last); strategic launchers fire at the known cluster with
/// the most mass inside the blast (plus an exposed enemy commander), skipping targets a known anti-nuke covers.
/// Fog-fair: targets come from the perception's known structures (ghosts) and last seen commander positions.
/// Silos stockpile by themselves; anti-nukes and TMD defend automatically.
/// </summary>
public sealed class AiStrategic
{
    private readonly AiBrain _b;

    public AiStrategic(AiBrain b) => _b = b;

    public void Think()
    {
        var v = _b.View;
        if (v.Silos.Count == 0) return;
        var w = _b.W;
        var u = w.Units;
        var ss = StrategicState.Of(w);
        foreach (int s in v.Silos)
        {
            var bp = w.BpOf(s);
            if (!SiloSystem.IsLauncher(bp)) continue;
            ref var info = ref _b.Info(s);
            info.Role = AiRole.Silo;
            if (info.HoldUntil > w.Tick || ss.StockOf(w, s) <= 0) continue;
            var q = u.Orders[s];
            if (!q.IsEmpty && q.Current.Type == OrderType.Launch) continue;
            bool nuke = bp.Silo!.Kind == SiloKind.Strategic;
            if (!(nuke ? NukeTarget(s, bp, out var target) : TacticalTarget(s, bp, out target)))
            {
                info.HoldUntil = w.Tick + 50;
                continue;
            }
            _b.Cmd.Order(s, Order.LaunchAt(target));
            info.HoldUntil = w.Tick + (nuke ? 300 : 40);
            if (nuke) _b.Stats.Nukes++; else _b.Stats.Tml++;
        }
    }

    private static float Value(UnitBlueprint bp)
    {
        float m = MathF.Max(20f, bp.Economy.BuildCostMass);
        if (bp.Has(UnitCategory.Wall)) return 0;
        if (bp.Has(UnitCategory.Defense) || bp.Silo != null) m *= 1.6f;
        else if (bp.HasAny(UnitCategory.MassExtractor | UnitCategory.PowerGenerator | UnitCategory.MassFabricator)) m *= 1.3f;
        return m;
    }

    /// <summary>Known enemy missile defenses (blueprint has MissileDefense) within r of p.</summary>
    private int DefensesNear(Vector2 p, float r, bool antiNuke)
    {
        var db = _b.W.Blueprints;
        int n = 0;
        foreach (var k in _b.Perception.Structures)
        {
            var kb = db[k.Bp];
            if (kb.MissileDefense == null) continue;
            bool isAntiNuke = kb.Silo is { Kind: SiloKind.AntiNuke };
            if (isAntiNuke != antiNuke) continue;
            if (Vector2.DistanceSquared(k.Pos, p) <= r * r) n++;
        }
        return n;
    }

    private bool TacticalTarget(int silo, UnitBlueprint bp, out Vector2 target)
    {
        target = default;
        var w = _b.W;
        var from = w.Units.Pos[silo];
        var db = w.Blueprints;
        float best = 0;
        foreach (var k in _b.Perception.Structures)
        {
            if (!w.IsEnemy(_b.Army, k.Army)) continue;
            if (!SiloSystem.InRange(bp, from, k.Pos)) continue;
            var kb = db[k.Bp];
            float val = Value(kb);
            if (val <= best) continue;
            if (DefensesNear(k.Pos, 32f, antiNuke: false) > 0) val *= 0.25f;
            if (val <= best) continue;
            best = val;
            target = k.Pos;
        }
        return best >= 60f;
    }

    private bool NukeTarget(int silo, UnitBlueprint bp, out Vector2 target)
    {
        target = default;
        var w = _b.W;
        var db = w.Blueprints;
        var (inner, outer) = StrategicApi.NukeRings(db, bp.Id);
        if (outer <= 0) outer = 30f;
        float r2 = outer * outer;
        var known = _b.Perception.Structures;
        float best = 0;
        for (int i = 0; i < known.Count; i++)
        {
            var c = known[i].Pos;
            if (!w.IsEnemy(_b.Army, known[i].Army)) continue;
            float sum = 0;
            for (int j = 0; j < known.Count; j++)
                if (Vector2.DistanceSquared(known[j].Pos, c) <= r2) sum += Value(db[known[j].Bp]);
            for (int a = 0; a < w.Armies.Length; a++)
            {
                if (!w.IsEnemy(_b.Army, a) || _b.Perception.CommanderAge(a) > 300) continue;
                var cp = _b.Perception.EnemyCommanderPos[a]!.Value;
                if (Vector2.DistanceSquared(cp, c) <= MathF.Max(inner, outer * 0.6f) * MathF.Max(inner, outer * 0.6f)) sum += 20000f;
            }
            if (sum <= best || sum < 4000f) continue;
            if (DefensesNear(c, 95f, antiNuke: true) > 0) continue;
            best = sum;
            target = c;
        }
        return best > 0;
    }
}

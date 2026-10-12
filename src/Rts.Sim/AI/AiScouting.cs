using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Intel gathering of one AI (it plays with fog of war, so it must look): keeps a few land scouts and an air scout
/// alive (rebuilt when lost) and sends each idle scout to the scouting point whose information is the most valuable:
/// weight (enemy start positions &gt; deposits &gt; map center) × staleness (time since the AI last had vision there,
/// from <see cref="AiPerception.Staleness"/>) / distance, avoiding known threats and points another scout is
/// already heading to. Radar coverage is built by the economy (<see cref="AiEconomy"/>).
/// </summary>
public sealed class AiScouting
{
    private readonly AiBrain _b;
    private readonly List<(Vector2 pos, float weight)> _points = new();
    private int _lastLandScoutTick = int.MinValue / 2, _lastAirScoutTick = int.MinValue / 2;
    /// <summary>Scout trips started (diagnostics/tests).</summary>
    public int Trips;

    private SimWorld W => _b.W;
    private AiView V => _b.View;
    private AiConfig Cfg => _b.Cfg;

    public AiScouting(AiBrain b) => _b = b;

    public IReadOnlyList<(Vector2 pos, float weight)> Points => _points;

    /// <summary>Builds the list of scouting points (called once the base position is known).</summary>
    public void Initialize()
    {
        _points.Clear();
        foreach (var es in _b.EnemyStarts) _points.Add((es, 4f));
        foreach (var d in W.Terrain.Deposits)
        {
            if (Vector2.Distance(d.Pos, _b.BasePos) < 40f) continue;
            bool merged = false;
            for (int k = 0; k < _points.Count; k++)
                if (Vector2.DistanceSquared(_points[k].pos, d.Pos) < 20f * 20f) { merged = true; break; }
            if (!merged) _points.Add((d.Pos, 1.5f));
        }
        _points.Add((new Vector2(W.Terrain.Width * 0.5f, W.Terrain.Height * 0.5f), 1f));
    }

    private int CountScouts(int bp) => bp < 0 ? 0 : V.Total(bp);

    /// <summary>Should a land factory build a land scout now?</summary>
    public bool WantLandScout()
    {
        if (_b.BpScout < 0 || Cfg.LandScouts <= 0 || _b.GameTime < 40) return false;
        if (CountScouts(_b.BpScout) >= Cfg.LandScouts) return false;
        bool first = _lastLandScoutTick < 0;
        return first || W.Tick - _lastLandScoutTick >= Cfg.ScoutRebuildTime * SimWorld.TicksPerSecond;
    }

    public void LandScoutOrdered() => _lastLandScoutTick = W.Tick;

    /// <summary>Should an air factory build an air scout now?</summary>
    public bool WantAirScout()
    {
        if (_b.BpAirScout < 0 || Cfg.AirScouts <= 0) return false;
        if (CountScouts(_b.BpAirScout) >= Cfg.AirScouts) return false;
        return _lastAirScoutTick < 0 || W.Tick - _lastAirScoutTick >= Cfg.ScoutRebuildTime * SimWorld.TicksPerSecond;
    }

    public void AirScoutOrdered() => _lastAirScoutTick = W.Tick;

    public void Think()
    {
        if (_points.Count == 0) return;
        var u = W.Units;
        foreach (int i in V.LandCombat)
        {
            ref var info = ref _b.Info(i);
            if (info.Role != AiRole.Scout) continue;
            bool idle = u.Orders[i].IsEmpty;
            // Running into firepower: give up this point (it is seen now anyway) and go elsewhere.
            if (!idle && info.HoldUntil <= W.Tick && V.ThreatNowAt(u.Pos[i], 10f) > 0) idle = true;
            if (idle) SendScout(i, air: false);
        }
        foreach (int i in V.AirUnits)
        {
            ref var info = ref _b.Info(i);
            if (info.Role != AiRole.AirScout || !u.Orders[i].IsEmpty) continue;
            SendScout(i, air: true);
        }
    }

    private void SendScout(int i, bool air)
    {
        var u = W.Units;
        var bp = W.BpOf(i);
        var pos = u.Pos[i];
        ref var info = ref _b.Info(i);
        int best = -1;
        float bestScore = 0;
        for (int k = 0; k < _points.Count; k++)
        {
            var (p, weight) = _points[k];
            if (k == info.Deposit && !air) continue; // just came from there
            if (TakenByOtherScout(k, i)) continue;
            float stale = MathF.Min(_b.Perception.Staleness(V, p), 3600);
            if (stale < 300) continue; // seen in the last 30 s
            float danger = air ? V.AirThreatAt(p, 20f) : V.ThreatAt(p, 12f);
            if (danger > (air ? 150f : 400f)) continue;
            float score = weight * stale / (1f + Vector2.Distance(pos, p) / 80f);
            if (score <= bestScore) continue;
            if (!air && !_b.CanReachNear(bp.Movement.Layer, bp.Radius, pos, p, 2f)) continue;
            best = k;
            bestScore = score;
        }
        if (best < 0)
        {
            // Everything is fresh: circle near the enemy side (air) / wait at the rally (land).
            if (air) _b.Cmd.Order(i, Order.PatrolTo(W.Terrain.Clamp(_b.EnemyStarts.Count > 0 ? _b.EnemyStarts[0] : _b.Rally, 4f)));
            else if (Vector2.Distance(pos, _b.Rally) > 12f) _b.Cmd.Order(i, Order.MoveTo(_b.Rally));
            info.HoldUntil = W.Tick + 100;
            return;
        }
        var dest = _points[best].pos;
        if (!air) dest = _b.FindStandable(dest, bp.Movement.Layer);
        _b.Cmd.Order(i, Order.MoveTo(W.Terrain.Clamp(dest, 2f)));
        info.Deposit = best; // scouting point index (scouts never claim deposits)
        info.HoldUntil = W.Tick + 30;
        Trips++;
    }

    private bool TakenByOtherScout(int point, int self)
    {
        var u = W.Units;
        foreach (int j in V.LandCombat)
            if (j != self && _b.Info(j).Role == AiRole.Scout && _b.Info(j).Deposit == point && !u.Orders[j].IsEmpty) return true;
        foreach (int j in V.AirUnits)
            if (j != self && _b.Info(j).Role == AiRole.AirScout && _b.Info(j).Deposit == point && !u.Orders[j].IsEmpty) return true;
        return false;
    }
}

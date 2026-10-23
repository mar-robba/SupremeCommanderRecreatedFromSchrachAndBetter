using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>Drop target, landing zone and the landed group (see <see cref="AiTransport"/>).</summary>
public sealed partial class AiTransport
{
    /// <summary>Enemy base (or structure) the drops go for, the landing zone near it and the pickup beacon at home.</summary>
    public Vector2 TargetPos, Lz, Beacon;
    /// <summary>Land units dropped on the enemy island (gathering at the landing zone, then attacking).</summary>
    public readonly List<int> Landed = new();
    public AiGroupState LandedState;
    public Vector2 Objective;

    private bool _hasTarget, _hasLz;
    private int _targetComp = -1;
    private int _nextTargetTick, _nextLzTick, _nextObjectiveTick, _firstLandTick = -1, _orderTick;
    private readonly List<(Vector2 pos, int until)> _badLz = new();
    /// <summary>Last seen enemy commander on the target island (it kills most drops): landing zones and objectives
    /// keep away from it.</summary>
    private Vector2 _cmdPos;
    private int _cmdTick = int.MinValue / 4;

    private void TrackCommander()
    {
        var u = W.Units;
        foreach (int e in V.EnemyMobile)
        {
            if (!W.BpOf(e).Has(UnitCategory.Commander) || !_b.Perception.IsIdentified(e)) continue;
            if (LandComp(u.Pos[e], 3) != _targetComp) continue;
            _cmdPos = u.Pos[e];
            _cmdTick = W.Tick;
            return;
        }
    }

    /// <summary>Where the enemy commander probably is: last seen (5 min memory), else the enemy start.</summary>
    private Vector2 CommanderPos => W.Tick - _cmdTick < 3000 ? _cmdPos : TargetPos;

    // ------------------------------------------------------------------ target

    /// <summary>The enemy base to drop on: the nearest start of an undefeated enemy on another island, or failing that
    /// the nearest known enemy structure on another island. Re-checked every minute.</summary>
    private void UpdateTarget()
    {
        if (W.Tick < _nextTargetTick) return;
        _nextTargetTick = W.Tick + 600;
        var starts = W.Terrain.StartPositions;
        float best = float.MaxValue;
        var pos = Vector2.Zero;
        int comp = -1;
        for (int a = 0; a < W.Armies.Length; a++)
        {
            if (!W.IsEnemy(_b.Army, a) || W.Armies[a].Defeated || starts.Length == 0) continue;
            var p = starts[a % starts.Length];
            float d = Vector2.DistanceSquared(p, _b.BasePos);
            if (d >= best || !EnemyIslandHasStructures(p, a)) continue;
            int cc = LandComp(p, 8);
            if (cc < 0 || cc == _homeComp) continue;
            best = d; pos = p; comp = cc;
        }
        if (comp < 0)
        {
            var structs = _b.Perception.Structures;
            for (int k = 0; k < structs.Count; k++)
            {
                var ks = structs[k];
                float d = Vector2.DistanceSquared(ks.Pos, _b.BasePos);
                if (d >= best) continue;
                int cc = LandComp(ks.Pos, 6);
                if (cc < 0 || cc == _homeComp) continue;
                best = d; pos = ks.Pos; comp = cc;
            }
        }
        bool changed = comp != _targetComp || Vector2.DistanceSquared(pos, TargetPos) > 100f;
        _hasTarget = comp >= 0;
        _targetComp = comp;
        TargetPos = pos;
        if (changed)
        {
            _nextLzTick = 0;
            if (_b.Tracing) _b.Trace(_hasTarget ? $"drop: target ({pos.X:F0},{pos.Y:F0}) comp {comp}" : "drop: no target");
        }
    }

    /// <summary>An enemy start still counts until it was scouted recently with no known structure of that army near it.</summary>
    private bool EnemyIslandHasStructures(Vector2 start, int army)
    {
        if (_b.Perception.Staleness(V, start) > 3000) return true;
        var structs = _b.Perception.Structures;
        for (int k = 0; k < structs.Count; k++)
            if (structs[k].Army == army && Vector2.DistanceSquared(structs[k].Pos, start) < 80f * 80f) return true;
        return false;
    }

    // ------------------------------------------------------------------ landing zone

    /// <summary>Picks the landing zone every 45 s (sooner when the target changed): passable land on the target's
    /// island, 30-100 from the target, with little ground and anti-air threat, preferably out of the enemy's base
    /// defense radius and away from its structures (so the landed units can gather before the enemy army reacts),
    /// not far from home.</summary>
    private void UpdateLz()
    {
        if (W.Tick < _nextLzTick) return;
        _nextLzTick = W.Tick + 450;
        for (int k = _badLz.Count - 1; k >= 0; k--) if (_badLz[k].until <= W.Tick) _badLz.RemoveAt(k);
        float best = float.MaxValue;
        var bestP = Vector2.Zero;
        float s = Scale;
        float defR = Cfg.DefenseRadius + 10f;
        var structs = _b.Perception.Structures;
        var cmd = CommanderPos;
        for (int ring = 0; ring < 8; ring++)
        {
            float r = (30f + ring * 10f) * s;
            for (int d = 0; d < 16; d++)
            {
                var p = W.Terrain.Clamp(TargetPos + DMath.Dir(d * DMath.TwoPi / 16) * r, 4f);
                var q = _b.FindStandable(p, MotionLayer.Land);
                if (Vector2.DistanceSquared(p, q) > 12f * 12f || LandComp(q, 2) != _targetComp) continue;
                float score = V.ThreatAt(q, 20f) * 2f + V.AirThreatAt(q, 24f) * 1.5f + RouteAirThreat(_b.Rally, q)
                    + Vector2.Distance(q, _b.BasePos) * 0.2f + r * 0.3f;
                float dt = Vector2.Distance(q, TargetPos);
                if (dt < defR) score += 150f + (defR - dt) * 4f;
                float near2 = 35f * 35f;
                for (int k = 0; k < structs.Count; k++)
                {
                    float d2 = Vector2.DistanceSquared(structs[k].Pos, q);
                    if (d2 < near2) near2 = d2;
                }
                score += (35f - MathF.Sqrt(near2)) * 6f;
                score += Cfg.DropCommanderAvoid * MathF.Max(0, 60f * s - Vector2.Distance(q, cmd));
                foreach (var bad in _badLz)
                    if (Vector2.DistanceSquared(bad.pos, q) < 25f * 25f) score += 2000f;
                if (score >= best) continue;
                best = score;
                bestP = q;
            }
        }
        if (best == float.MaxValue) { _hasLz = false; return; }
        bool moved = !_hasLz || Vector2.DistanceSquared(bestP, Lz) > 10f * 10f;
        _hasLz = true;
        if (moved)
        {
            Lz = bestP;
            var toLz = Lz - _b.Rally;
            var dir = toLz.LengthSquared() > 1f ? Vector2.Normalize(toLz) : _b.EnemyDir;
            var bc = _b.FindStandable(_b.Rally + dir * 8f, MotionLayer.Land);
            Beacon = LandComp(bc, 2) == _homeComp ? bc : _b.Rally;
            if (_b.Tracing) _b.Trace($"drop: LZ ({Lz.X:F0},{Lz.Y:F0}) score {best:F0} beacon ({Beacon.X:F0},{Beacon.Y:F0})");
        }
    }

    /// <summary>Known anti-air (ships, AA, fighters) along the straight flight from a to b.</summary>
    private float RouteAirThreat(Vector2 a, Vector2 b)
    {
        float len = Vector2.Distance(a, b);
        int n = Math.Clamp((int)(len / 24f), 1, 16);
        float t = 0;
        for (int k = 1; k < n; k++) t += V.AirThreatAt(Vector2.Lerp(a, b, k / (float)n), 12f);
        return t;
    }

    // ------------------------------------------------------------------ landed group

    /// <summary>Moves delivered units to the landed group, gathers them at the landing zone and attacks once the group
    /// is big enough (or has waited long enough); a group wiped out early marks its landing zone as bad.</summary>
    private void ManageLanded()
    {
        var u = W.Units;
        int arrived = 0;
        for (int k = Cargo.Count - 1; k >= 0; k--)
        {
            int i = Cargo[k];
            if ((u.Flags[i] & UnitFlags.Carried) != 0 || LandComp(u.Pos[i], 3) == _homeComp) continue;
            Cargo.RemoveAt(k);
            Landed.Add(i);
            arrived++;
        }
        if (arrived > 0)
        {
            UnitsLanded += arrived;
            _b.Stats.Drops++;
            if (_firstLandTick < 0) _firstLandTick = W.Tick;
        }
        if (Landed.Count == 0)
        {
            if (_firstLandTick >= 0)
            {
                // Wiped out: if it happened quickly the landing zone is covered by defenses.
                if (W.Tick - _firstLandTick < 900 && _hasLz)
                {
                    _badLz.Add((Lz, W.Tick + 3000));
                    _nextLzTick = 0;
                }
                if (_b.Tracing) _b.Trace($"drop: landed group lost after {(W.Tick - _firstLandTick) / 10}s");
            }
            _firstLandTick = -1;
            LandedState = AiGroupState.Assemble;
            return;
        }

        float strength = 0;
        var centroid = Vector2.Zero;
        foreach (int i in Landed) { strength += AiPerception.Strength(W, i, W.BpOf(i)); centroid += u.Pos[i]; }
        centroid /= Landed.Count;

        if (LandedState == AiGroupState.Assemble)
        {
            // Big enough: a full wave that can take on a good share of the enemy's land army (it stays home on
            // island maps), or the gathering took too long.
            float enemy = MathF.Max(V.EnemyLandStrength, _b.Perception.EnemyLandEstimate);
            bool big = Landed.Count >= Cfg.DropWave && strength >= enemy * Cfg.DropStrengthShare;
            bool waited = W.Tick - _firstLandTick >= Cfg.DropGatherTime * 10f && Landed.Count >= Cfg.DropWave / 2;
            if (!big && !waited) return;
            LandedState = AiGroupState.Attack;
            _nextObjectiveTick = 0;
            if (_b.Tracing) _b.Trace($"drop: landed group attacks n={Landed.Count} st={strength:F0}");
        }

        bool newObjective = false;
        if (W.Tick >= _nextObjectiveTick)
        {
            _nextObjectiveTick = W.Tick + 100;
            var obj = ChooseLandedObjective(centroid);
            if (Vector2.DistanceSquared(obj, Objective) > 8f * 8f) { Objective = obj; newObjective = true; }
        }
        _tmp2.Clear();
        foreach (int i in Landed)
            if (newObjective || u.Orders[i].IsEmpty) _tmp2.Add(i);
        if (_tmp2.Count > 0 && (newObjective || W.Tick - _orderTick >= 30))
        {
            _b.Cmd.Order(_tmp2, Order.AttackMoveTo(Objective));
            _orderTick = W.Tick;
        }
    }

    /// <summary>The landed group's objective: the best known enemy structure on the target island (factories first,
    /// nearer ones preferred), else the target itself.</summary>
    private Vector2 ChooseLandedObjective(Vector2 from)
    {
        var structs = _b.Perception.Structures;
        var db = W.Blueprints;
        float best = float.MinValue;
        var pos = TargetPos;
        var cmd = CommanderPos;
        // Hard raids: extractors (away from the commander) before the factories.
        float mexValue = Cfg.Difficulty == AiDifficulty.Hard ? Cfg.DropMexValueHard : Cfg.DropMexValue;
        for (int k = 0; k < structs.Count; k++)
        {
            var ks = structs[k];
            var bp = db[ks.Bp];
            float value = bp.Has(UnitCategory.Factory) ? 60f : bp.Has(UnitCategory.MassExtractor) ? mexValue
                : bp.Has(UnitCategory.PowerGenerator) ? 30f : bp.Weapons.Length > 0 ? 25f : 10f;
            float score = value - Vector2.Distance(from, ks.Pos) * 0.25f
                - Cfg.DropCommanderAvoid * MathF.Max(0, 40f - Vector2.Distance(ks.Pos, cmd));
            if (score <= best) continue;
            int half = (int)MathF.Ceiling(MathF.Max(bp.FootprintX, bp.FootprintZ) * 0.5f) + 3;
            if (LandComp(ks.Pos, half) != _targetComp) continue;
            best = score;
            pos = ks.Pos;
        }
        return _b.FindStandable(pos, MotionLayer.Land);
    }
}

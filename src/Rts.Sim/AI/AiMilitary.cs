using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.AI;

public enum AiGroupState : byte { Attack, Retreat }

/// <summary>A wave of land units sent together at a target.</summary>
public sealed class AiGroup
{
    public int Id;
    public readonly List<int> Members = new();
    public AiGroupState State;
    public Vector2 Target;
    public UnitHandle TargetUnit = UnitHandle.None;
    public float InitialStrength;
    public int OrderTick;
    public Vector2 LastCentroid;
    public int StuckThinks;
}

/// <summary>
/// Military side of a brain: new land units gather at the rally point (the "pool"); when the pool is big enough
/// (wave size grows over time, scaled by difficulty) it becomes an <see cref="AiGroup"/> that attack-moves to the
/// best reachable known enemy structure (eco targets first, avoiding defenses stronger than the group; the enemy
/// base/commander once strong enough or late). Groups retarget when their target dies, retreat when beaten (Normal/
/// Hard), and badly damaged units go home for repair (Hard). Enemies near the base or expansions pull nearby units
/// into defense. Interceptors patrol the base, bombers raid weakly defended eco in groups, scouts patrol.
/// The commander stays home and retreats when hurt.
/// </summary>
public sealed class AiMilitary
{
    private readonly AiBrain _b;
    public readonly List<AiGroup> Groups = new();
    private int _nextGroupId = 1;
    public int WavesLaunched;
    private readonly List<int> _pool = new();
    private readonly List<int> _tmp = new();
    private readonly List<(float score, int idx)> _cands = new();

    // Threat assessment of this think.
    private float _baseThreat;
    private Vector2 _baseThreatPos;
    private float _expThreat;
    private Vector2 _expThreatPos;
    private Vector2 _defendPos;
    private int _defendTick = -10000;
    private bool[] _ownCells = [];

    private SimWorld W => _b.W;
    private AiConfig Cfg => _b.Cfg;
    private AiView V => _b.View;

    public AiMilitary(AiBrain b) => _b = b;

    public void Think()
    {
        ClassifyNewUnits();
        AssessThreats();
        CommanderSafety();
        Defend();
        ManageGroups();
        ManagePool();
        ManageAir();
        ManageScouts();
    }

    // ------------------------------------------------------------------ roles

    private void ClassifyNewUnits()
    {
        foreach (int i in V.LandCombat)
        {
            ref var info = ref _b.Info(i);
            if (info.Role != AiRole.None) continue;
            var bp = W.BpOf(i);
            info.Role = bp.Has(UnitCategory.Scout) || bp.Weapons.Length == 0 ? AiRole.Scout : AiRole.Pool;
        }
        foreach (int i in V.AirUnits)
        {
            ref var info = ref _b.Info(i);
            if (info.Role != AiRole.None) continue;
            var bp = W.BpOf(i);
            info.Role = bp.Has(UnitCategory.Bomber) ? AiRole.Bomber
                : bp.Weapons.Length == 0 ? AiRole.AirScout
                : AiRole.AirDefense;
        }
    }

    // ------------------------------------------------------------------ threats

    private void AssessThreats()
    {
        var u = W.Units;
        int cols = V.ThreatCols, rows = V.ThreatRows;
        if (_ownCells.Length != cols * rows) _ownCells = new bool[cols * rows];
        Array.Clear(_ownCells);
        foreach (int s in V.Structures)
        {
            var p = u.Pos[s];
            int cx = Math.Clamp((int)(p.X / AiView.ThreatCell), 0, cols - 1), cz = Math.Clamp((int)(p.Y / AiView.ThreatCell), 0, rows - 1);
            _ownCells[cz * cols + cx] = true;
        }

        _baseThreat = 0; _expThreat = 0;
        var baseSum = Vector2.Zero;
        float bestExpDist = float.MaxValue;
        float r2 = Cfg.DefenseRadius * Cfg.DefenseRadius;
        foreach (int e in V.EnemyMobile)
        {
            var bp = W.BpOf(e);
            if (bp.Movement.Layer == MotionLayer.Air) continue; // AA/interceptors handle air
            float st = _b.Perception.IsIdentified(e) ? AiPerception.Strength(W, e, bp) : 50f;
            if (st <= 0) st = bp.Has(UnitCategory.Engineer) ? 15f : 0f;
            if (st <= 0) continue;
            var p = u.Pos[e];
            if (Vector2.DistanceSquared(p, _b.BasePos) < r2)
            {
                _baseThreat += st;
                baseSum += p * st;
                continue;
            }
            // Near one of our outlying structures?
            int cx = Math.Clamp((int)(p.X / AiView.ThreatCell), 0, cols - 1), cz = Math.Clamp((int)(p.Y / AiView.ThreatCell), 0, rows - 1);
            bool near = false;
            for (int dz = -1; dz <= 1 && !near; dz++)
            for (int dx = -1; dx <= 1 && !near; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x >= 0 && z >= 0 && x < cols && z < rows && _ownCells[z * cols + x]) near = true;
            }
            if (!near) continue;
            float d = Vector2.Distance(p, _b.BasePos);
            if (d < bestExpDist) { bestExpDist = d; _expThreatPos = p; }
        }
        if (_baseThreat > 0) _baseThreatPos = baseSum / _baseThreat;
        if (bestExpDist < float.MaxValue) _expThreat = V.ThreatAt(_expThreatPos, 20f);
    }

    private void CommanderSafety()
    {
        int c = V.Commander;
        if (c < 0 || Cfg.CommanderRetreatHealth <= 0) return;
        var u = W.Units;
        var bp = W.BpOf(c);
        ref var info = ref _b.Info(c);
        float hp = u.Health[c] / bp.MaxHealth;
        var pos = u.Pos[c];
        float threat = V.ThreatAt(pos, 26f);
        bool danger = threat > 0 && (hp < Cfg.CommanderRetreatHealth || (threat > 2500f && hp < 0.8f));
        if (!danger) return;
        // Retreat behind the base, away from the threat.
        var away = _baseThreat > 0 ? _b.BasePos - _baseThreatPos : -_b.EnemyDir;
        away = away.LengthSquared() > 1e-3f ? Vector2.Normalize(away) : -_b.EnemyDir;
        var dest = _b.FindStandable(_b.BasePos + away * 12f, MotionLayer.Amphibious);
        if (info.HoldUntil > W.Tick && Vector2.DistanceSquared(pos, dest) < 25f) return;
        if (Vector2.DistanceSquared(pos, dest) > 16f)
        {
            _b.Cmd.Order(c, Order.MoveTo(dest));
            info.Task = AiTask.Return;
        }
        info.HoldUntil = W.Tick + 80;
    }

    // ------------------------------------------------------------------ defense

    private void Defend()
    {
        var u = W.Units;
        bool baseAttack = _baseThreat > 0;
        bool expAttack = !baseAttack && _expThreat > 0 && Vector2.Distance(_expThreatPos, _b.Rally) < 110f;
        if (!baseAttack && !expAttack)
        {
            // Threat gone: defenders go back to the pool.
            _tmp.Clear();
            foreach (int i in V.LandCombat)
            {
                ref var info = ref _b.Info(i);
                if (info.Role != AiRole.Defend) continue;
                info.Role = AiRole.Pool;
                _tmp.Add(i);
            }
            if (_tmp.Count > 0) _b.Cmd.Order(_tmp, Order.MoveTo(_b.Rally));
            return;
        }

        var target = baseAttack ? _baseThreatPos : _expThreatPos;
        float threat = baseAttack ? _baseThreat : _expThreat;
        // Who responds: the pool and defenders; for base attacks also groups still close to home.
        _tmp.Clear();
        float strength = 0;
        foreach (int i in V.LandCombat)
        {
            ref var info = ref _b.Info(i);
            bool take = info.Role is AiRole.Pool or AiRole.Defend;
            if (!take && baseAttack && info.Role == AiRole.Attack && Vector2.Distance(u.Pos[i], _b.BasePos) < Cfg.DefenseRadius + 45f)
                take = true;
            if (!take) continue;
            _tmp.Add(i);
            strength += AiPerception.Strength(W, i, W.BpOf(i));
        }
        if (_tmp.Count == 0) return;
        // Expansions: only fight when we clearly win (the base is what matters).
        if (expAttack && strength < threat * 1.2f) return;

        bool reissue = Vector2.Distance(target, _defendPos) > 10f || W.Tick - _defendTick > 80;
        _tmp2.Clear();
        foreach (int i in _tmp)
        {
            ref var info = ref _b.Info(i);
            bool fresh = info.Role != AiRole.Defend;
            if (fresh && info.Role == AiRole.Attack) RemoveFromGroup(i, info.Group);
            info.Role = AiRole.Defend;
            info.Group = -1;
            if (fresh || reissue || u.Orders[i].IsEmpty) _tmp2.Add(i);
        }
        if (_tmp2.Count == 0) return;
        _b.Cmd.Order(_tmp2, Order.AttackMoveTo(_b.FindStandable(target, MotionLayer.Land)));
        _defendPos = target;
        _defendTick = W.Tick;
    }

    private readonly List<int> _tmp2 = new();
    private readonly List<int> _first = new();
    private readonly List<int> _second = new();

    // ------------------------------------------------------------------ pool & waves

    private void ManagePool()
    {
        var u = W.Units;
        _pool.Clear();
        _tmp.Clear();
        float strength = 0;
        foreach (int i in V.LandCombat)
        {
            ref var info = ref _b.Info(i);
            if (info.Role != AiRole.Pool) continue;
            _pool.Add(i);
            strength += AiPerception.Strength(W, i, W.BpOf(i));
            if (u.Orders[i].IsEmpty && Vector2.DistanceSquared(u.Pos[i], _b.Rally) > 14f * 14f && info.HoldUntil <= W.Tick) _tmp.Add(i);
        }
        if (_tmp.Count > 0)
        {
            _b.Cmd.Order(_tmp, Order.MoveTo(_b.Rally));
            foreach (int i in _tmp) _b.Info(i).HoldUntil = W.Tick + 50;
        }
        if (_baseThreat > 0 || _pool.Count == 0) return;

        int threshold = Math.Min(Cfg.MaxWave, Cfg.FirstWave + Cfg.WaveGrowth * WavesLaunched);
        bool big = _pool.Count >= Cfg.MaxWave;
        if (_b.GameTime < Cfg.FirstAttackTime && !big) return;
        if (_pool.Count < threshold) return;

        // Only units that have gathered (near the rally) leave with the wave.
        _tmp.Clear();
        float waveStrength = 0;
        foreach (int i in _pool)
        {
            if (Vector2.DistanceSquared(u.Pos[i], _b.Rally) > 30f * 30f) continue;
            _tmp.Add(i);
            waveStrength += AiPerception.Strength(W, i, W.BpOf(i));
        }
        if (_tmp.Count < threshold * 3 / 4) return;
        var centroid = Centroid(_tmp);
        if (!ChooseTarget(centroid, waveStrength, big, out var tpos, out var th)) return;

        var g = new AiGroup { Id = _nextGroupId++, State = AiGroupState.Attack, Target = tpos, TargetUnit = th, InitialStrength = waveStrength, OrderTick = W.Tick, LastCentroid = centroid };
        foreach (int i in _tmp)
        {
            ref var info = ref _b.Info(i);
            info.Role = AiRole.Attack;
            info.Group = g.Id;
            g.Members.Add(i);
        }
        Groups.Add(g);
        WavesLaunched++;
        _b.Cmd.Order(g.Members, Order.AttackMoveTo(tpos));
    }

    private Vector2 Centroid(List<int> units)
    {
        var u = W.Units;
        var c = Vector2.Zero;
        foreach (int i in units) c += u.Pos[i];
        return units.Count > 0 ? c / units.Count : _b.Rally;
    }

    /// <summary>
    /// Best reachable known enemy structure for a group of the given strength at 'from' (eco first, closer first,
    /// avoiding defenses that outgun the group; the enemy base and commander when pushing). Falls back to the known
    /// enemy commander position and to enemy start positions.
    /// </summary>
    private bool ChooseTarget(Vector2 from, float strength, bool force, out Vector2 pos, out UnitHandle handle)
    {
        pos = default;
        handle = UnitHandle.None;
        var db = W.Blueprints;
        var known = _b.Perception.Structures;
        bool push = _b.GameTime > Cfg.PushTime || strength > 2500f;
        _cands.Clear();
        for (int k = 0; k < known.Count; k++)
        {
            var ks = known[k];
            var bp = db[ks.Bp];
            float d = Vector2.Distance(from, ks.Pos);
            float score = d;
            if (bp.Has(UnitCategory.MassExtractor)) score -= 30f;
            else if (bp.HasAny(UnitCategory.PowerGenerator | UnitCategory.Engineer)) score -= 10f;
            float defense = V.ThreatAt(ks.Pos, 24f);
            if (defense > strength * 0.8f) { if (!force && !push) continue; score += 150f; }
            if (push)
            {
                foreach (var es in _b.EnemyStarts)
                    if (Vector2.DistanceSquared(es, ks.Pos) < 40f * 40f) { score -= 60f; break; }
            }
            _cands.Add((score, k));
        }
        _cands.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : a.idx.CompareTo(b.idx));
        int checks = 0;
        foreach (var (_, k) in _cands)
        {
            if (checks++ >= 12) break;
            var ks = known[k];
            var bp = db[ks.Bp];
            if (!_b.CanReachNear(MotionLayer.Land, 0.6f, from, ks.Pos, ConstructionHalf(bp))) continue;
            pos = ks.Pos;
            handle = ks.Handle;
            return true;
        }
        // Nothing known (or reachable): the enemy commander if we can take it, else scout the enemy starts.
        for (int a = 0; a < _b.Perception.EnemyCommanderPos.Length; a++)
        {
            if (_b.Perception.EnemyCommanderPos[a] is not Vector2 cp) continue;
            if (!force && !push && V.ThreatAt(cp, 24f) > strength * 0.8f) continue;
            if (!_b.CanReachNear(MotionLayer.Land, 0.6f, from, cp, 2f)) continue;
            pos = cp;
            return true;
        }
        float best = float.MaxValue;
        bool found = false;
        foreach (var es in _b.EnemyStarts)
        {
            float d = Vector2.DistanceSquared(es, from);
            if (d >= best) continue;
            if (!force && !push && V.ThreatAt(es, 24f) > strength * 0.8f) continue;
            if (!_b.CanReachNear(MotionLayer.Land, 0.6f, from, es, 2f)) continue;
            best = d;
            pos = es;
            found = true;
        }
        return found;
    }

    private static float ConstructionHalf(UnitBlueprint bp) => MathF.Max(bp.FootprintX, bp.FootprintZ) * 0.5f;

    private void RemoveFromGroup(int unit, int groupId)
    {
        foreach (var g in Groups)
            if (g.Id == groupId) { g.Members.Remove(unit); return; }
    }

    private void ManageGroups()
    {
        var u = W.Units;
        for (int gi = Groups.Count - 1; gi >= 0; gi--)
        {
            var g = Groups[gi];
            // Prune dead members and units that changed role (pulled into defense, sent home).
            for (int k = g.Members.Count - 1; k >= 0; k--)
            {
                int i = g.Members[k];
                bool keep = u.IsActive(i) && u.Army[i] == _b.Army;
                if (keep)
                {
                    ref var info = ref _b.Info(i);
                    keep = info.Role == AiRole.Attack && info.Group == g.Id;
                }
                if (!keep) g.Members.RemoveAt(k);
            }
            if (g.Members.Count == 0) { Groups.RemoveAt(gi); continue; }

            float strength = 0;
            int idle = 0;
            foreach (int i in g.Members)
            {
                strength += AiPerception.Strength(W, i, W.BpOf(i));
                if (u.Orders[i].IsEmpty) idle++;
            }
            var centroid = Centroid(g.Members);

            // Micro: badly damaged units go home to be repaired (Hard).
            if (Cfg.PullBackHealth > 0 && g.State == AiGroupState.Attack)
            {
                _tmp.Clear();
                foreach (int i in g.Members)
                    if (u.Health[i] < W.BpOf(i).MaxHealth * Cfg.PullBackHealth) _tmp.Add(i);
                if (_tmp.Count > 0 && _tmp.Count < g.Members.Count)
                {
                    foreach (int i in _tmp)
                    {
                        ref var info = ref _b.Info(i);
                        info.Role = AiRole.Pool;
                        info.Group = -1;
                        info.HoldUntil = W.Tick + 100;
                        g.Members.Remove(i);
                    }
                    _b.Cmd.Order(_tmp, Order.MoveTo(_b.FindStandable(_b.BasePos + _b.EnemyDir * 12f, MotionLayer.Land)));
                }
            }

            if (g.State == AiGroupState.Retreat)
            {
                if (idle == g.Members.Count || Vector2.Distance(centroid, _b.Rally) < 16f) Dissolve(gi);
                continue;
            }

            // Retreat when beaten (Normal/Hard).
            float local = V.ThreatAt(centroid, 24f);
            if (Cfg.RetreatFraction > 0 && strength < g.InitialStrength * Cfg.RetreatFraction && local > strength)
            {
                g.State = AiGroupState.Retreat;
                _b.Cmd.Order(g.Members, Order.MoveTo(_b.Rally));
                g.OrderTick = W.Tick;
                continue;
            }

            bool targetGone = !g.TargetUnit.IsNone ? !TargetStillKnown(g.TargetUnit) : idle == g.Members.Count;
            bool stuck = false;
            if (W.Tick - g.OrderTick > 300)
            {
                if (Vector2.Distance(centroid, g.LastCentroid) < 3f) g.StuckThinks++;
                else g.StuckThinks = 0;
                g.LastCentroid = centroid;
                stuck = g.StuckThinks >= 6 && local <= 0;
            }
            if (!targetGone && !stuck && idle < g.Members.Count) continue;
            if (!targetGone && !stuck && idle == g.Members.Count && W.Tick - g.OrderTick < 50) continue;

            if (ChooseTarget(centroid, strength, false, out var tpos, out var th))
            {
                g.Target = tpos;
                g.TargetUnit = th;
                g.OrderTick = W.Tick;
                g.StuckThinks = 0;
                _b.Cmd.Order(g.Members, Order.AttackMoveTo(tpos));
            }
            else
            {
                g.State = AiGroupState.Retreat;
                _b.Cmd.Order(g.Members, Order.MoveTo(_b.Rally));
                g.OrderTick = W.Tick;
            }
        }
    }

    private bool TargetStillKnown(UnitHandle h)
    {
        foreach (var ks in _b.Perception.Structures)
            if (ks.Handle == h) return true;
        return W.Units.IsActive(h) && !W.BpOf(h.Index).IsStructure && _b.Perception.IsKnown(h.Index);
    }

    private void Dissolve(int gi)
    {
        var g = Groups[gi];
        foreach (int i in g.Members)
        {
            ref var info = ref _b.Info(i);
            info.Role = AiRole.Pool;
            info.Group = -1;
        }
        Groups.RemoveAt(gi);
    }

    // ------------------------------------------------------------------ air

    private void ManageAir()
    {
        var u = W.Units;
        var perp = new Vector2(-_b.EnemyDir.Y, _b.EnemyDir.X);
        _tmp.Clear();   // idle bombers at home
        _tmp2.Clear();  // idle interceptors
        foreach (int i in V.AirUnits)
        {
            ref var info = ref _b.Info(i);
            var q = u.Orders[i];
            switch (info.Role)
            {
                case AiRole.AirDefense:
                    if (q.IsEmpty) _tmp2.Add(i);
                    break;
                case AiRole.Bomber:
                    if (!q.IsEmpty) break;
                    if (Vector2.Distance(u.Pos[i], _b.BasePos) < 40f) _tmp.Add(i);
                    else _b.Cmd.Order(i, Order.MoveTo(_b.AirRally));
                    break;
                case AiRole.AirScout:
                    if (q.IsEmpty && _b.EnemyStarts.Count > 0)
                    {
                        var rng = _b.Rng(i);
                        var es = _b.EnemyStarts[rng.Range(0, _b.EnemyStarts.Count)];
                        _b.Cmd.Order(i, Order.PatrolTo(W.Terrain.Clamp(es + DMath.Dir(rng.Range(0f, DMath.TwoPi)) * 20f, 4f)));
                    }
                    break;
            }
        }
        if (_tmp2.Count > 0)
        {
            // Interceptors patrol over the base (patrol makes aircraft chase what they engage).
            var a = W.Terrain.Clamp(_b.BasePos + perp * 22f + _b.EnemyDir * 10f, 4f);
            var b = W.Terrain.Clamp(_b.BasePos - perp * 22f + _b.EnemyDir * 10f, 4f);
            _first.Clear();
            _second.Clear();
            for (int k = 0; k < _tmp2.Count; k++) (k % 2 == 0 ? _first : _second).Add(_tmp2[k]);
            if (_first.Count > 0) _b.Cmd.Order(_first, Order.PatrolTo(a));
            if (_second.Count > 0) _b.Cmd.Order(_second, Order.PatrolTo(b));
        }
        if (_tmp.Count >= Cfg.BomberGroup)
        {
            int target = ChooseBomberTarget(_b.BasePos);
            if (target >= 0)
            {
                _b.Cmd.Order(_tmp, Order.AttackUnit(u.HandleOf(target)));
                _b.Cmd.Order(_tmp, Order.MoveTo(_b.AirRally), queue: true);
            }
        }
    }

    /// <summary>Enemy eco structure or engineer with the least anti-air around it (then the closest).</summary>
    private int ChooseBomberTarget(Vector2 from)
    {
        var u = W.Units;
        int best = -1;
        float bestScore = float.MaxValue;
        void Consider(int e, float bonus)
        {
            var p = u.Pos[e];
            float aa = AirDefenseNear(p);
            float score = Vector2.Distance(from, p) + aa * 2f - bonus;
            if (score < bestScore) { bestScore = score; best = e; }
        }
        foreach (int e in V.EnemyStructures)
        {
            if (!_b.Perception.IsIdentified(e)) continue;
            var bp = W.BpOf(e);
            if (bp.Has(UnitCategory.MassExtractor)) Consider(e, 40f);
            else if (bp.Has(UnitCategory.PowerGenerator)) Consider(e, 20f);
        }
        foreach (int e in V.EnemyMobile)
        {
            if (!_b.Perception.IsIdentified(e)) continue;
            if (W.BpOf(e).Has(UnitCategory.Engineer) && !W.BpOf(e).Has(UnitCategory.Commander)) Consider(e, 30f);
        }
        return best;
    }

    private float AirDefenseNear(Vector2 p) => V.AirThreatAt(p, 30f);

    // ------------------------------------------------------------------ scouts

    private void ManageScouts()
    {
        var u = W.Units;
        var deps = W.Terrain.Deposits;
        foreach (int i in V.LandCombat)
        {
            ref var info = ref _b.Info(i);
            if (info.Role != AiRole.Scout || !u.Orders[i].IsEmpty) continue;
            // Patrol to a random deposit away from home (prefers the middle and enemy side of the map).
            var rng = _b.Rng(i ^ 0x5C);
            var bp = W.BpOf(i);
            Vector2 dest = _b.EnemyStarts.Count > 0 ? _b.EnemyStarts[0] : _b.Rally;
            for (int tries = 0; tries < 8 && deps.Length > 0; tries++)
            {
                var p = deps[rng.Range(0, deps.Length)].Pos;
                if (Vector2.Distance(p, _b.BasePos) < 50f) continue;
                if (!_b.CanReachNear(bp.Movement.Layer, bp.Radius, u.Pos[i], p, 2f)) continue;
                dest = p;
                break;
            }
            _b.Cmd.Order(i, Order.PatrolTo(_b.FindStandable(dest + DMath.Dir(rng.Range(0f, DMath.TwoPi)) * 4f, bp.Movement.Layer)));
        }
    }
}

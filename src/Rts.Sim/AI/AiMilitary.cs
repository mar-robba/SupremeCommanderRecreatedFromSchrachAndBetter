using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.AI;

public enum AiGroupState : byte { Assemble, Attack, Retreat }

/// <summary>Army = the main force (one per AI); Raid = small party hitting weakly defended extractors;
/// Clear = pool units sent to chase enemies off our side's deposits (back to the pool afterwards);
/// Reinforce = a batch of fresh units walking to the army (merged into it on arrival).</summary>
public enum AiGroupKind : byte { Army, Raid, Clear, Reinforce }

/// <summary>A group of land units the AI moves together.</summary>
public sealed class AiGroup
{
    public int Id;
    public AiGroupKind Kind;
    public readonly List<int> Members = new();
    public AiGroupState State;
    /// <summary>Objective position and (if it is a known structure) its handle.</summary>
    public Vector2 Target;
    public UnitHandle TargetUnit = UnitHandle.None;
    /// <summary>Where the army gathers before attacking the objective.</summary>
    public Vector2 Staging;
    /// <summary>Strength when the current attack started (retreat threshold).</summary>
    public float InitialStrength;
    public int OrderTick;
    public int StateTick;
    public int NextObjectiveTick;
    public Vector2 LastCentroid;
    public int CheckTick;
    public int StuckThinks;
    public int Reissues;
    public int SnipeTick;
    /// <summary>Army: gathering again mid-attack because it got strung out (no strength check, short wait).</summary>
    public bool Regroup;
    public int RegroupTick;
    public int EngageTick;
    // Computed at the start of each military think.
    public float Strength;
    public Vector2 Centroid;
    /// <summary>Position of the member closest to the centroid (standable, unlike the centroid of a spread group).</summary>
    public Vector2 Anchor;
    /// <summary>Members farther than 40 from the anchor.</summary>
    public int Spread;
    public int Idle;
    /// <summary>Clearing jobs go back to the pool when done.</summary>
    public bool ReturnWhenDone => Kind == AiGroupKind.Clear;
}

/// <summary>
/// Military side of a brain. New land units gather at the rally point (the "pool"). Once the first attack time has
/// passed and the pool is big enough it becomes the <b>army</b>: one concentrated force that picks an objective
/// from what the AI knows (known enemy structures by value / distance / defenses, the enemy base and commander when
/// pushing, unexplored enemy starts as a fallback), gathers at a staging point outside the objective's known
/// defenses, waits for stragglers and for enough strength (margin over the estimated enemy strength there, or a
/// size cap / timeout), then attacks. After a kill it moves on to nearby objectives or re-assembles near the next
/// one; it retreats to the rally when beaten (not in the late game). Fresh units reinforce it in batches.
/// Enemies near the base pull the pool and nearby army units into defense; expansions are defended by the pool or a
/// sortie of the assembling army. Hard also raids weakly defended extractors and snipes exposed commanders. Air:
/// interceptors patrol the base, bombers raid eco. Scouting: <see cref="AiScouting"/>.
/// </summary>
public sealed class AiMilitary
{
    private readonly AiBrain _b;
    public readonly List<AiGroup> Groups = new();
    private int _nextGroupId = 1;
    /// <summary>Attacks launched (the army leaving its staging point, raids).</summary>
    public int WavesLaunched;
    private readonly List<int> _pool = new();
    private readonly List<int> _tmp = new();
    private readonly List<int> _tmp2 = new();
    private readonly List<int> _first = new();
    private readonly List<int> _second = new();
    private readonly List<(float score, int idx)> _cands = new();
    private int _poolSinceTick = -1;

    // Threat assessment of this think.
    private float _baseThreat;
    /// <summary>Base threat from armed units only (no engineers/scouts): what may pull army units home.</summary>
    private float _baseArmedThreat;
    private int _armyDefendTick = -10000;
    /// <summary>Objectives the army could not reach (stuck): skipped by ChooseObjective until the tick.</summary>
    private readonly List<(Vector2 pos, int until)> _badTargets = new();
    private readonly List<int> _tmp3 = new();
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

    public bool LateGame => _b.GameTime >= Cfg.LateGameTime;

    /// <summary>The main army group, if formed.</summary>
    public AiGroup? Army
    {
        get
        {
            foreach (var g in Groups) if (g.Kind == AiGroupKind.Army) return g;
            return null;
        }
    }

    public void Think()
    {
        ClassifyNewUnits();
        AssessThreats();
        CommanderSafety();
        UpdateGroups();
        Defend();
        ManageArmy();
        ManageSmallGroups();
        ManagePool();
        ManageAir();
        _b.Navy.Think();
        _b.Transport.Think();
        _b.Scouting.Think();
    }

    // ------------------------------------------------------------------ roles

    private void ClassifyNewUnits()
    {
        foreach (int i in V.LandCombat)
        {
            ref var info = ref _b.Info(i);
            if (info.Role != AiRole.None) continue;
            var bp = W.BpOf(i);
            // Mobile shields (no weapons) march with the army; other unarmed units scout.
            info.Role = bp.Has(UnitCategory.Scout) || (bp.Weapons.Length == 0 && !bp.Has(UnitCategory.Shield)) ? AiRole.Scout : AiRole.Pool;
        }
        foreach (int i in V.AirUnits)
        {
            ref var info = ref _b.Info(i);
            if (info.Role != AiRole.None) continue;
            var bp = W.BpOf(i);
            info.Role = bp.Has(UnitCategory.Transport) ? AiRole.Transport
                : bp.Has(UnitCategory.Bomber) ? AiRole.Bomber
                : bp.Weapons.Length == 0 ? AiRole.AirScout
                : AiRole.AirDefense;
        }
    }

    private float StrengthOf(int i) => AiPerception.Strength(W, i, W.BpOf(i));

    // ------------------------------------------------------------------ threats

    private void AssessThreats()
    {
        var u = W.Units;
        int cols = V.ThreatCols, rows = V.ThreatRows;
        if (_ownCells.Length != cols * rows) _ownCells = new bool[cols * rows];
        Array.Clear(_ownCells);
        foreach (int s in V.Structures) _ownCells[V.CellOf(u.Pos[s])] = true;

        _baseThreat = 0; _expThreat = 0; _baseArmedThreat = 0;
        var baseSum = Vector2.Zero;
        float bestExpDist = float.MaxValue;
        float r2 = Cfg.DefenseRadius * Cfg.DefenseRadius;
        foreach (int e in V.EnemyMobile)
        {
            var bp = W.BpOf(e);
            if (bp.Movement.Layer == MotionLayer.Air) continue; // AA/interceptors handle air
            if (bp.Movement.Layer is MotionLayer.Water or MotionLayer.Sub) continue; // naval: not handled by land defense
            float st = _b.Perception.IsIdentified(e) ? StrengthOf(e) : 50f;
            if (st <= 0) st = bp.Has(UnitCategory.Engineer) ? 15f : 0f;
            if (st <= 0) continue;
            var p = u.Pos[e];
            if (Vector2.DistanceSquared(p, _b.BasePos) < r2)
            {
                _baseThreat += st;
                baseSum += p * st;
                if (!bp.HasAny(UnitCategory.Engineer | UnitCategory.Scout)) _baseArmedThreat += st;
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
        if (bestExpDist < float.MaxValue) _expThreat = V.ThreatNowAt(_expThreatPos, 20f);
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
        float threat = V.ThreatNowAt(pos, 26f);
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

    // ------------------------------------------------------------------ groups bookkeeping

    private void UpdateGroups()
    {
        var u = W.Units;
        for (int gi = Groups.Count - 1; gi >= 0; gi--)
        {
            var g = Groups[gi];
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
            g.Strength = 0;
            g.Idle = 0;
            var c = Vector2.Zero;
            foreach (int i in g.Members)
            {
                g.Strength += StrengthOf(i);
                if (u.Orders[i].IsEmpty) g.Idle++;
                c += u.Pos[i];
            }
            g.Centroid = c / g.Members.Count;
            float best = float.MaxValue;
            foreach (int i in g.Members)
            {
                float d = Vector2.DistanceSquared(u.Pos[i], g.Centroid);
                if (d < best) { best = d; g.Anchor = u.Pos[i]; }
            }
            g.Spread = 0;
            foreach (int i in g.Members)
                if (Vector2.DistanceSquared(u.Pos[i], g.Anchor) > 40f * 40f) g.Spread++;
        }
    }

    private AiGroup NewGroup(AiGroupKind kind, List<int> units, AiGroupState state)
    {
        var g = new AiGroup { Id = _nextGroupId++, Kind = kind, State = state, OrderTick = W.Tick, StateTick = W.Tick, CheckTick = W.Tick };
        foreach (int i in units) Join(g, i);
        g.Centroid = Centroid(units);
        g.Anchor = units.Count > 0 ? W.Units.Pos[units[0]] : g.Centroid;
        g.LastCentroid = g.Centroid;
        foreach (int i in units) g.Strength += StrengthOf(i);
        Groups.Add(g);
        return g;
    }

    private void Join(AiGroup g, int i)
    {
        ref var info = ref _b.Info(i);
        info.Role = AiRole.Attack;
        info.Group = g.Id;
        g.Members.Add(i);
    }

    private void Release(AiGroup g, bool goHome)
    {
        foreach (int i in g.Members)
        {
            ref var info = ref _b.Info(i);
            info.Role = AiRole.Pool;
            info.Group = -1;
        }
        if (goHome) _b.Cmd.Order(g.Members, Order.MoveTo(_b.Rally));
        Groups.Remove(g);
    }

    private Vector2 Centroid(List<int> units)
    {
        var u = W.Units;
        var c = Vector2.Zero;
        foreach (int i in units) c += u.Pos[i];
        return units.Count > 0 ? c / units.Count : _b.Rally;
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
        // Who responds: the pool and defenders.
        _tmp.Clear();
        float strength = 0;
        foreach (int i in V.LandCombat)
        {
            ref var info = ref _b.Info(i);
            if (info.Role is not (AiRole.Pool or AiRole.Defend)) continue;
            _tmp.Add(i);
            strength += StrengthOf(i);
        }
        // A real attack on the base the pool can't handle: army members still close to home help, nearest first,
        // without leaving the army (it doesn't dissolve over a few raiders).
        _tmp3.Clear();
        var army = Army;
        if (baseAttack && army != null && _baseArmedThreat >= 150f && strength < threat * 1.5f)
        {
            float near2 = (Cfg.DefenseRadius + 45f) * (Cfg.DefenseRadius + 45f);
            _cands.Clear();
            foreach (int i in army.Members)
            {
                float d2 = Vector2.DistanceSquared(u.Pos[i], target);
                if (Vector2.DistanceSquared(u.Pos[i], _b.BasePos) < near2) _cands.Add((d2, i));
            }
            _cands.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : a.idx.CompareTo(b.idx));
            float have = strength;
            foreach (var (_, i) in _cands)
            {
                if (have >= threat * 1.5f) break;
                _tmp3.Add(i);
                have += StrengthOf(i);
            }
            if (_tmp3.Count > 0) _armyDefendTick = W.Tick;
            // The base is losing and the army is away: bring it home (unless it is finishing the enemy off).
            if (_tmp3.Count == 0 && _baseArmedThreat >= 300f && strength * 1.3f < threat && W.Tick - _recallTick > 300
                && army.Strength > threat * 0.5f && !FinishingEnemy(army))
            {
                _recallTick = W.Tick;
                StartAttack(army, _b.FindStandable(target, MotionLayer.Land), UnitHandle.None, $"recall threat={threat:F0}", countWave: false);
            }
        }
        if (_tmp.Count == 0 && _tmp3.Count == 0) return;
        // Expansions: only fight when we clearly win (the base is what matters).
        if (expAttack && strength < threat * 1.2f) return;

        bool reissue = Vector2.Distance(target, _defendPos) > 10f || W.Tick - _defendTick > 80;
        _tmp2.Clear();
        foreach (int i in _tmp)
        {
            ref var info = ref _b.Info(i);
            bool fresh = info.Role != AiRole.Defend;
            info.Role = AiRole.Defend;
            info.Group = -1;
            if (fresh || reissue || u.Orders[i].IsEmpty) _tmp2.Add(i);
        }
        if (reissue) _tmp2.AddRange(_tmp3);
        if (_tmp2.Count == 0) return;
        _b.Cmd.Order(_tmp2, Order.AttackMoveTo(_b.FindStandable(target, MotionLayer.Land)));
        _defendPos = target;
        _defendTick = W.Tick;
    }

    private int _recallTick = -10000;

    /// <summary>The army is in an enemy base in the late game, or next to an exposed enemy commander: don't recall it.</summary>
    private bool FinishingEnemy(AiGroup g)
    {
        if (g.State != AiGroupState.Attack) return false;
        var p = _b.Perception;
        for (int a = 0; a < p.EnemyCommanderPos.Length; a++)
            if (p.EnemyCommanderPos[a] is Vector2 cp && p.CommanderAge(a) < 100 && Vector2.Distance(cp, g.Anchor) < 45f) return true;
        if (!LateGame) return false;
        foreach (var es in _b.EnemyStarts)
            if (Vector2.Distance(es, g.Anchor) < 55f) return true;
        return false;
    }

    // ------------------------------------------------------------------ the army

    /// <summary>Minimum strength of an attack: grows with each attack launched (like growing waves).</summary>
    private float MinAttackStrength()
    {
        int n = Math.Min(Cfg.MaxWave, Cfg.FirstWave + Cfg.WaveGrowth * WavesLaunched);
        return n * 45f;
    }

    /// <summary>Strength the army wants before attacking an objective: margin × (known defenses and remembered
    /// units around it + a share of the rest of the estimated enemy army, which may come to help).</summary>
    public float RequiredStrength(Vector2 target)
    {
        float local = V.ThreatAt(target, 32f);
        float rest = MathF.Max(0, _b.Perception.EnemyLandEstimate - local);
        return MathF.Max(MinAttackStrength(), Cfg.AttackMargin * (local + Cfg.EnemyArmyShare * rest));
    }

    private void ManageArmy()
    {
        var g = Army;
        if (g == null) return;
        var u = W.Units;
        int n = g.Members.Count;
        switch (g.State)
        {
            case AiGroupState.Assemble:
            {
                if (W.Tick >= g.NextObjectiveTick || (!g.TargetUnit.IsNone && !TargetStillKnown(g.TargetUnit)))
                {
                    g.NextObjectiveTick = W.Tick + 100;
                    if (g.Regroup) { /* keep the objective while regrouping */ }
                    else if (ChooseObjective(g.Anchor, g.Strength, g, out var tpos, out var th))
                    {
                        bool moved = Vector2.Distance(tpos, g.Target) > 8f;
                        g.Target = tpos;
                        g.TargetUnit = th;
                        if (moved || g.Staging == default) SetStaging(g, StagingFor(tpos, g.Anchor, g.Strength));
                    }
                    else if (g.Staging == default) SetStaging(g, _b.Rally);
                }
                // Expansion under attack and the army can take it: sortie.
                if (_expThreat > 0 && Vector2.Distance(g.Centroid, _expThreatPos) < 90f && g.Strength > _expThreat * 1.3f)
                {
                    StartAttack(g, _expThreatPos, UnitHandle.None, "sortie", countWave: false);
                    break;
                }
                // Members that drifted away or idle far from the staging point walk (back) to it.
                _tmp.Clear();
                int gathered = 0;
                foreach (int i in g.Members)
                {
                    float d = Vector2.Distance(u.Pos[i], g.Staging);
                    if (d < 20f) gathered++;
                    else if (u.Orders[i].IsEmpty) _tmp.Add(i);
                }
                if (_tmp.Count > 0) _b.Cmd.Order(_tmp, Order.AttackMoveTo(g.Staging));
                Engage(g, 30f, g.Staging);
                if (Cfg.FocusFire) FocusFire(g, g.Staging);
                float elapsed = (W.Tick - g.StateTick) * SimWorld.Dt;
                if (W.Tick - _armyDefendTick < 60) break; // part of the army is defending the base
                if (g.Regroup)
                {
                    // Resume the attack once the bulk caught up (or after a short wait).
                    if (gathered >= n * 3 / 4 || elapsed > 35f) StartAttack(g, g.Target, g.TargetUnit, $"regrouped {gathered}/{n}", countWave: false);
                    break;
                }
                bool ready = gathered >= n * 3 / 4 || (elapsed > 45f && gathered >= n / 2);
                float required = RequiredStrength(g.Target);
                float timeout = LateGame ? Cfg.AssembleTimeout * 0.5f : Cfg.AssembleTimeout;
                // Enough strength; or big/waited long and not hopeless (late game: go anyway once big).
                bool strong = g.Strength >= required
                              || (n >= Cfg.MaxArmy && (LateGame || g.Strength >= required * 0.7f))
                              || (elapsed > timeout && g.Strength >= MinAttackStrength() * 0.75f && g.Strength >= required * 0.7f);
                if (ready && strong && g.Target != default) StartAttack(g, g.Target, g.TargetUnit, $"req={required:F0} gathered={gathered}/{n} waited={elapsed:F0}s");
                break;
            }
            case AiGroupState.Attack:
                UpdateAttackingGroup(g);
                break;
            case AiGroupState.Retreat:
                if (g.Idle >= n * 3 / 4 || Vector2.Distance(g.Centroid, _b.Rally) < 18f)
                {
                    g.State = AiGroupState.Assemble;
                    g.StateTick = W.Tick;
                    g.NextObjectiveTick = W.Tick;
                    g.Staging = default;
                }
                break;
        }
    }

    private void SetStaging(AiGroup g, Vector2 staging)
    {
        g.Staging = staging;
        g.OrderTick = W.Tick;
        _b.Cmd.Order(g.Members, Order.AttackMoveTo(staging));
    }

    private void StartAttack(AiGroup g, Vector2 target, UnitHandle th, string why, bool countWave = true)
    {
        bool resume = g.Regroup;
        g.Regroup = false;
        g.State = AiGroupState.Attack;
        g.StateTick = W.Tick;
        g.OrderTick = W.Tick;
        g.CheckTick = W.Tick;
        g.StuckThinks = 0;
        g.Reissues = 0;
        g.Target = target;
        g.TargetUnit = th;
        if (!resume) g.InitialStrength = g.Strength;
        g.LastCentroid = g.Centroid;
        if (countWave) WavesLaunched++;
        if (_b.Tracing) _b.Trace($"attack g{g.Id} {g.Kind} n={g.Members.Count} st={g.Strength:F0} from ({g.Centroid.X:F0},{g.Centroid.Y:F0}) -> ({target.X:F0},{target.Y:F0}) {why}");
        _b.Cmd.Order(g.Members, Order.AttackMoveTo(target));
    }

    /// <summary>Gathering point for an attack on target: StageDistance short of it along the walking route from
    /// 'from' (so the army arrives together even after long detours), outside known threat; straight-line fallback.</summary>
    private Vector2 StagingFor(Vector2 target, Vector2 from, float strength)
    {
        if (Cfg.StageDistance <= 0) return target;
        float maxThreat = MathF.Max(300f, strength * 0.4f);
        if (_b.SolveRoute(MotionLayer.Land, 0.6f, from, target, out float len))
        {
            // Walk back along the route until the point is outside the known defenses (up to ~3x StageDistance).
            for (int k = 0; k < 8; k++)
            {
                float d = Cfg.StageDistance + 12f * k;
                if (d >= len - 5f) return from;
                var p = _b.FindStandable(_b.RoutePointBack(d), MotionLayer.Land);
                if (V.ThreatAt(p, 16f) > maxThreat) continue;
                return p;
            }
            return _b.FindStandable(_b.RoutePointBack(MathF.Min(len * 0.5f, Cfg.StageDistance * 3f)), MotionLayer.Land);
        }
        var dir = from - target;
        float dist = dir.Length();
        if (dist < 1f) return from;
        dir /= dist;
        for (int k = 0; k < 5; k++)
        {
            float d = Cfg.StageDistance + 15f * k;
            if (d >= dist - 5f) return from;
            var p = _b.FindStandable(target + dir * d, MotionLayer.Land);
            if (V.ThreatAt(p, 16f) > maxThreat) continue;
            if (!_b.CanReachNear(MotionLayer.Land, 0.6f, from, p, 1f)) continue;
            return p;
        }
        return from;
    }

    private void UpdateAttackingGroup(AiGroup g)
    {
        var u = W.Units;
        int n = g.Members.Count;
        // Micro: badly damaged units go home to be repaired (Hard).
        if (Cfg.PullBackHealth > 0 && n > 1)
        {
            _tmp.Clear();
            foreach (int i in g.Members)
                if (u.Health[i] < W.BpOf(i).MaxHealth * Cfg.PullBackHealth) _tmp.Add(i);
            if (_tmp.Count > 0 && _tmp.Count < n)
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
                n = g.Members.Count;
            }
        }

        float local = V.ThreatNowAt(g.Anchor, 24f);
        // Retreat when beaten (not in the late game: then the AI plays to finish).
        float retreatFraction = LateGame ? Cfg.RetreatFraction * 0.4f : Cfg.RetreatFraction;
        if (retreatFraction > 0 && g.Strength < g.InitialStrength * retreatFraction && local > g.Strength * 1.2f)
        {
            if (_b.Tracing) _b.Trace($"retreat g{g.Id} {g.Kind} n={n} st={g.Strength:F0}/{g.InitialStrength:F0} local={local:F0} at ({g.Centroid.X:F0},{g.Centroid.Y:F0})");
            g.State = AiGroupState.Retreat;
            g.StateTick = W.Tick;
            _b.Cmd.Order(g.Members, Order.MoveTo(_b.Rally));
            g.OrderTick = W.Tick;
            return;
        }

        if (Cfg.CommanderSnipe || LateGame) TrySnipe(g);

        // Strung out along the route (long ramps, stragglers) while nobody is fighting: the front waits for the rest.
        if (g.Kind == AiGroupKind.Army && n >= 6 && g.Spread * 3 > n && W.Tick - g.RegroupTick > 600 && W.Tick - g.OrderTick > 100
            && local <= 0)
        {
            var front = g.Anchor;
            float bestFront = float.MaxValue;
            foreach (int i in g.Members)
            {
                float d = Vector2.DistanceSquared(u.Pos[i], g.Target);
                if (d < bestFront) { bestFront = d; front = u.Pos[i]; }
            }
            if (V.ThreatNowAt(front, 30f) <= 0)
            {
                if (_b.Tracing) _b.Trace($"regroup g{g.Id} n={n} spread={g.Spread} at ({g.Anchor.X:F0},{g.Anchor.Y:F0})");
                g.RegroupTick = W.Tick;
                g.Regroup = true;
                g.State = AiGroupState.Assemble;
                g.StateTick = W.Tick;
                SetStaging(g, _b.FindStandable(g.Anchor, MotionLayer.Land));
                return;
            }
        }

        Engage(g, 45f, g.Target);
        if (Cfg.FocusFire && local > 0) FocusFire(g, g.Target);

        // Position targets (sorties, last known positions) are done when the group idles there, or after a while
        // when it dominates locally: a stream of fresh enemies must not pin the army there for minutes.
        bool targetGone = !g.TargetUnit.IsNone ? !TargetStillKnown(g.TargetUnit)
            : (g.Idle >= Math.Max(1, n / 2) && W.Tick - g.OrderTick > 50)
              || (Cfg.PositionTargetTime > 0 && W.Tick - g.OrderTick > Cfg.PositionTargetTime * SimWorld.TicksPerSecond
                  && local < g.Strength * 0.5f);
        // Stuck: no progress over 10 s windows while nothing is shooting at us.
        bool stuck = false;
        if (W.Tick - g.OrderTick > 300 && W.Tick - g.CheckTick >= 100)
        {
            if (Vector2.Distance(g.Centroid, g.LastCentroid) < 5f && local <= 0) g.StuckThinks++;
            else g.StuckThinks = 0;
            g.LastCentroid = g.Centroid;
            g.CheckTick = W.Tick;
            stuck = g.StuckThinks >= 3;
        }
        if (stuck && !targetGone && g.Reissues < 1)
        {
            g.Reissues++;
            g.StuckThinks = 0;
            g.OrderTick = W.Tick;
            if (_b.Tracing) _b.Trace($"reissue g{g.Id} stuck at ({g.Centroid.X:F0},{g.Centroid.Y:F0}) -> ({g.Target.X:F0},{g.Target.Y:F0})");
            _b.Cmd.Order(g.Members, Order.AttackMoveTo(g.Target));
            return;
        }
        if (!targetGone && !stuck) return;

        if (g.Kind == AiGroupKind.Clear) { Release(g, goHome: true); return; }
        if (stuck) _badTargets.Add((g.Target, W.Tick + 1500)); // can't get there: pick something else for a while
        bool found = g.Kind == AiGroupKind.Raid
            ? ChooseRaidTarget(g.Centroid, g.Strength, out var tpos, out var th)
            : ChooseObjective(g.Centroid, g.Strength, g, out tpos, out th);
        if (!found)
        {
            if (g.Kind == AiGroupKind.Raid) { Release(g, goHome: true); return; }
            g.State = AiGroupState.Retreat;
            g.StateTick = W.Tick;
            g.OrderTick = W.Tick;
            _b.Cmd.Order(g.Members, Order.MoveTo(_b.Rally));
            return;
        }
        if (_b.Tracing) _b.Trace($"next g{g.Id} {(targetGone ? "gone" : "stuck")} at ({g.Centroid.X:F0},{g.Centroid.Y:F0}) -> ({tpos.X:F0},{tpos.Y:F0})");
        float dist = Vector2.Distance(g.Centroid, tpos);
        // Close by, or clearly stronger and not too far: go on. Otherwise stage again near the next objective (also in
        // the late game: a long march straight into the enemy base strings the army out and loses it).
        bool keepGoing = g.Kind != AiGroupKind.Army || dist < 70f || Cfg.StageDistance <= 0
                         || (dist < 140f && g.Strength >= RequiredStrength(tpos) * (LateGame ? 0.8f : 1.1f));
        if (keepGoing)
        {
            g.Target = tpos;
            g.TargetUnit = th;
            g.OrderTick = W.Tick;
            g.StuckThinks = 0;
            g.Reissues = 0;
            _b.Cmd.Order(g.Members, Order.AttackMoveTo(tpos));
            return;
        }
        // Far away and not clearly stronger: gather again near the new objective.
        g.State = AiGroupState.Assemble;
        g.StateTick = W.Tick;
        g.Target = tpos;
        g.TargetUnit = th;
        g.NextObjectiveTick = W.Tick + 100;
        SetStaging(g, StagingFor(tpos, g.Anchor, g.Strength));
    }

    /// <summary>Idle members while known enemies are close (outranged by artillery, attack-move finished short of
    /// them): attack-move at the nearest one, then back to 'then'.</summary>
    private void Engage(AiGroup g, float radius, Vector2 then)
    {
        if (g.Idle == 0 || W.Tick - g.EngageTick < 30) return;
        if (!NearestKnownEnemy(g.Anchor, radius, out var ep)) return;
        var u = W.Units;
        _tmp.Clear();
        foreach (int i in g.Members) if (u.Orders[i].IsEmpty) _tmp.Add(i);
        if (_tmp.Count == 0) return;
        g.EngageTick = W.Tick;
        _b.Cmd.Order(_tmp, Order.AttackMoveTo(ep));
        if (then != default) _b.Cmd.Order(_tmp, Order.AttackMoveTo(then), queue: true);
    }

    private readonly List<int> _focusEnemies = new();
    private float[] _focusLeft = [];
    private Vector2[] _focusPos = [];
    private readonly List<(int target, int unit)> _focusPairs = new();

    /// <summary>Hard micro (<see cref="AiConfig.FocusFire"/>): every direct-fire member picks, among the identified
    /// enemy land units in its weapon range, the one with the least health left after the damage already assigned to
    /// it (FocusHorizon seconds of each shooter's damage), so the group kills units one after the other instead of
    /// spreading its fire. Only members whose weapon is not already on their pick get an order (attack it, then
    /// attack-move on to 'then').</summary>
    private void FocusFire(AiGroup g, Vector2 then)
    {
        var u = W.Units;
        _focusEnemies.Clear();
        const float near = 60f;
        foreach (int e in V.EnemyMobile)
        {
            if (!_b.Perception.IsIdentified(e)) continue;
            if (W.BpOf(e).Movement.Layer is MotionLayer.Air or MotionLayer.Water or MotionLayer.Sub) continue;
            if (Vector2.DistanceSquared(u.Pos[e], g.Anchor) < near * near) _focusEnemies.Add(e);
        }
        if (_focusEnemies.Count == 0) return;
        if (_focusLeft.Length < _focusEnemies.Count)
        {
            _focusLeft = new float[_focusEnemies.Count * 2];
            _focusPos = new Vector2[_focusEnemies.Count * 2];
        }
        int ne = _focusEnemies.Count;
        for (int k = 0; k < ne; k++)
        {
            _focusLeft[k] = u.Health[_focusEnemies[k]];
            _focusPos[k] = u.Pos[_focusEnemies[k]];
        }
        _focusPairs.Clear();
        foreach (int i in g.Members)
        {
            var bp = W.BpOf(i);
            if (bp.Weapons.Length == 0 || bp.HasAny(UnitCategory.IndirectFire | UnitCategory.AntiAir)) continue;
            var wb = bp.Weapons[0];
            if (wb.Ballistic || (wb.TargetMask & LayerMask.Land) == 0) continue;
            float dmg = wb.Damage * wb.ProjectilesPerShot * wb.RateOfFire * Cfg.FocusHorizon;
            int best = PickFocusTarget(u.Pos[i], wb.Range + 1f, _focusPos.AsSpan(0, ne), _focusLeft.AsSpan(0, ne), dmg);
            if (best < 0) continue;
            var th = u.HandleOf(_focusEnemies[best]);
            if (u.Weapons[i * UnitStore.MaxWeapons].Target == th) continue;
            _focusPairs.Add((_focusEnemies[best], i));
        }
        if (_focusPairs.Count == 0) return;
        _focusPairs.Sort((a, b) => a.target != b.target ? a.target.CompareTo(b.target) : a.unit.CompareTo(b.unit));
        for (int s = 0; s < _focusPairs.Count;)
        {
            int t = _focusPairs[s].target;
            _tmp.Clear();
            while (s < _focusPairs.Count && _focusPairs[s].target == t) _tmp.Add(_focusPairs[s++].unit);
            _b.Cmd.Order(_tmp, Order.AttackUnit(u.HandleOf(t)));
            if (then != default) _b.Cmd.Order(_tmp, Order.AttackMoveTo(then), queue: true);
        }
    }

    /// <summary>Focus-fire choice of one shooter at 'shooter' with weapon range 'range': among the enemies in range,
    /// the one with the least health left (healthLeft = health minus the damage already assigned; saturated targets,
    /// left &lt;= 0, only when nothing else is in range), nearest on ties. Assigns 'damage' to it. -1 if none in range.</summary>
    public static int PickFocusTarget(Vector2 shooter, float range, ReadOnlySpan<Vector2> enemyPos, Span<float> healthLeft, float damage)
    {
        int best = -1;
        float bestLeft = float.MaxValue, bestD = float.MaxValue, r2 = range * range;
        for (int k = 0; k < enemyPos.Length; k++)
        {
            float d = Vector2.DistanceSquared(shooter, enemyPos[k]);
            if (d > r2) continue;
            float left = healthLeft[k] > 0 ? healthLeft[k] : 1e6f + healthLeft[k];
            if (left < bestLeft || (left == bestLeft && d < bestD)) { best = k; bestLeft = left; bestD = d; }
        }
        if (best >= 0) healthLeft[best] -= damage;
        return best;
    }

    /// <summary>Nearest known (seen or on radar) enemy land unit or structure within radius of p.</summary>
    private bool NearestKnownEnemy(Vector2 p, float radius, out Vector2 pos)
    {
        var u = W.Units;
        pos = default;
        float best = radius * radius;
        foreach (int e in V.EnemyMobile)
        {
            var layer = W.BpOf(e).Movement.Layer;
            if (layer is MotionLayer.Air or MotionLayer.Water or MotionLayer.Sub) continue;
            float d = Vector2.DistanceSquared(u.Pos[e], p);
            if (d < best) { best = d; pos = u.Pos[e]; }
        }
        foreach (int e in V.EnemyStructures)
        {
            if (W.BpOf(e).HasAny(UnitCategory.Naval)) continue;
            float d = Vector2.DistanceSquared(u.Pos[e], p);
            if (d < best) { best = d; pos = u.Pos[e]; }
        }
        if (pos == default) return false;
        pos = _b.FindStandable(pos, MotionLayer.Land);
        return true;
    }

    /// <summary>Hard: when an enemy commander is in sight close to the army and the army can take it, the direct-
    /// fire units focus it (assassination wins the game).</summary>
    private void TrySnipe(AiGroup g)
    {
        if (W.Tick - g.SnipeTick < 50) return;
        var u = W.Units;
        var p = _b.Perception;
        for (int a = 0; a < p.EnemyCommanderPos.Length; a++)
        {
            if (p.CommanderAge(a) > 0 || p.EnemyCommanderPos[a] is not Vector2 cp) continue;
            var h = p.EnemyCommanderHandle[a];
            if (!u.IsActive(h)) continue;
            if (Vector2.Distance(cp, g.Anchor) > Cfg.SnipeRadius) continue;
            float hp = u.Health[h.Index] / MathF.Max(1f, W.BpOf(h.Index).MaxHealth);
            float defense = V.ThreatNowAt(cp, 20f);
            if (g.Strength < defense * 0.7f && hp > 0.5f) continue;
            _tmp.Clear();
            foreach (int i in g.Members)
            {
                var bp = W.BpOf(i);
                if (bp.HasAny(UnitCategory.IndirectFire | UnitCategory.AntiAir) || bp.Weapons.Length == 0) continue;
                if (Vector2.Distance(u.Pos[i], cp) < Cfg.SnipeRadius + 5f) _tmp.Add(i);
            }
            if (_tmp.Count < 3) continue;
            g.SnipeTick = W.Tick;
            if (_b.Tracing) _b.Trace($"snipe g{g.Id} {_tmp.Count} units on commander of army {a} hp={hp:F2}");
            _b.Cmd.Order(_tmp, Order.AttackUnit(h));
            _b.Cmd.Order(_tmp, Order.AttackMoveTo(g.Target), queue: true);
            return;
        }
    }

    /// <summary>
    /// Best objective for a group of the given strength at 'from' among what the AI knows: known enemy structures
    /// (value by type, minus distance, avoiding defenses that outgun the group unless pushing; near the enemy base
    /// and clustered when pushing), the enemy commander's last known position, then unexplored/stale enemy starts.
    /// </summary>
    private bool ChooseObjective(Vector2 from, float strength, AiGroup? g, out Vector2 pos, out UnitHandle handle)
    {
        pos = default;
        handle = UnitHandle.None;
        var db = W.Blueprints;
        var known = _b.Perception.Structures;
        bool push = _b.GameTime > Cfg.PushTime || LateGame || (g != null && g.Members.Count >= Cfg.MaxWave)
                    || strength > 1.5f * (_b.Perception.EnemyLandEstimate + 600f);
        for (int k = _badTargets.Count - 1; k >= 0; k--)
            if (_badTargets[k].until <= W.Tick) _badTargets.RemoveAt(k);
        _cands.Clear();
        for (int k = 0; k < known.Count; k++)
        {
            var ks = known[k];
            var bp = db[ks.Bp];
            if (bp.HasAny(UnitCategory.Naval) || bp.Movement.Layer is MotionLayer.Water or MotionLayer.Sub) continue;
            if (IsBadTarget(ks.Pos)) continue;
            float value = bp.Has(UnitCategory.Factory) ? 35f
                : bp.Has(UnitCategory.MassExtractor) ? 30f
                : bp.HasAny(UnitCategory.PowerGenerator | UnitCategory.MassFabricator) ? 20f
                : bp.Has(UnitCategory.Defense) ? 5f : 10f;
            float d = Vector2.Distance(from, ks.Pos);
            float score = value - d * 0.25f;
            float defense = V.ThreatAt(ks.Pos, 24f);
            if (defense > strength * 0.8f && !push) score -= 40f + defense / 50f;
            if (!push)
            {
                // Before the push the enemy base (commander, fresh units, defenses) is a bad trade: take the map.
                foreach (var es in _b.EnemyStarts)
                    if (Vector2.DistanceSquared(es, ks.Pos) < 50f * 50f) { score -= 30f; break; }
            }
            else
            {
                foreach (var es in _b.EnemyStarts)
                    if (Vector2.DistanceSquared(es, ks.Pos) < 45f * 45f) { score += 60f; break; }
                int cluster = 0;
                for (int j = 0; j < known.Count && cluster < 10; j++)
                    if (j != k && Vector2.DistanceSquared(known[j].Pos, ks.Pos) < 25f * 25f) cluster++;
                score += cluster * 4f;
            }
            if (g != null && !g.TargetUnit.IsNone && ks.Army == ArmyOf(g.TargetUnit)) score += 10f;
            _cands.Add((-score, k));
        }
        _cands.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : a.idx.CompareTo(b.idx));
        int checks = 0;
        foreach (var (_, k) in _cands)
        {
            if (checks++ >= 12) break;
            var ks = known[k];
            if (!_b.CanReachNear(MotionLayer.Land, 0.6f, from, ks.Pos, ConstructionHalf(db[ks.Bp]))) continue;
            pos = ks.Pos;
            handle = ks.Handle;
            return true;
        }
        // Nothing known (or reachable): the enemy commander's last known position if we can take it.
        for (int a = 0; a < _b.Perception.EnemyCommanderPos.Length; a++)
        {
            if (_b.Perception.EnemyCommanderPos[a] is not Vector2 cp || _b.Perception.CommanderAge(a) > 1800 || IsBadTarget(cp)) continue;
            if (!push && V.ThreatAt(cp, 24f) > strength * 0.8f) continue;
            if (!_b.CanReachNear(MotionLayer.Land, 0.6f, from, cp, 2f)) continue;
            pos = cp;
            return true;
        }
        // Scout by force: the nearest enemy start that is unexplored or stale.
        float best = float.MaxValue;
        bool found = false;
        foreach (var es in _b.EnemyStarts)
        {
            if (!EnemyAliveNear(es) || IsBadTarget(es)) continue;
            if (_b.Perception.Staleness(V, es) < 600) continue; // seen recently and nothing there
            float d = Vector2.DistanceSquared(es, from);
            if (d >= best) continue;
            if (!_b.CanReachNear(MotionLayer.Land, 0.6f, from, es, 2f)) continue;
            best = d;
            pos = es;
            found = true;
        }
        return found;
    }

    private bool IsBadTarget(Vector2 p)
    {
        foreach (var (bp, _) in _badTargets)
            if (Vector2.DistanceSquared(bp, p) < 12f * 12f) return true;
        return false;
    }

    /// <summary>Is any not-defeated enemy army starting at this start position (start positions are known)?</summary>
    private bool EnemyAliveNear(Vector2 start)
    {
        var starts = W.Terrain.StartPositions;
        for (int a = 0; a < W.Armies.Length; a++)
        {
            if (!W.IsEnemy(_b.Army, a) || W.Armies[a].Defeated) continue;
            if (starts.Length > 0 && Vector2.DistanceSquared(starts[a % starts.Length], start) < 4f) return true;
        }
        return false;
    }

    private int ArmyOf(UnitHandle h)
    {
        foreach (var ks in _b.Perception.Structures)
            if (ks.Handle == h) return ks.Army;
        return -1;
    }

    /// <summary>Raid targets: known enemy extractors (and power) with little known defense, away from enemy bases.</summary>
    private bool ChooseRaidTarget(Vector2 from, float strength, out Vector2 pos, out UnitHandle handle)
    {
        pos = default;
        handle = UnitHandle.None;
        var db = W.Blueprints;
        var known = _b.Perception.Structures;
        float bestScore = float.MaxValue;
        int best = -1;
        for (int k = 0; k < known.Count; k++)
        {
            var ks = known[k];
            var bp = db[ks.Bp];
            if (!bp.HasAny(UnitCategory.MassExtractor | UnitCategory.PowerGenerator) || bp.HasAny(UnitCategory.Naval)) continue;
            bool nearBase = false;
            foreach (var es in _b.EnemyStarts)
                if (Vector2.DistanceSquared(es, ks.Pos) < 50f * 50f) { nearBase = true; break; }
            if (nearBase) continue;
            if (V.ThreatAt(ks.Pos, 20f) > strength * 0.4f) continue;
            float score = Vector2.Distance(from, ks.Pos) - (bp.Has(UnitCategory.MassExtractor) ? 20f : 0f);
            if (score >= bestScore) continue;
            if (!_b.CanReachNear(MotionLayer.Land, 0.6f, from, ks.Pos, ConstructionHalf(bp))) continue;
            best = k;
            bestScore = score;
        }
        if (best < 0) return false;
        pos = known[best].Pos;
        handle = known[best].Handle;
        return true;
    }

    private static float ConstructionHalf(UnitBlueprint bp) => MathF.Max(bp.FootprintX, bp.FootprintZ) * 0.5f;

    private bool TargetStillKnown(UnitHandle h)
    {
        if (_b.Perception.IsStructureKnown(h)) return true;
        return W.Units.IsActive(h) && !W.BpOf(h.Index).IsStructure && _b.Perception.IsKnown(h.Index);
    }

    // ------------------------------------------------------------------ raids and clearing jobs

    private void ManageSmallGroups()
    {
        for (int gi = Groups.Count - 1; gi >= 0; gi--)
        {
            var g = Groups[gi];
            if (g.Kind == AiGroupKind.Army) continue;
            if (g.Kind == AiGroupKind.Reinforce) { UpdateReinforcements(g); continue; }
            if (g.State == AiGroupState.Retreat)
            {
                if (g.Idle >= g.Members.Count * 3 / 4 || Vector2.Distance(g.Centroid, _b.Rally) < 16f) Release(g, goHome: false);
                continue;
            }
            // Raids run from anything that outguns them.
            if (g.Kind == AiGroupKind.Raid && V.ThreatNowAt(g.Centroid, 20f) > g.Strength * 1.2f)
            {
                g.State = AiGroupState.Retreat;
                _b.Cmd.Order(g.Members, Order.MoveTo(_b.Rally));
                continue;
            }
            UpdateAttackingGroup(g);
        }
    }

    /// <summary>Where reinforcements walk: the staging point while the army gathers; while it attacks, the staging
    /// point (on the army's route, behind it) when it lies on the way, then the objective. Fixed points, so batches
    /// don't zig-zag after a moving army.</summary>
    private void SendReinforcements(AiGroup army, List<int> units)
    {
        if (army.State == AiGroupState.Assemble && army.Staging != default)
        {
            _b.Cmd.Order(units, Order.AttackMoveTo(army.Staging));
            return;
        }
        var target = army.Target != default ? army.Target : army.Anchor;
        bool viaStaging = army.Staging != default
                          && Vector2.Distance(army.Staging, target) < Vector2.Distance(_b.Rally, target) - 10f;
        if (viaStaging)
        {
            _b.Cmd.Order(units, Order.AttackMoveTo(army.Staging));
            _b.Cmd.Order(units, Order.AttackMoveTo(target), queue: true);
        }
        else _b.Cmd.Order(units, Order.AttackMoveTo(target));
    }

    private static Vector2 ReinforceGoal(AiGroup army) =>
        army.State == AiGroupState.Assemble && army.Staging != default ? army.Staging : army.Target;

    /// <summary>A reinforcement batch walks to the army and merges when it reaches it (or arrives and idles).</summary>
    private void UpdateReinforcements(AiGroup g)
    {
        var army = Army;
        if (army == null || army.State == AiGroupState.Retreat) { Release(g, goHome: true); return; }
        bool arrived = g.Idle * 2 >= g.Members.Count && W.Tick - g.OrderTick > 30;
        if (Vector2.Distance(g.Anchor, army.Anchor) < 35f || arrived)
        {
            foreach (int i in g.Members)
            {
                _b.Info(i).Group = army.Id;
                army.Members.Add(i);
            }
            army.Strength += g.Strength;
            if (army.State == AiGroupState.Attack) army.InitialStrength += g.Strength;
            if (arrived) SendReinforcements(army, g.Members);
            Groups.Remove(g);
            return;
        }
        var goal = ReinforceGoal(army);
        if (W.Tick - g.OrderTick >= 50 && Vector2.Distance(goal, g.Target) > 15f)
        {
            g.Target = goal;
            g.OrderTick = W.Tick;
            SendReinforcements(army, g.Members);
        }
    }

    // ------------------------------------------------------------------ pool

    private int _nextClearTick, _nextRaidTick;

    private void ManagePool()
    {
        var u = W.Units;
        _pool.Clear();
        _tmp.Clear();
        foreach (int i in V.LandCombat)
        {
            ref var info = ref _b.Info(i);
            if (info.Role != AiRole.Pool) continue;
            _pool.Add(i);
            if (u.Orders[i].IsEmpty && Vector2.DistanceSquared(u.Pos[i], _b.Rally) > 14f * 14f && info.HoldUntil <= W.Tick) _tmp.Add(i);
        }
        if (_tmp.Count > 0)
        {
            _b.Cmd.Order(_tmp, Order.MoveTo(_b.Rally));
            foreach (int i in _tmp) _b.Info(i).HoldUntil = W.Tick + 50;
        }
        if (_pool.Count == 0) { _poolSinceTick = -1; return; }
        if (_poolSinceTick < 0) _poolSinceTick = W.Tick;
        if (_baseThreat > 0) return;

        // Units gathered at the rally (the ones that can leave together).
        _tmp.Clear();
        float gatheredStrength = 0;
        foreach (int i in _pool)
        {
            if (Vector2.DistanceSquared(u.Pos[i], _b.Rally) > 30f * 30f) continue;
            _tmp.Add(i);
            gatheredStrength += StrengthOf(i);
        }

        if (TryRaid()) return;

        var army = Army;
        if (army == null)
        {
            bool big = _pool.Count >= Cfg.MaxWave;
            if ((_b.GameTime >= Cfg.FirstAttackTime || big) && _tmp.Count >= Cfg.FirstWave)
            {
                var g = NewGroup(AiGroupKind.Army, _tmp, AiGroupState.Assemble);
                g.NextObjectiveTick = W.Tick;
                g.Staging = default;
                if (_b.Tracing) _b.Trace($"army g{g.Id} formed n={g.Members.Count} st={g.Strength:F0}");
                _poolSinceTick = -1;
                ManageArmy(); // pick an objective and start walking right away
                return;
            }
            ClearOwnDeposits(gatheredStrength);
            return;
        }

        // Reinforce the army in batches (always while it assembles, while it attacks if configured). Batches walk
        // together as a Reinforce group and merge when they reach the army, so they don't trickle in one by one.
        if (_tmp.Count == 0) return;
        if (army.State == AiGroupState.Retreat) return;
        if (army.State == AiGroupState.Attack && !Cfg.Reinforce) return;
        if (army.State == AiGroupState.Assemble && Vector2.Distance(army.Anchor, _b.Rally) < 40f)
        {
            // The army is at home: join right away.
            foreach (int i in _tmp) Join(army, i);
            SendReinforcements(army, _tmp);
            _poolSinceTick = -1;
            return;
        }
        int batch = army.State == AiGroupState.Attack
            ? Math.Max(Cfg.ReinforceBatch, (int)(army.Members.Count * Cfg.ReinforceBatchFraction)) : 4;
        bool waitedEnough = W.Tick - _poolSinceTick > (army.State == AiGroupState.Attack ? Cfg.ReinforceWait * 10 : 250);
        if (_tmp.Count < batch && !waitedEnough) return;
        // A batch that has only just left takes the newcomers along; otherwise a new batch leaves.
        AiGroup? rgrp = null;
        foreach (var rg in Groups)
            if (rg.Kind == AiGroupKind.Reinforce && Vector2.Distance(rg.Anchor, _b.Rally) < 30f) { rgrp = rg; break; }
        if (rgrp != null) foreach (int i in _tmp) Join(rgrp, i);
        else
        {
            rgrp = NewGroup(AiGroupKind.Reinforce, _tmp, AiGroupState.Attack);
            rgrp.Target = ReinforceGoal(army);
            rgrp.OrderTick = W.Tick;
        }
        SendReinforcements(army, _tmp);
        _poolSinceTick = -1;
    }

    /// <summary>Hard: a small raiding party of tanks hits a weakly defended enemy extractor.</summary>
    private bool TryRaid()
    {
        if (Cfg.RaidSize <= 0 || W.Tick < _nextRaidTick || _b.GameTime < Cfg.FirstAttackTime) return false;
        int raids = 0;
        foreach (var rg in Groups) if (rg.Kind == AiGroupKind.Raid) raids++;
        if (raids >= Cfg.MaxRaids) return false;
        _nextRaidTick = W.Tick + 150;
        _tmp2.Clear();
        float st = 0;
        foreach (int i in _tmp)
        {
            var bp = W.BpOf(i);
            if (!bp.Has(UnitCategory.DirectFire)) continue;
            _tmp2.Add(i);
            st += StrengthOf(i);
            if (_tmp2.Count >= Cfg.RaidSize) break;
        }
        if (_tmp2.Count < Cfg.RaidSize) return false;
        if (!ChooseRaidTarget(_b.Rally, st, out var tpos, out var th)) return false;
        var g = NewGroup(AiGroupKind.Raid, _tmp2, AiGroupState.Attack);
        StartAttack(g, tpos, th, "raid");
        foreach (int i in _tmp2) _tmp.Remove(i);
        return true;
    }

    /// <summary>
    /// Enemies sitting on free mass deposits on our side of the map block expansion: when the pool clearly outguns
    /// them, send it there (attack-move) and bring it back afterwards.
    /// </summary>
    private void ClearOwnDeposits(float poolStrength)
    {
        if (W.Tick < _nextClearTick || _tmp.Count < 4 || poolStrength <= 0) return;
        _nextClearTick = W.Tick + 200;
        foreach (var g0 in Groups) if (g0.Kind == AiGroupKind.Clear) return; // one clearing job at a time
        var u = W.Units;
        var deps = W.Terrain.Deposits;
        int best = -1;
        float bestD = float.MaxValue;
        for (int d = 0; d < deps.Length; d++)
        {
            var p = deps[d].Pos;
            float dHome = Vector2.Distance(p, _b.BasePos);
            if (dHome >= bestD) continue;
            bool ourSide = true;
            foreach (var es in _b.EnemyStarts)
                if (Vector2.Distance(p, es) * 0.75f < dHome) { ourSide = false; break; } // clearly closer to us
            if (!ourSide) continue;
            int occ = W.Occupancy.At((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));
            if (occ >= 0 && u.Army[occ] == _b.Army) continue; // ours already
            float t = V.ThreatNowAt(p, 16f);
            bool enemyStructure = occ >= 0 && W.IsEnemy(_b.Army, u.Army[occ]) && _b.Perception.IsStructureKnown(u.HandleOf(occ));
            if ((t <= 0 && !enemyStructure) || t * 1.5f > poolStrength) continue;
            if (!_b.CanReachNear(MotionLayer.Land, 0.6f, _b.Rally, p, 2f)) continue;
            best = d;
            bestD = dHome;
        }
        if (best < 0) return;
        var target = _b.FindStandable(deps[best].Pos, MotionLayer.Land);
        var g = NewGroup(AiGroupKind.Clear, _tmp, AiGroupState.Attack);
        g.Target = target;
        g.InitialStrength = poolStrength;
        _b.Cmd.Order(g.Members, Order.AttackMoveTo(target));
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
        if (_tmp.Count >= Cfg.BomberGroup && ChooseBomberTarget(_b.BasePos, out var tpos, out var th))
        {
            // In sight: attack it; a remembered (ghost) target: fly there on attack-move and bomb what is found.
            if (u.IsActive(th) && _b.Perception.IsIdentified(th.Index)) _b.Cmd.Order(_tmp, Order.AttackUnit(th));
            else _b.Cmd.Order(_tmp, Order.AttackMoveTo(tpos));
            _b.Cmd.Order(_tmp, Order.MoveTo(_b.AirRally), queue: true);
        }
    }

    /// <summary>Known enemy eco structure (ghost or in sight) or engineer in sight with the least anti-air around it,
    /// then the closest.</summary>
    private bool ChooseBomberTarget(Vector2 from, out Vector2 pos, out UnitHandle handle)
    {
        var u = W.Units;
        pos = default;
        handle = UnitHandle.None;
        float bestScore = float.MaxValue;
        bool found = false;
        foreach (var ks in _b.Perception.Structures)
        {
            var bp = W.Blueprints[ks.Bp];
            float bonus = bp.Has(UnitCategory.MassExtractor) ? 40f : bp.Has(UnitCategory.PowerGenerator) ? 20f : -1f;
            if (bonus < 0) continue;
            float score = Vector2.Distance(from, ks.Pos) + V.AirThreatAt(ks.Pos, 30f) * 2f - bonus;
            if (score < bestScore) { bestScore = score; pos = ks.Pos; handle = ks.Handle; found = true; }
        }
        foreach (int e in V.EnemyMobile)
        {
            if (!_b.Perception.IsIdentified(e)) continue;
            var bp = W.BpOf(e);
            if (!bp.Has(UnitCategory.Engineer) || bp.Has(UnitCategory.Commander)) continue;
            var p = u.Pos[e];
            float score = Vector2.Distance(from, p) + V.AirThreatAt(p, 30f) * 2f - 30f;
            if (score < bestScore) { bestScore = score; pos = p; handle = u.HandleOf(e); found = true; }
        }
        return found;
    }
}

using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Pathfinding;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// The fleet: new ships wait at <see cref="AiNavy.Home"/> (the reserve, which also defends the coast). With at least
/// MinFleet ships and an objective (enemy ships in our water, known enemy naval factories, enemy structures within
/// gun range of the water, else unexplored enemy starts) the reserve sails as one fleet when it is strong enough
/// (FleetAttackMargin x (remembered enemy naval strength + defenses at the goal)), after FleetAssembleTimeout or when
/// big. The sailing fleet takes reinforcements in batches, retargets as objectives die, gives up stuck goals, goes
/// home when beaten (FleetRetreatShare) or when the coast at home is attacked.
/// </summary>
public sealed partial class AiNavy
{
    /// <summary>Info.Group of the sailing fleet's members (others with Role Naval are the reserve at Home).</summary>
    public const int FleetGroup = 1_000_001;

    public readonly List<int> Fleet = new();
    public AiGroupState FleetState;
    public Vector2 FleetGoal;
    public UnitHandle FleetTarget = UnitHandle.None;
    /// <summary>Fleets launched (bench/tests).</summary>
    public int FleetsLaunched;
    /// <summary>The map has something for the fleet to attack (ships, naval factories, coast), or the navy is the
    /// main force: the ship cap is MaxFleet, else 6.</summary>
    public bool HasTargets => Priority || _targetsSeenTick > 0 && W.Tick - _targetsSeenTick < 6000;

    private readonly List<int> _reserve = new();
    private readonly List<int> _tmpN = new();
    private readonly List<(Vector2 pos, int until)> _badGoals = new();
    private float _fleetStrength, _reserveStrength, _fleetInitial, _goalThreat;
    private Vector2 _fleetCentroid;
    private int _fleetStateTick, _nextObjectiveTick, _idleOrderTick, _checkTick, _readySince = -1, _targetsSeenTick;
    private Vector2 _checkPos;
    private bool _hasObjective;
    // Top shore candidates of one objective search (fixed size: no allocation).
    private readonly int[] _candIdx = new int[6];
    private readonly float[] _candScore = new float[6];

    /// <summary>Fleet orders (military think).</summary>
    public void Think()
    {
        Decide();
        if (!Active || V.NavalUnits.Count == 0 && Fleet.Count == 0) return;
        var u = W.Units;
        for (int k = Fleet.Count - 1; k >= 0; k--)
        {
            int i = Fleet[k];
            if (!u.IsActive(i) || u.Army[i] != _b.Army) { Fleet.RemoveAt(k); continue; }
            ref var fi = ref _b.Info(i);
            if (fi.Role != AiRole.Naval || fi.Group != FleetGroup) Fleet.RemoveAt(k);
        }
        _reserve.Clear();
        _reserveStrength = 0;
        foreach (int i in V.NavalUnits)
        {
            ref var info = ref _b.Info(i);
            if (info.Role == AiRole.None) { info.Role = AiRole.Naval; info.Group = -1; }
            if (info.Role != AiRole.Naval || info.Group == FleetGroup) continue;
            _reserve.Add(i);
            _reserveStrength += AiPerception.Strength(W, i, W.BpOf(i));
        }
        _fleetStrength = 0;
        var c = Vector2.Zero;
        foreach (int i in Fleet) { _fleetStrength += AiPerception.Strength(W, i, W.BpOf(i)); c += u.Pos[i]; }
        _fleetCentroid = Fleet.Count > 0 ? c / Fleet.Count : Home;

        float homeThreat = CoastThreat(out var threatPos);
        if (homeThreat > 0 && _reserve.Count > 0)
        {
            // The reserve defends the coast at home.
            _tmpN.Clear();
            foreach (int i in _reserve) if (u.Orders[i].IsEmpty || W.Tick - _idleOrderTick > 50) _tmpN.Add(i);
            if (_tmpN.Count > 0) { _b.Cmd.Order(_tmpN, Order.AttackMoveTo(threatPos)); _idleOrderTick = W.Tick; }
        }
        switch (FleetState)
        {
            case AiGroupState.Assemble: Assemble(homeThreat); break;
            case AiGroupState.Attack: Attack(homeThreat); break;
            case AiGroupState.Retreat:
                if (Fleet.Count == 0 || Vector2.Distance(_fleetCentroid, Home) < 25f || W.Tick - _fleetStateTick > 900) Disband();
                break;
        }
    }

    /// <summary>Enemy naval firepower near our base or fleet home (and where); notes the time for torpedo defense.</summary>
    private float CoastThreat(out Vector2 pos)
    {
        var u = W.Units;
        float r = 70f * Scale, r2 = r * r, total = 0;
        var sum = Vector2.Zero;
        foreach (int e in V.EnemyMobile)
        {
            var p = u.Pos[e];
            bool naval;
            float st;
            if (_b.Perception.IsIdentified(e))
            {
                var bp = W.BpOf(e);
                naval = bp.Movement.Layer is MotionLayer.Water or MotionLayer.Sub;
                st = AiPerception.Strength(W, e, bp);
            }
            else
            {
                naval = W.Terrain.InBounds(p) && (W.Terrain.PassAt((int)p.X, (int)p.Y) & PassFlags.DeepWater) != 0;
                st = 50f;
            }
            if (!naval || st <= 0) continue;
            if (Vector2.DistanceSquared(p, Home) > r2 && Vector2.DistanceSquared(p, _b.BasePos) > r2) continue;
            total += st;
            sum += p * st;
        }
        if (total > 0) _enemyShipNearTick = W.Tick;
        pos = total > 0 ? sum / total : Home;
        return total;
    }

    private void Assemble(float homeThreat)
    {
        var u = W.Units;
        if (_reserve.Count == 0) { _readySince = -1; return; }
        if (homeThreat > 0) return;
        _tmpN.Clear();
        foreach (int i in _reserve)
        {
            ref var info = ref _b.Info(i);
            if (u.Orders[i].IsEmpty && info.HoldUntil <= W.Tick && Vector2.DistanceSquared(u.Pos[i], Home) > 18f * 18f)
            {
                _tmpN.Add(i);
                info.HoldUntil = W.Tick + 100;
            }
        }
        if (_tmpN.Count > 0) _b.Cmd.Order(_tmpN, Order.MoveTo(Home));
        if (W.Tick >= _nextObjectiveTick)
        {
            _nextObjectiveTick = W.Tick + 100;
            _hasObjective = ChooseObjective(Home, _reserveStrength, out FleetGoal, out FleetTarget, out _goalThreat);
        }
        int minFleet = Math.Max(2, Cfg.MinFleet + (int)Diff(-1, 0, 1));
        if (_reserve.Count < minFleet) { _readySince = -1; return; }
        if (_readySince < 0) _readySince = W.Tick;
        if (!_hasObjective) return;
        float required = Cfg.FleetAttackMargin * (_b.Perception.EnemyNavalMem * 0.7f + _goalThreat);
        bool timeout = (W.Tick - _readySince) * SimWorld.Dt > Cfg.FleetAssembleTimeout;
        bool big = _reserve.Count >= Cfg.MaxFleet * Diff(0.4f, 1f, 1.3f) * 0.6f;
        if (_reserveStrength < required && !timeout && !big) return;
        Fleet.Clear();
        foreach (int i in _reserve) { Fleet.Add(i); _b.Info(i).Group = FleetGroup; }
        _fleetInitial = _reserveStrength;
        _fleetStrength = _reserveStrength;
        _reserve.Clear();
        _reserveStrength = 0;
        SetState(AiGroupState.Attack);
        FleetsLaunched++;
        _checkTick = W.Tick;
        _checkPos = Home;
        _b.Cmd.Order(Fleet, Order.AttackMoveTo(FleetGoal));
        _idleOrderTick = W.Tick;
        if (_b.Tracing) _b.Trace($"navy: fleet sails n={Fleet.Count} st={_fleetInitial:F0} req={required:F0} goal ({FleetGoal.X:F0},{FleetGoal.Y:F0})");
    }

    private void Attack(float homeThreat)
    {
        var u = W.Units;
        if (Fleet.Count == 0) { SetState(AiGroupState.Assemble); return; }
        // Home coast attacked and the reserve can't hold it: come back.
        if (homeThreat > _reserveStrength + 50f && Vector2.Distance(_fleetCentroid, Home) > 60f) { Retreat("recalled"); return; }
        if (_fleetStrength < Cfg.FleetRetreatShare * _fleetInitial && V.ThreatAt(_fleetCentroid, 30f) > _fleetStrength) { Retreat("beaten"); return; }
        // Reinforcements in batches of 3.
        if (_reserve.Count >= 3 && homeThreat <= 0)
        {
            foreach (int i in _reserve) { Fleet.Add(i); _b.Info(i).Group = FleetGroup; }
            _b.Cmd.Order(_reserve, Order.AttackMoveTo(FleetGoal));
            _fleetInitial += _reserveStrength;
            _reserve.Clear();
        }
        bool targetGone = !FleetTarget.IsNone && !TargetKnown(FleetTarget);
        if (targetGone || W.Tick >= _nextObjectiveTick)
        {
            _nextObjectiveTick = W.Tick + 100;
            if (!ChooseObjective(_fleetCentroid, _fleetStrength, out var goal, out var th, out _goalThreat)) { Retreat("no objective"); return; }
            FleetTarget = th;
            if (Vector2.Distance(goal, FleetGoal) > 10f)
            {
                FleetGoal = goal;
                _b.Cmd.Order(Fleet, Order.AttackMoveTo(FleetGoal));
                _idleOrderTick = W.Tick;
                _checkTick = W.Tick;
                _checkPos = _fleetCentroid;
            }
        }
        // Stuck (blocked channel, unreachable goal): give the goal up for a while.
        if (W.Tick - _checkTick > 600)
        {
            if (Vector2.Distance(_fleetCentroid, _checkPos) < 10f && Vector2.Distance(_fleetCentroid, FleetGoal) > 30f)
            {
                _badGoals.Add((FleetGoal, W.Tick + 1500));
                _nextObjectiveTick = W.Tick;
                if (_b.Tracing) _b.Trace($"navy: fleet stuck, goal ({FleetGoal.X:F0},{FleetGoal.Y:F0}) blacklisted");
            }
            _checkTick = W.Tick;
            _checkPos = _fleetCentroid;
        }
        if (W.Tick - _idleOrderTick < 30) return;
        _tmpN.Clear();
        foreach (int i in Fleet) if (u.Orders[i].IsEmpty) _tmpN.Add(i);
        if (_tmpN.Count > 0) { _b.Cmd.Order(_tmpN, Order.AttackMoveTo(FleetGoal)); _idleOrderTick = W.Tick; }
    }

    private void Retreat(string why)
    {
        SetState(AiGroupState.Retreat);
        _b.Cmd.Order(Fleet, Order.MoveTo(Home));
        if (_b.Tracing) _b.Trace($"navy: fleet retreats ({why}) n={Fleet.Count} st={_fleetStrength:F0}/{_fleetInitial:F0}");
    }

    private void Disband()
    {
        foreach (int i in Fleet) _b.Info(i).Group = -1;
        Fleet.Clear();
        SetState(AiGroupState.Assemble);
        _nextObjectiveTick = W.Tick;
        _readySince = -1;
    }

    private void SetState(AiGroupState s)
    {
        FleetState = s;
        _fleetStateTick = W.Tick;
    }

    private bool TargetKnown(UnitHandle h)
    {
        var u = W.Units;
        if (!u.IsActive(h)) return false;
        return W.BpOf(h.Index).IsStructure ? _b.Perception.IsStructureKnown(h) : _b.Perception.IsKnown(h.Index);
    }

    private bool BadGoal(Vector2 p)
    {
        for (int k = _badGoals.Count - 1; k >= 0; k--)
        {
            if (_badGoals[k].until <= W.Tick) { _badGoals.RemoveAt(k); continue; }
            if (Vector2.DistanceSquared(_badGoals[k].pos, p) < 20f * 20f) return true;
        }
        return false;
    }

    /// <summary>Mass-weighted range of the ships' surface-attack weapons (how far from the water a fleet hits).</summary>
    private float ShoreRange()
    {
        float sum = 0, wsum = 0;
        AddRanges(Fleet, ref sum, ref wsum);
        AddRanges(_reserve, ref sum, ref wsum);
        return wsum > 0 ? sum / wsum : 0f;
    }

    private void AddRanges(List<int> units, ref float sum, ref float wsum)
    {
        foreach (int i in units)
        {
            var bp = W.BpOf(i);
            float r = 0;
            foreach (var wb in bp.Weapons) if ((wb.TargetMask & LayerMask.Land) != 0) r = MathF.Max(r, wb.Range);
            if (r <= 0) continue;
            sum += r * bp.Economy.BuildCostMass;
            wsum += bp.Economy.BuildCostMass;
        }
    }

    /// <summary>
    /// Fleet objective (SEQUENTIAL: nav graph queries): the nearest identified enemy ship in our water; else the best
    /// known enemy naval factory or coastal structure whose nearest water is within reach of our guns and not
    /// defended beyond 1.5x our strength; else an enemy start not seen for a while (explore).
    /// </summary>
    private bool ChooseObjective(Vector2 from, float strength, out Vector2 goal, out UnitHandle target, out float threat)
    {
        goal = Home;
        target = UnitHandle.None;
        threat = 0;
        var g = W.Paths.GraphFor(MotionLayer.Water, FleetReq);
        if (g == null) return false;
        int comp = WaterComp(g, Home, 16);
        if (comp < 0) return false;
        var grid = g.Grid;
        var u = W.Units;
        var db = W.Blueprints;

        // a) Enemy ships we can see, in our water.
        float best = float.MaxValue;
        foreach (int e in V.EnemyMobile)
        {
            if (!_b.Perception.IsIdentified(e)) continue;
            var bp = W.BpOf(e);
            if (bp.Movement.Layer is not (MotionLayer.Water or MotionLayer.Sub)) continue;
            var p = u.Pos[e];
            float d = Vector2.DistanceSquared(p, from);
            if (d >= best || g.ComponentOfCell(grid.CellOf(p)) != comp || BadGoal(p)) continue;
            best = d;
            goal = p;
            target = u.HandleOf(e);
        }
        if (best < float.MaxValue)
        {
            threat = V.ThreatAt(goal, 25f);
            _targetsSeenTick = W.Tick;
            return true;
        }

        // b/c) Known enemy structures by value: naval factories first, then factories, extractors, power, defenses.
        int nc = 0;
        var structs = _b.Perception.Structures;
        for (int k = 0; k < structs.Count; k++)
        {
            var ks = structs[k];
            var bp = db[ks.Bp];
            float value = bp.Has(UnitCategory.Factory) ? (bp.Has(UnitCategory.Naval) ? 200f : 60f)
                : bp.Has(UnitCategory.MassExtractor) ? 50f : bp.Has(UnitCategory.PowerGenerator) ? 30f
                : bp.Weapons.Length > 0 ? 25f : 10f;
            float score = value - Vector2.Distance(from, ks.Pos) * 0.25f;
            // Insert into the fixed-size top list (sorted by score, descending).
            int at = nc;
            while (at > 0 && _candScore[at - 1] < score) at--;
            if (at >= _candIdx.Length) continue;
            for (int m = Math.Min(nc, _candIdx.Length - 1); m > at; m--) { _candIdx[m] = _candIdx[m - 1]; _candScore[m] = _candScore[m - 1]; }
            _candIdx[at] = k;
            _candScore[at] = score;
            if (nc < _candIdx.Length) nc++;
        }
        float shore = MathF.Max(8f, ShoreRange() * 0.9f);
        for (int k = 0; k < nc; k++)
        {
            var ks = structs[_candIdx[k]];
            var bp = db[ks.Bp];
            int cell = g.NearestCellInComponent(ks.Pos, comp);
            if (cell < 0) continue;
            var wp = grid.CenterOf(cell);
            float reach = bp.Has(UnitCategory.Naval) ? MathF.Max(sbpHalf(bp) + 12f, shore) : shore + sbpHalf(bp);
            if (Vector2.DistanceSquared(wp, ks.Pos) > reach * reach || BadGoal(wp)) continue;
            float t = V.ThreatAt(wp, 20f);
            if (t > strength * 1.5f + 1f) continue;
            goal = wp;
            target = ks.Handle;
            threat = t;
            _targetsSeenTick = W.Tick;
            return true;
        }

        // d) Explore: the water nearest an enemy start not seen for a while.
        foreach (var es in _b.EnemyStarts)
        {
            if (_b.Perception.Staleness(V, es) < 3000) continue;
            int cell = g.NearestCellInComponent(es, comp);
            if (cell < 0) continue;
            var wp = grid.CenterOf(cell);
            if (Vector2.Distance(wp, es) > shore + 40f || BadGoal(wp)) continue;
            goal = wp;
            threat = V.ThreatAt(wp, 20f);
            return true;
        }
        return false;

        static float sbpHalf(UnitBlueprint b) => MathF.Max(b.FootprintX, b.FootprintZ) * 0.5f;
    }
}

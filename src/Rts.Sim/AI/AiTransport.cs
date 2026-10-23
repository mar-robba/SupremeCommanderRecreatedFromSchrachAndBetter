using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Pathfinding;
using Rts.Sim.Transport;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Air transport side of a brain: when no enemy base can be reached on foot (Archipelago) the land army is ferried to
/// the enemy island. Transports run a ferry route (<see cref="TransportOrders.Ferry"/>) from a beacon near the rally
/// point to a landing zone on the enemy island (<c>AiTransport.Drop.cs</c>: passable land near the enemy base, low
/// threat); idle land units (pool, idle army) walk to the beacon and board; the dropped units gather at the landing
/// zone and then attack-move the enemy's structures as one group. Low-fuel transports refuel at an air staging (built
/// by a T2 engineer). Hooks: <see cref="ChooseAir"/> (air factory production, from AiEconomy.ChooseAir),
/// <see cref="TryBuild"/> (engineer jobs, from AiEconomy.TryAssign), <see cref="Think"/> (from AiMilitary.Think).
/// <para>Ferried land units have Role Transport and Group <see cref="DropGroup"/>, so the military code leaves them
/// alone. Maps where an enemy base is reachable on foot never get past <see cref="Decide"/>: no command, no random
/// draw (land presets are unchanged).</para>
/// </summary>
public sealed partial class AiTransport
{
    /// <summary>Info.Group of transports and land units of the drop operation.</summary>
    public const int DropGroup = 1_000_002;

    private readonly AiBrain _b;

    /// <summary>No enemy base is reachable on foot: drops are the land army's only way to fight.</summary>
    public bool Island;
    /// <summary>Transports ordered and land units delivered (bench/tests).</summary>
    public int TransportsOrdered, UnitsLanded, StagingOrdered;
    /// <summary>Transports of the ferry route.</summary>
    public readonly List<int> Transports = new();
    /// <summary>Land units sent to the beacon (walking, waiting or carried).</summary>
    public readonly List<int> Cargo = new();

    private bool _decided;
    private int _nextDecideTick;
    private int _homeComp = -1, _idleLand;
    private int _homeThreatTick = int.MinValue / 4;
    private readonly List<int> _tmp = new();
    private readonly List<int> _tmp2 = new();

    public AiTransport(AiBrain b) => _b = b;

    private SimWorld W => _b.W;
    private AiConfig Cfg => _b.Cfg;
    private AiView V => _b.View;
    private float Scale => MathF.Sqrt(MathF.Max(W.Terrain.Width, W.Terrain.Height) / 256f);

    // ------------------------------------------------------------------ decision

    /// <summary>Island mode = no enemy start reachable on foot (SEQUENTIAL: reachability queries). Runs at the first
    /// think and again every 4 minutes until decided.</summary>
    private void Decide()
    {
        if (_decided || W.Tick < _nextDecideTick) return;
        _nextDecideTick = W.Tick + 2400;
        if (_b.R.Transport1 < 0 && _b.R.Transport2 < 0 || Cfg.MaxTransports <= 0 || _b.EnemyStarts.Count == 0) { _decided = true; return; }
        _decided = true;
        foreach (var es in _b.EnemyStarts)
            if (_b.CanReachNear(MotionLayer.Land, 0.6f, _b.BasePos, es, 2f)) return;
        Island = true;
        _homeComp = LandComp(_b.BasePos, 8);
        if (_b.Tracing) _b.Trace($"drop: island mode home comp {_homeComp}");
    }

    private NavGraph? LandGraph => W.Paths.GraphFor(MotionLayer.Land, NavGrid.ClearanceFor(0.6f));

    /// <summary>Land component (small units) at or near p, -1 if no passable land within r cells.</summary>
    private int LandComp(Vector2 p, int r)
    {
        var g = LandGraph;
        if (g == null) return -1;
        var grid = g.Grid;
        int cx = (int)MathF.Floor(p.X), cz = (int)MathF.Floor(p.Y);
        if (!grid.FindNearestPassable(cx, cz, g.Req, r, out int ox, out int oz)) return -1;
        return g.ComponentOfCell(oz * grid.Width + ox);
    }

    // ------------------------------------------------------------------ production

    private int WantTransports()
    {
        if (!Island || !_hasTarget) return 0;
        int idle = 0;
        foreach (int i in V.LandCombat)
        {
            var r = _b.Info(i).Role;
            if (r is AiRole.Pool or AiRole.Attack or AiRole.Transport) idle++;
        }
        _idleLand = idle;
        if (idle < Cfg.DropMinUnits && Transports.Count == 0) return 0;
        int extra = Cfg.DropUnitsPerTransport > 0 ? idle / Cfg.DropUnitsPerTransport : 0;
        return Math.Min(Cfg.MaxTransports * Cfg.DropTransportCap, Cfg.MaxTransports + extra);
    }

    /// <summary>A transport blueprint the air factory <paramref name="factory"/> should build now, or -1.</summary>
    public int ChooseAir(int factory)
    {
        if (!Island) return -1;
        int t1 = _b.R.Transport1, t2 = _b.R.Transport2;
        if (V.Total(t1) + V.Total(t2) >= WantTransports()) return -1;
        var fbp = W.BpOf(factory);
        int bp = fbp.Tech >= 2 && Cfg.MaxUnitTier >= 2 && AiRoster.CanBuild(fbp, t2) ? t2 : AiRoster.CanBuild(fbp, t1) ? t1 : -1;
        if (bp >= 0) TransportsOrdered++;
        return bp;
    }

    /// <summary>Island mode: idle air factories start the wanted transports before the land factories take the
    /// budget (hook in AiEconomy.ManageFactories, after the navy).</summary>
    public void ManageFactories(ref float slot, float budget)
    {
        if (!Island) return;
        // More idle land units than the transports can use: the land (and air) factories get no budget this think,
        // so the mass goes to the navy (served before) and the engineers.
        if (_idleLand > Cfg.DropIdleCap) slot += budget;
        int t1 = _b.R.Transport1, t2 = _b.R.Transport2;
        int have = V.Total(t1) + V.Total(t2);
        int want = WantTransports();
        if (have >= want) return;
        var u = W.Units;
        foreach (int f in V.AirFactories)
        {
            var q = u.Factory[f].Queue;
            if (u.Upgrade[f].Active || q != null && (q.Count >= Cfg.QueueDepth || q.Contains(t1) || q.Contains(t2))) continue;
            int bp = ChooseAir(f);
            if (bp < 0) continue;
            slot += _b.Economy.FactoryWeight(f);
            if (_b.Tracing) _b.Trace($"drop: transport queued ({have + 1}/{want}, queue {q?.Count ?? 0})");
            _b.Cmd.Enqueue(f, bp);
            V.Queued[bp]++;
            if (++have >= want) return;
        }
    }

    /// <summary>Engineer job, before the mass-stall gate (hook in AiEconomy.TryAssign): island maps need an air
    /// factory for the transports, the economy builds it late or never there. Returns true if an order was issued.</summary>
    public bool TryAirFactory(int unit, bool isCommander)
    {
        if (!Island || !_hasTarget) return false;
        int af = _b.BpAirFactory;
        var ubp = W.BpOf(unit);
        if (V.AirFactories.Count > 0 || !AiRoster.CanBuild(ubp, af) || V.Count[af] + _b.Placement.ReservedCount(af) > 0
            || WantTransports() == 0) return false;
        if (!_b.Placement.FindSite(af, _b.BasePos, -_b.EnemyDir, 8f, isCommander ? Cfg.CommanderRadius : 40f, out var fs, 600)) return false;
        Build(unit, ubp, af, fs);
        if (_b.Tracing) _b.Trace($"drop: air factory at ({fs.X:F0},{fs.Y:F0})");
        return true;
    }

    /// <summary>Engineer job: one air staging near the base once the ferry route runs (refuels the transports).
    /// Returns true if an order was issued.</summary>
    public bool TryBuild(int unit, bool isCommander)
    {
        if (!Island || !_hasTarget) return false;
        var ubp = W.BpOf(unit);
        if (isCommander || Transports.Count < 2 || _b.GameTime < Cfg.DropStagingTime) return false;
        int bp = _b.R.AirStaging;
        if (!AiRoster.CanBuild(ubp, bp) || V.Count[bp] + _b.Placement.ReservedCount(bp) > 0) return false;
        if (!_b.Placement.FindSite(bp, _b.BasePos, -_b.EnemyDir, 10f, 30f, out var site, 600)) return false;
        Build(unit, ubp, bp, site);
        StagingOrdered++;
        return true;
    }

    private void Build(int unit, UnitBlueprint ubp, int bp, Vector2 site)
    {
        _b.Cmd.Order(unit, Order.BuildAt(bp, site, 0f));
        _b.Placement.Reserve(bp, site, unit);
        float rate = MathF.Max(1f, ubp.Economy.BuildRate);
        _b.Economy.SetTask(unit, AiTask.Build, site, W.Blueprints[bp].Economy.BuildTime / rate, -1);
    }

    // ------------------------------------------------------------------ think

    /// <summary>Ferry route, boarding and the landed group (military think).</summary>
    public void Think()
    {
        Decide();
        if (!Island) return;
        UpdateTarget();
        Validate();
        if (!_hasTarget) return;
        UpdateLz();
        if (_hasLz)
        {
            ManageTransports();
            Board();
        }
        ManageLanded();
    }

    private void Validate()
    {
        var u = W.Units;
        for (int k = Transports.Count - 1; k >= 0; k--)
            if (!Member(Transports[k])) Transports.RemoveAt(k);
        for (int k = Cargo.Count - 1; k >= 0; k--)
            if (!Member(Cargo[k])) Cargo.RemoveAt(k);
        for (int k = Landed.Count - 1; k >= 0; k--)
            if (!Member(Landed[k])) Landed.RemoveAt(k);
        foreach (int i in V.AirUnits)
        {
            ref var info = ref _b.Info(i);
            if (info.Role != AiRole.Transport || info.Group == DropGroup) continue;
            var bp = W.BpOf(i);
            if (!bp.IsVtol || bp.Cargo == null || bp.Cargo.Layer != CargoLayer.Land) continue;
            info.Group = DropGroup;
            Transports.Add(i);
        }

        bool Member(int i) => u.IsActive(i) && u.Army[i] == _b.Army && _b.Info(i).Role == AiRole.Transport && _b.Info(i).Group == DropGroup;
    }

    /// <summary>Keeps every transport on the ferry route (beacon to landing zone); low-fuel empty transports refuel
    /// at the air staging first.</summary>
    private void ManageTransports()
    {
        var u = W.Units;
        var c = W.Cargo;
        bool staging = _b.R.AirStaging >= 0 && V.Complete[_b.R.AirStaging] > 0;
        _tmp.Clear();
        foreach (int i in Transports)
        {
            var q = u.Orders[i];
            if (!q.IsEmpty && q.Current.Type == OrderType.Dock) continue;
            var bp = W.BpOf(i);
            if (staging && c.Count(i) == 0 && bp.FuelSeconds > 0 && c.Fuel[i] < bp.FuelSeconds * 0.25f)
            {
                _b.Cmd.Order(i, TransportOrders.Dock(UnitHandle.None));
                _b.Cmd.Order(i, TransportOrders.Ferry(Beacon, Lz), queue: true);
                continue;
            }
            bool ok = !q.IsEmpty && q.Current.Type == OrderType.Ferry
                && Vector2.DistanceSquared(q.Current.Pos, Beacon) < 4f && Vector2.DistanceSquared(q.Current.Aux, Lz) < 4f;
            if (!ok) _tmp.Add(i);
        }
        if (_tmp.Count > 0) _b.Cmd.Order(_tmp, TransportOrders.Ferry(Beacon, Lz));
    }

    /// <summary>Sends idle land units to the beacon until about two loads wait there (keeping a few at home), and
    /// re-sends units that lost their ferry order.</summary>
    private void Board()
    {
        if (Transports.Count == 0) return;
        var u = W.Units;
        // Enemies in the base (an enemy drop): units still at home help the defense instead of boarding.
        if (V.ThreatNowAt(_b.BasePos, Cfg.DefenseRadius) >= Cfg.DropDefendThreat)
        {
            _homeThreatTick = W.Tick;
            int released = 0;
            for (int k = Cargo.Count - 1; k >= 0; k--)
            {
                int i = Cargo[k];
                if ((u.Flags[i] & UnitFlags.Carried) != 0) continue;
                ref var info = ref _b.Info(i);
                info.Role = AiRole.Pool;
                info.Group = -1;
                Cargo.RemoveAt(k);
                released++;
            }
            if (released > 0 && _b.Tracing) _b.Trace($"drop: base attacked, {released} units stay to defend");
        }
        if (W.Tick - _homeThreatTick < 300) return;
        int capacity = 0;
        foreach (int t in Transports) capacity += W.BpOf(t).Cargo!.Capacity;
        _tmp.Clear();
        int waiting = 0;
        foreach (int i in Cargo)
        {
            if ((u.Flags[i] & UnitFlags.Carried) != 0) continue;
            waiting += Math.Max(1, W.BpOf(i).CargoSize);
            var q = u.Orders[i];
            bool ok = !q.IsEmpty && q.Current.Type == OrderType.Ferry && Vector2.DistanceSquared(q.Current.Pos, Beacon) < 4f;
            if (!ok) _tmp.Add(i);
        }
        if (_tmp.Count > 0) _b.Cmd.Order(_tmp, TransportOrders.Ferry(Beacon, Lz));

        int want = capacity * 2 - waiting;
        if (want <= 0) return;
        var army = _b.Military.Army;
        bool armyIdle = army != null && army.State == AiGroupState.Assemble;
        int maxSize = 0;
        foreach (int t in Transports) maxSize = Math.Max(maxSize, W.BpOf(t).Cargo!.Capacity);
        int keep = Cfg.DropKeepHome;
        _tmp.Clear();
        foreach (int i in V.LandCombat)
        {
            ref var info = ref _b.Info(i);
            bool pool = info.Role == AiRole.Pool && info.HoldUntil <= W.Tick;
            bool idleArmy = armyIdle && info.Role == AiRole.Attack && info.Group == army!.Id;
            if (!pool && !idleArmy) continue;
            var bp = W.BpOf(i);
            if (!TransportOrders.IsLandCargo(bp) || bp.CargoSize > maxSize || bp.Weapons.Length == 0) continue;
            if (pool && keep > 0) { keep--; continue; }
            if (LandComp(u.Pos[i], 3) != _homeComp) continue;
            info.Role = AiRole.Transport;
            info.Group = DropGroup;
            Cargo.Add(i);
            _tmp.Add(i);
            want -= Math.Max(1, bp.CargoSize);
            if (want <= 0) break;
        }
        if (_tmp.Count > 0)
        {
            _b.Cmd.Order(_tmp, TransportOrders.Ferry(Beacon, Lz));
            if (_b.Tracing) _b.Trace($"drop: {_tmp.Count} units to the beacon ({Cargo.Count} boarding, {Transports.Count} transports)");
        }
    }
}

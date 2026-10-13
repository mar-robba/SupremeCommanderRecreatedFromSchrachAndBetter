using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Pathfinding;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// One AI player. Thinking is spread over ticks: the economy thinks every <see cref="AiConfig.EconInterval"/> ticks,
/// the military every <see cref="AiConfig.MilitaryInterval"/>, both staggered by army index so several AIs don't
/// think on the same tick. Each think starts with a fresh <see cref="AiView"/> from the perception layer.
/// </summary>
public sealed class AiBrain
{
    public readonly AiState State;
    public readonly SimWorld W;
    public readonly int Army;
    public AiConfig Cfg;
    public readonly AiPerception Perception;
    public readonly AiView View = new();
    public readonly AiEconomy Economy;
    public readonly AiMilitary Military;
    public readonly AiScouting Scouting;
    public readonly AiPlacement Placement;
    public readonly AiStats Stats = new();
    public readonly AiCommands Cmd;

    /// <summary>Base center (commander start), direction toward the nearest enemy start, rally point for new units.</summary>
    public Vector2 BasePos;
    public Vector2 EnemyDir = new(1, 0);
    public Vector2 Rally;
    public Vector2 AirRally;
    /// <summary>Start positions of the enemy armies (fallback attack targets).</summary>
    public readonly List<Vector2> EnemyStarts = new();
    private bool _initialized;

    // Blueprint indices from the army faction's roles (-1 if the faction has no such unit).
    public readonly int BpEngineer, BpTank, BpArtillery, BpMobileAA, BpScout, BpLandFactory, BpAirFactory;
    public readonly int BpMex, BpPgen, BpHydro, BpMassStorage, BpEnergyStorage, BpPD, BpAA, BpRadar;
    public readonly int BpInterceptor, BpBomber, BpAirScout;

    public AiBrain(AiState state, int army)
    {
        State = state;
        W = state.World;
        Army = army;
        Cfg = AiConfig.For(W.Armies[army].AiDifficulty);
        Perception = new AiPerception(W, army);
        Cmd = new AiCommands(W, army, Stats);
        Placement = new AiPlacement(this);
        Economy = new AiEconomy(this);
        Military = new AiMilitary(this);
        Scouting = new AiScouting(this);

        // Blueprints come from the army faction's roles (never hardcoded ids): -1 = the faction has no such unit.
        var factions = W.Blueprints.Factions;
        string faction = W.Armies[army].Faction;
        int R(string role) => factions.RoleBlueprint(faction, role);
        BpEngineer = R(FactionRoles.EngineerT1);
        BpTank = R(FactionRoles.TankT1);
        BpArtillery = R(FactionRoles.ArtilleryT1);
        BpMobileAA = R(FactionRoles.MobileAaT1);
        BpScout = R(FactionRoles.ScoutT1);
        BpLandFactory = R(FactionRoles.LandFactoryT1);
        BpAirFactory = R(FactionRoles.AirFactoryT1);
        BpMex = R(FactionRoles.MexT1);
        BpPgen = R(FactionRoles.PgenT1);
        BpHydro = R(FactionRoles.Hydro);
        BpMassStorage = R(FactionRoles.MassStorage);
        BpEnergyStorage = R(FactionRoles.EnergyStorage);
        BpPD = R(FactionRoles.PdT1);
        BpAA = R(FactionRoles.AaT1);
        BpRadar = R(FactionRoles.RadarT1);
        BpInterceptor = R(FactionRoles.InterceptorT1);
        BpBomber = R(FactionRoles.BomberT1);
        BpAirScout = R(FactionRoles.AirScoutT1);
    }

    public float GameTime => W.GameTime;

    /// <summary>Deterministic random stream for this army, tick and purpose.</summary>
    public DetRandom Rng(int salt) => DetRandom.For(W.Seed, W.Tick, 0x41000 + Army, salt);

    public ref AiUnitInfo Info(int unit) => ref State.Info(unit);

    public void Update(List<Command> output)
    {
        var army = W.Armies[Army];
        if (Cfg.Difficulty != army.AiDifficulty) Cfg = AiConfig.For(army.AiDifficulty);
        int t = W.Tick;
        int phase = Army * 7;
        bool econ = !_initialized || (t + phase) % Cfg.EconInterval == 0;
        bool mil = (t + phase + 3) % Cfg.MilitaryInterval == 0;
        if (!econ && !mil) return;
        Cmd.Begin(output);
        if (!_initialized) Initialize();
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Perception.Scan(View);
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        Stats.ScanMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds;
        if (View.Commander < 0 && View.Engineers.Count == 0 && View.LandFactories.Count == 0 && View.AirFactories.Count == 0
            && View.LandCombat.Count == 0 && View.AirUnits.Count == 0) return;
        if (econ) Economy.Think();
        long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
        Stats.EconMs += System.Diagnostics.Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds;
        if (mil) Military.Think();
        Stats.MilitaryMs += System.Diagnostics.Stopwatch.GetElapsedTime(t2).TotalMilliseconds;
    }

    private void Initialize()
    {
        _initialized = true;
        var u = W.Units;
        var cmd = W.Armies[Army].Commander;
        BasePos = u.IsActive(cmd) ? u.Pos[cmd.Index] : W.Terrain.StartPositions[Army % W.Terrain.StartPositions.Length];
        var center = new Vector2(W.Terrain.Width * 0.5f, W.Terrain.Height * 0.5f);

        // Enemy start positions: where each enemy commander starts (fallback: the map's start list).
        float best = float.MaxValue;
        var nearestEnemy = center;
        for (int a = 0; a < W.Armies.Length; a++)
        {
            if (!W.IsEnemy(Army, a)) continue;
            var h = W.Armies[a].Commander;
            var p = u.IsActive(h) ? u.Pos[h.Index] : W.Terrain.StartPositions[a % W.Terrain.StartPositions.Length];
            EnemyStarts.Add(p);
            float d = Vector2.DistanceSquared(p, BasePos);
            if (d < best) { best = d; nearestEnemy = p; }
        }
        var toEnemy = nearestEnemy - BasePos;
        if (toEnemy.LengthSquared() < 1) toEnemy = center - BasePos;
        EnemyDir = toEnemy.LengthSquared() > 1e-3f ? Vector2.Normalize(toEnemy) : new Vector2(1, 0);

        W.Paths.EnsureGrids();
        Rally = FindStandable(BasePos + EnemyDir * 24f, MotionLayer.Land);
        AirRally = W.Terrain.Clamp(BasePos + EnemyDir * 10f);
        Scouting.Initialize();
    }

    /// <summary>A point near p where a unit of the layer can stand (searching outward), reachable from the base.</summary>
    public Vector2 FindStandable(Vector2 p, MotionLayer layer)
    {
        p = W.Terrain.Clamp(p, 2f);
        var grid = W.Paths.GridFor(layer);
        if (grid == null) return p;
        int cx = (int)MathF.Floor(p.X), cz = (int)MathF.Floor(p.Y);
        if (grid.FindNearestPassable(cx, cz, 1, 24, out int ox, out int oz)) return new Vector2(ox + 0.5f, oz + 0.5f);
        return p;
    }

    /// <summary>Land reachability helper (SEQUENTIAL: may build a nav graph). Tries points around a target so that
    /// structures (whose own cells are blocked) count as reachable when their surroundings are.</summary>
    public bool CanReachNear(MotionLayer layer, float radius, Vector2 from, Vector2 target, float targetHalfSize)
    {
        if (layer == MotionLayer.Air) return true;
        var paths = W.Paths;
        // The origin may sit on a structure footprint (e.g. the base center): start from the nearest open cell.
        var grid = paths.GridFor(layer);
        if (grid != null && !grid.CanStand(from)
            && grid.FindNearestPassable((int)MathF.Floor(from.X), (int)MathF.Floor(from.Y), 1, 12, out int fx, out int fz))
            from = new Vector2(fx + 0.5f, fz + 0.5f);
        if (paths.CanReach(layer, radius, from, target)) return true;
        float r = targetHalfSize + 2f;
        for (int k = 0; k < 8; k++)
        {
            var p = W.Terrain.Clamp(target + DMath.Dir(k * DMath.TwoPi / 8) * r);
            if (paths.CanReach(layer, radius, from, p)) return true;
        }
        return false;
    }

    private readonly PathResult _route = new();
    private Vector2 _routeFrom;

    /// <summary>
    /// Solves the walking route from 'from' to 'to' (SEQUENTIAL: one path solve) and keeps it for
    /// <see cref="RoutePointBack"/>. Used to stage attacks along the real route, which on cliff maps can be much
    /// longer than the straight line. Returns false when there is no full route (unreachable or truncated).
    /// </summary>
    public bool SolveRoute(MotionLayer layer, float radius, Vector2 from, Vector2 to, out float length)
    {
        length = 0;
        _route.Reset();
        _routeFrom = from;
        var g = W.Paths.GraphFor(layer, NavGrid.ClearanceFor(radius));
        if (g == null) return false;
        PathSolver.ForThread().Solve(W.Paths, g, from, to, _route);
        if (_route.Failed || _route.Partial || _route.Count == 0) { _route.Reset(); return false; }
        for (int k = 0; k < _route.Count; k++) length += Vector2.Distance(k == 0 ? from : _route.Points[k - 1], _route.Points[k]);
        return true;
    }

    /// <summary>The point 'back' world units before the end of the last solved route (its start if shorter).</summary>
    public Vector2 RoutePointBack(float back)
    {
        int n = _route.Count;
        if (n == 0) return _routeFrom;
        float left = back;
        for (int k = n - 1; k >= 0; k--)
        {
            var a = k == 0 ? _routeFrom : _route.Points[k - 1];
            var b = _route.Points[k];
            float seg = Vector2.Distance(a, b);
            if (seg >= left) return seg > 1e-4f ? b + (a - b) * (left / seg) : b;
            left -= seg;
        }
        return _routeFrom;
    }

    public UnitBlueprint Bp(int unit) => W.BpOf(unit);

    /// <summary>Emits a diagnostics message (only when a tool installed <see cref="AiSystem.Trace"/>).</summary>
    public void Trace(string msg) => AiSystem.Trace?.Invoke(Army, msg);
    public bool Tracing => AiSystem.Trace != null;
}

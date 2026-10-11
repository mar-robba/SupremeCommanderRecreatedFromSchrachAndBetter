using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
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

    // Blueprint indices (-1 if missing from the data).
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

        var db = W.Blueprints;
        BpEngineer = db.IndexOf("uef_t1_engineer");
        BpTank = db.IndexOf("uef_t1_tank");
        BpArtillery = db.IndexOf("uef_t1_artillery");
        BpMobileAA = db.IndexOf("uef_t1_mobile_aa");
        BpScout = db.IndexOf("uef_t1_scout");
        BpLandFactory = db.IndexOf("uef_t1_land_factory");
        BpAirFactory = db.IndexOf("uef_t1_air_factory");
        BpMex = db.IndexOf("uef_t1_mass_extractor");
        BpPgen = db.IndexOf("uef_t1_power_generator");
        BpHydro = db.IndexOf("uef_t1_hydrocarbon");
        BpMassStorage = db.IndexOf("uef_t1_mass_storage");
        BpEnergyStorage = db.IndexOf("uef_t1_energy_storage");
        BpPD = db.IndexOf("uef_t1_point_defense");
        BpAA = db.IndexOf("uef_t1_aa_turret");
        BpRadar = db.IndexOf("uef_t1_radar");
        BpInterceptor = db.IndexOf("uef_t1_interceptor");
        BpBomber = db.IndexOf("uef_t1_bomber");
        BpAirScout = db.IndexOf("uef_t1_air_scout");
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

    public UnitBlueprint Bp(int unit) => W.BpOf(unit);
}

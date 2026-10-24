using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Naval;
using Rts.Sim.Orders;
using Rts.Sim.Pathfinding;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Naval side of a brain: decides whether the map calls for a navy (a coast near the base whose water connects to an
/// enemy base), builds naval factories, torpedo launchers and sonar through engineers, keeps the naval factories
/// producing and moves the fleet (<c>AiNavy.Fleet.cs</c>). Hooks: <see cref="TryBuild"/> (engineer jobs, from
/// AiEconomy.TryAssign), <see cref="ManageFactories"/> (from AiEconomy.ManageFactories: before the land/air factories
/// when <see cref="Priority"/>), <see cref="Think"/> (from AiMilitary.Think).
/// <para>Maps without water near the base (Plains) never get past <see cref="Decide"/>: no command, no random draw,
/// so those games are unchanged by the navy.</para>
/// </summary>
public sealed partial class AiNavy
{
    private readonly AiBrain _b;

    /// <summary>The map calls for a navy: a naval factory site near the base whose water reaches an enemy base.</summary>
    public bool Active;
    /// <summary>No enemy base is reachable by land (Archipelago): the navy is the main force and gets the factory
    /// budget before the land factories.</summary>
    public bool Priority;
    /// <summary>First naval factory site (coast near the base) and the fleet's gathering point on open water.</summary>
    public Vector2 Site, Home;
    /// <summary>Battleships (clearance class 4) can sail from Home to an enemy coast.</summary>
    public bool BattleshipOk;
    /// <summary>Naval factories ordered (bench/tests).</summary>
    public int FactoriesOrdered, TorpedoOrdered, SonarOrdered;

    private bool _decided, _waterChecked, _hasWater;
    private int _nextDecideTick;
    private int _siteFailUntil;
    /// <summary>Last tick an enemy ship was seen near our base or fleet home.</summary>
    private int _enemyShipNearTick = int.MinValue / 4;

    /// <summary>Clearance class planned for the fleet (frigates, subs, destroyers, cruisers).</summary>
    private const int FleetReq = 2;

    public AiNavy(AiBrain b) => _b = b;

    private SimWorld W => _b.W;
    private AiConfig Cfg => _b.Cfg;
    private AiView V => _b.View;
    private float Scale => MathF.Sqrt(MathF.Max(W.Terrain.Width, W.Terrain.Height) / 256f);
    private float Diff(float easy, float normal, float hard) =>
        Cfg.Difficulty == AiDifficulty.Easy ? easy : Cfg.Difficulty == AiDifficulty.Hard ? hard : normal;

    // ------------------------------------------------------------------ decision

    /// <summary>Looks for a coast near the base whose water reaches an enemy base (SEQUENTIAL: may build the water
    /// nav graph). Runs at the first economy think and every 4 minutes until a site is found.</summary>
    private void Decide()
    {
        if (_decided || W.Tick < _nextDecideTick) return;
        _nextDecideTick = W.Tick + 2400;
        if (!Cfg.NavyEnabled) return;
        int fbp = _b.R.NavalFactory[1];
        if (fbp < 0) { _decided = true; return; }
        if (!_waterChecked)
        {
            _waterChecked = true;
            var pass = W.Terrain.Pass;
            for (int c = 0; c < pass.Length && !_hasWater; c++) _hasWater = (pass[c] & PassFlags.DeepWater) != 0;
        }
        if (!_hasWater) { _decided = true; return; }
        var g = W.Paths.GraphFor(MotionLayer.Water, FleetReq);
        if (g == null) { _decided = true; return; }
        if (!FindCoastSite(fbp, _b.BasePos, Cfg.NavalSiteRadius * Scale, requireReach: true, out var site)) return;
        _decided = true;
        Active = true;
        Site = site;
        int comp = WaterComp(g, site, 13);
        Home = HomeFor(g, comp, site);
        Priority = true;
        foreach (var es in _b.EnemyStarts)
            if (_b.CanReachNear(MotionLayer.Land, 0.6f, _b.BasePos, es, 2f)) { Priority = false; break; }
        BattleshipOk = CheckBattleships();
        if (_b.Tracing) _b.Trace($"navy: site ({Site.X:F0},{Site.Y:F0}) home ({Home.X:F0},{Home.Y:F0}) priority={Priority} battleships={BattleshipOk}");
    }

    /// <summary>Water component (class FleetReq) at or near p (-1 if no passable water within r cells).</summary>
    private static int WaterComp(NavGraph g, Vector2 p, int r)
    {
        var grid = g.Grid;
        if (!grid.FindNearestPassable((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), g.Req, r, out int ox, out int oz)) return -1;
        return g.ComponentOfCell(oz * grid.Width + ox);
    }

    /// <summary>Does the water component come close to an enemy base (where its naval factories and coast are)?</summary>
    private bool CompReachesEnemy(NavGraph g, int comp)
    {
        if (comp < 0) return false;
        float reach = (Cfg.NavalSiteRadius + 20f) * Scale;
        foreach (var es in _b.EnemyStarts)
        {
            int c = g.NearestCellInComponent(es, comp);
            if (c >= 0 && Vector2.DistanceSquared(g.Grid.CenterOf(c), es) <= reach * reach) return true;
        }
        return false;
    }

    /// <summary>Fleet gathering point: open water a little out from the site (away from the base).</summary>
    private Vector2 HomeFor(NavGraph g, int comp, Vector2 site)
    {
        var away = site - _b.BasePos;
        away = away.LengthSquared() > 1e-3f ? Vector2.Normalize(away) : _b.EnemyDir;
        int c = comp < 0 ? -1 : g.NearestCellInComponent(W.Terrain.Clamp(site + away * 16f, 2f), comp);
        return c >= 0 ? g.Grid.CenterOf(c) : site;
    }

    private bool CheckBattleships()
    {
        if (_b.R.Battleship3 < 0) return false;
        var g4 = W.Paths.GraphFor(MotionLayer.Water, NavGrid.ClearanceFor(W.Blueprints[_b.R.Battleship3].Radius));
        return g4 != null && CompReachesEnemy(g4, WaterComp(g4, Home, 24));
    }

    /// <summary>
    /// Nearest buildable water site for a water structure within maxR of center (ring scan, deterministic order): every
    /// footprint cell deep enough (OccupancyGrid.CanPlace), a 2-cell ring free of structures (ships get out), clear of
    /// our reservations; with <paramref name="requireReach"/> its water must reach an enemy base (checked once per
    /// water component).
    /// </summary>
    private bool FindCoastSite(int bp, Vector2 center, float maxR, bool requireReach, out Vector2 site)
    {
        site = default;
        var t = W.Terrain;
        var occ = W.Occupancy;
        var sbp = W.Blueprints[bp];
        int cx = (int)MathF.Floor(center.X), cz = (int)MathF.Floor(center.Y);
        int rmax = (int)MathF.Ceiling(maxR);
        int okComp = -2, badComp1 = -2, badComp2 = -2;
        NavGraph? g = requireReach ? W.Paths.GraphFor(MotionLayer.Water, FleetReq) : null;
        int margin = sbp.Has(UnitCategory.Factory) ? 2 : 1;
        var res = _b.Placement.Reserved;
        for (int r = 0; r <= rmax; r++)
        {
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                int x = cx + dx, z = cz + dz;
                if (!t.CellInBounds(x, z) || !NavalRules.CellBuildable(t, sbp, x, z)) continue;
                var p = OccupancyGrid.SnapStructure(sbp, new Vector2(x + 0.5f, z + 0.5f));
                if (Vector2.DistanceSquared(p, center) > maxR * maxR || !occ.CanPlace(sbp, p, t)) continue;
                var (x0, z0, x1, z1) = OccupancyGrid.Footprint(sbp, p);
                x0 -= margin; z0 -= margin; x1 += margin; z1 += margin;
                if (x0 < 1 || z0 < 1 || x1 >= t.Width - 1 || z1 >= t.Height - 1) continue;
                bool blocked = false;
                for (int zz = z0; zz <= z1 && !blocked; zz++)
                for (int xx = x0; xx <= x1; xx++)
                    if (occ.IsBlocked(xx, zz)) { blocked = true; break; }
                if (blocked) continue;
                for (int k = 0; k < res.Count && !blocked; k++)
                {
                    var rv = res[k];
                    blocked = x0 <= rv.X1 && x1 >= rv.X0 && z0 <= rv.Z1 && z1 >= rv.Z0;
                }
                if (blocked) continue;
                if (g != null)
                {
                    int comp = WaterComp(g, p, (int)MathF.Max(sbp.FootprintX, sbp.FootprintZ) / 2 + 8);
                    if (comp < 0 || comp == badComp1 || comp == badComp2) continue;
                    if (comp != okComp)
                    {
                        if (!CompReachesEnemy(g, comp))
                        {
                            if (badComp1 == -2) badComp1 = comp; else badComp2 = comp;
                            continue;
                        }
                        okComp = comp;
                    }
                }
                site = p;
                return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------------ engineer jobs

    private int NavalFactories()
    {
        var nf = _b.R.NavalFactory;
        int n = 0;
        for (int t = 1; t <= 3; t++) if (nf[t] >= 0) n += V.Count[nf[t]];
        return n;
    }

    private int CountWithReserved(int bp) => bp < 0 ? 0 : V.Count[bp] + _b.Placement.ReservedCount(bp);

    /// <summary>Engineer job: naval factory / torpedo launcher / sonar. Returns true if an order was issued.</summary>
    public bool TryBuild(int unit, bool isCommander)
    {
        Decide();
        if (!Active) return false;
        if (isCommander && !(Priority && Vector2.Distance(Site, _b.BasePos) <= MathF.Max(Cfg.CommanderRadius, 55f))) return false;
        var ubp = W.BpOf(unit);
        if (TryNavalFactory(unit, ubp)) return true;
        if (isCommander) return false;
        if (TryTorpedo(unit, ubp)) return true;
        if (TrySonar(unit, ubp)) return true;
        return false;
    }

    private bool TryNavalFactory(int unit, UnitBlueprint ubp)
    {
        int bp = _b.R.NavalFactory[1];
        if (!AiRoster.CanBuild(ubp, bp) || W.Tick < _siteFailUntil) return false;
        var eco = _b.Economy;
        int n = NavalFactories() + _b.Placement.ReservedCount(bp);
        int cap = Priority ? Cfg.MaxNavalFactories : Math.Max(1, Cfg.MaxNavalFactories - 1);
        if (n >= cap) return false;
        if (V.Count[bp] - V.Complete[bp] + _b.Placement.ReservedCount(bp) > 0) return false; // one at a time
        float t = _b.GameTime;
        if (n == 0)
        {
            if (Priority) { if (t < 150f && !eco.OpeningDone) return false; }
            else
            {
                float when = Cfg.NavalFactoryTime * (V.EnemyNavalFactories > 0 || _b.Perception.EnemyNavalMem > 0 ? 0.6f : 1f);
                if (t < when || V.LandFactories.Count < 2 || eco.MassIncome < 12f) return false;
            }
        }
        else if (eco.MassIncome < (Priority ? 8f + 10f * n : 30f + 15f * n) || eco.MassRatio < 0.2f) return false;
        Vector2 center = n == 0 ? _b.BasePos : Site;
        float radius = n == 0 ? Cfg.NavalSiteRadius * Scale : 40f * Scale;
        if (!FindCoastSite(bp, center, radius, requireReach: true, out var site))
        {
            _siteFailUntil = W.Tick + 600;
            return false;
        }
        var g = W.Paths.GraphFor(MotionLayer.Water, FleetReq)!;
        var exit = HomeFor(g, WaterComp(g, site, 13), site) - site;
        float heading = exit.LengthSquared() > 1e-3f ? DMath.HeadingOf(exit) : DMath.HeadingOf(_b.EnemyDir);
        Issue(unit, ubp, bp, site, heading);
        FactoriesOrdered++;
        if (_b.Tracing) _b.Trace($"navy: naval factory #{n + 1} at ({site.X:F0},{site.Y:F0})");
        return true;
    }

    private bool TryTorpedo(int unit, UnitBlueprint ubp)
    {
        int bp = _b.R.Torpedo1;
        if (!AiRoster.CanBuild(ubp, bp) || _b.Economy.MassRatio < 0.15f) return false;
        int cap = Math.Max(0, Cfg.MaxTorpedoDefenses + (int)Diff(-1, 0, 1));
        bool shipsNear = W.Tick - _enemyShipNearTick < 3000;
        if (!shipsNear) cap = V.EnemyNavalFactories > 0 && _b.GameTime > 600 ? Math.Min(cap, 1) : 0;
        if (CountWithReserved(bp) >= cap) return false;
        return TryWaterStructure(unit, ubp, bp, Site, 28f);
    }

    private bool TrySonar(int unit, UnitBlueprint ubp)
    {
        int bp = _b.R.Sonar1;
        if (!AiRoster.CanBuild(ubp, bp) || _b.GameTime < Diff(900, 540, 420) || CountWithReserved(bp) >= 1) return false;
        if (_b.R.Sonar2 >= 0 && V.Count[_b.R.Sonar2] > 0) return false;
        return TryWaterStructure(unit, ubp, bp, Home, 24f);
    }

    private bool TryWaterStructure(int unit, UnitBlueprint ubp, int bp, Vector2 near, float radius)
    {
        if (W.Tick < _siteFailUntil) return false;
        if (!FindCoastSite(bp, near, radius * Scale, requireReach: false, out var site))
        {
            _siteFailUntil = W.Tick + 600;
            return false;
        }
        Issue(unit, ubp, bp, site, 0f);
        if (bp == _b.R.Torpedo1) TorpedoOrdered++; else SonarOrdered++;
        return true;
    }

    private void Issue(int unit, UnitBlueprint ubp, int bp, Vector2 site, float heading)
    {
        _b.Cmd.Order(unit, Order.BuildAt(bp, site, heading));
        _b.Placement.Reserve(bp, site, unit);
        float rate = MathF.Max(1f, ubp.Economy.BuildRate);
        _b.Economy.SetTask(unit, AiTask.Build, site, W.Blueprints[bp].Economy.BuildTime / rate, -1);
    }

    // ------------------------------------------------------------------ factories

    /// <summary>Keeps the naval factories' queues topped up. <paramref name="slot"/> counts factories allowed to
    /// start units against <paramref name="budget"/> (same budget as land/air factories).</summary>
    public void ManageFactories(ref float slot, float budget)
    {
        Decide();
        if (!Active || V.NavalFactories.Count == 0) return;
        var eco = _b.Economy;
        var army = W.Armies[_b.Army];
        var u = W.Units;
        int ships = V.NavalUnits.Count;
        foreach (int f in V.NavalFactories)
        {
            var q = u.Factory[f].Queue;
            if (q != null) ships += q.Count;
        }
        int fleetCap = HasTargets ? (int)(Cfg.MaxFleet * Diff(0.4f, 1f, 1.3f)) : 6;
        foreach (int f in V.NavalFactories)
        {
            ref var info = ref _b.Info(f);
            if (!info.RallySet)
            {
                info.RallySet = true;
                info.Role = AiRole.Factory;
                _b.Cmd.Order(f, Order.MoveTo(Home));
            }
            slot += eco.FactoryWeight(f);
            if (slot > budget || u.Upgrade[f].Active) continue;
            int len = u.Factory[f].Queue?.Count ?? 0;
            if (len >= Cfg.QueueDepth) continue;
            if (len >= 1 && (eco.MassStall || eco.EnergyStall)) continue;
            if (eco.EnergyStall && eco.EnergyIncome < eco.EnergyDemand * 0.6f) continue;
            if (army.UnitCount + 4 >= army.UnitCap || ships >= fleetCap) return;
            int bp = ChooseShip(f);
            if (bp < 0) continue;
            _b.Cmd.Enqueue(f, bp);
            V.Queued[bp]++;
            ships++;
            _b.Stats.Naval++;
        }
    }

    private float Mass(int bp) => bp < 0 ? 0f : V.Total(bp) * W.Blueprints[bp].Economy.BuildCostMass;

    /// <summary>Ship by mass deficit against the tier's mix (frigates/subs; destroyers/cruisers; battleships), more
    /// cruisers against enemy air.</summary>
    private int ChooseShip(int factory)
    {
        var fbp = W.BpOf(factory);
        var r = _b.R;
        int tier = Math.Min(Math.Min(Math.Clamp(Cfg.MaxTier, 1, 3), Cfg.MaxUnitTier), fbp.Tech);
        float wF, wS, wD = 0, wC = 0, wB = 0;
        if (tier >= 3 && BattleshipOk && AiRoster.CanBuild(fbp, r.Battleship3)) { wB = 0.4f; wD = 0.25f; wC = 0.25f; wS = 0.1f; wF = 0f; }
        else if (tier >= 2) { wD = 0.4f; wC = 0.3f; wS = 0.15f; wF = 0.15f; }
        else { wF = 0.65f; wS = 0.35f; }
        if (_b.Perception.EnemyAirStrengthMem > 400f || _b.Perception.EnemyAirCountMem > 6f)
        {
            if (tier >= 2) { wC += 0.15f; wD -= 0.1f; wS = MathF.Max(0f, wS - 0.05f); }
            else { wF += 0.15f; wS -= 0.15f; }
        }
        float mF = Mass(r.Frigate1), mS = Mass(r.Sub1), mD = Mass(r.Destroyer2), mC = Mass(r.Cruiser2), mB = Mass(r.Battleship3);
        float n = mF + mS + mD + mC + mB + 1f;
        int best = -1;
        float bestDef = float.MinValue;
        Consider(r.Battleship3, wB, mB);
        Consider(r.Destroyer2, wD, mD);
        Consider(r.Cruiser2, wC, mC);
        Consider(r.Frigate1, wF, mF);
        Consider(r.Sub1, wS, mS);
        if (best < 0) best = AiRoster.CanBuild(fbp, r.Frigate1) ? r.Frigate1 : AiRoster.CanBuild(fbp, r.Sub1) ? r.Sub1 : -1;
        return best;

        void Consider(int bp, float want, float have)
        {
            if (want <= 0 || !AiRoster.CanBuild(fbp, bp)) return;
            float d = want - have / n;
            if (d > bestDef) { bestDef = d; best = bp; }
        }
    }
}

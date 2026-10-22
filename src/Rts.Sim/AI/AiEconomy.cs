using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Economy side of a brain: the commander's opening build order, engineer jobs (expand to free deposits nearest the
/// base first, power to keep energy ahead of the projected spending, factories when mass floats, defenses, storage,
/// finishing orphaned sites, repair, reclaim, assisting factories) and factory production (engineer count target,
/// scouts, land mix tanks/artillery/AA by enemy air, air factory interceptors/bombers).
/// </summary>
public sealed partial class AiEconomy
{
    private enum Kind : byte { Mex, Pgen, Hydro, LandFactory, AirFactory }

    private readonly AiBrain _b;
    private readonly List<Kind> _opening;
    private int _openingIndex;
    private int[] _depositBlockedUntil = [];
    /// <summary>Per unit slot: think stamp when some own builder was working on it (orphaned sites detection).</summary>
    private int[] _workedStamp = [];
    private int _stamp;
    private int _assignedThisThink;

    // Smoothed economy (EMA over thinks).
    public float MassIncome, EnergyIncome, MassDemand, EnergyDemand;
    public float MassRatio, EnergyRatio;
    public bool MassStall, EnergyStall;
    private bool _avgInit;
    /// <summary>Ticks spent in deep energy/mass stall (efficiency &lt; 0.5), sampled at economy thinks (diagnostics).</summary>
    public int DeepEnergyStallThinks, DeepMassStallThinks, Thinks;
    public int ScoutsOrdered, AirScoutsOrdered;

    private SimWorld W => _b.W;
    private AiConfig Cfg => _b.Cfg;
    private AiView V => _b.View;

    public AiEconomy(AiBrain b)
    {
        _b = b;
        _opening = b.Cfg.Difficulty switch
        {
            AiDifficulty.Easy => [Kind.Pgen, Kind.LandFactory, Kind.Mex, Kind.Mex, Kind.Pgen, Kind.Mex],
            AiDifficulty.Hard => [Kind.LandFactory, Kind.Mex, Kind.Pgen, Kind.Hydro, Kind.Mex, Kind.Pgen, Kind.Mex, Kind.Mex, Kind.Pgen],
            _ => [Kind.LandFactory, Kind.Mex, Kind.Pgen, Kind.Hydro, Kind.Mex, Kind.Pgen, Kind.Mex, Kind.Mex],
        };
    }

    public bool OpeningDone => _openingIndex >= _opening.Count;

    public void Think()
    {
        if (_depositBlockedUntil.Length != W.Terrain.Deposits.Length) _depositBlockedUntil = new int[W.Terrain.Deposits.Length];
        if (_workedStamp.Length != W.Units.Capacity) _workedStamp = new int[W.Units.Capacity];
        _assignedThisThink = 0;
        UpdateAverages();
        UpdateBudget();
        _b.Placement.Cleanup();
        MarkWorkedSites();
        ManageTech();
        ManageFactories();
        ManageCommander();
        ManageEngineers();
    }

    // ------------------------------------------------------------------ economy state

    private void UpdateAverages()
    {
        var eco = W.Armies[_b.Army].Eco;
        if (!_avgInit)
        {
            _avgInit = true;
            MassIncome = eco.MassIncome; EnergyIncome = eco.EnergyIncome;
            MassDemand = eco.MassDemand; EnergyDemand = eco.EnergyDemand;
        }
        const float k = 0.2f;
        MassIncome += (eco.MassIncome - MassIncome) * k;
        EnergyIncome += (eco.EnergyIncome - EnergyIncome) * k;
        MassDemand += (eco.MassDemand - MassDemand) * k;
        EnergyDemand += (eco.EnergyDemand - EnergyDemand) * k;
        MassRatio = eco.MassMax > 0 ? eco.Mass / eco.MassMax : 0;
        EnergyRatio = eco.EnergyMax > 0 ? eco.Energy / eco.EnergyMax : 0;
        EnergyStall = eco.EnergyEfficiency < 0.98f && EnergyRatio < 0.1f;
        MassStall = eco.MassEfficiency < 0.98f && MassRatio < 0.05f;
        if (MassRatio > 0.75f && MassIncome > 8f) _floatThinks++;
        else _floatThinks = Math.Max(0, _floatThinks - 2);
        Thinks++;
        if (eco.EnergyEfficiency < 0.5f) DeepEnergyStallThinks++;
        if (eco.MassEfficiency < 0.5f) DeepMassStallThinks++;
    }

    /// <summary>Upper estimate of the energy the army spends when its builders are busy.</summary>
    public float ProjectedEnergy() => _projectedEnergy; // computed by CountTech (builders by build rate + upkeep + upgrades)

    private int InProgress(int bp) => bp < 0 ? 0 : V.Count[bp] - V.Complete[bp] + _b.Placement.ReservedCount(bp);

    /// <summary>Should a builder make power now?</summary>
    public bool NeedPower()
    {
        int pg = InProgress(_b.R.Pgen[1]) + InProgress(_b.R.Pgen[2]) + InProgress(_b.R.Pgen[3]), hy = InProgress(_b.BpHydro);
        if (pg + hy >= 2 + V.Engineers.Count / 2) return false; // sanity cap on parallel power projects
        float income = EnergyIncome + PendingPower();
        if (EnergyStall) return income < EnergyDemand * 1.2f + 20f;
        // Floating energy (storage nearly full, income covers the actual spending): don't sink mass into power.
        if (EnergyRatio > 0.8f && income > EnergyDemand) return false;
        // Keep income ahead of the real (smoothed) spending, and of what busy builders could spend.
        float want = MathF.Max(EnergyDemand * 1.15f + 10f, ProjectedEnergy() * Cfg.PowerMargin);
        if (income < want) return true;
        return EnergyRatio < 0.3f && income < EnergyDemand * 1.2f;
    }

    /// <summary>Energy income expected from power structures being built (weighted by progress; planned ones count
    /// little because they may not start for a while).</summary>
    private float PendingPower()
    {
        float p = 0;
        int sites = 0;
        var u = W.Units;
        foreach (int s in V.Unfinished)
        {
            var bp = W.BpOf(s);
            if (!bp.Has(UnitCategory.PowerGenerator)) continue;
            p += bp.Economy.ProductionEnergy * (0.3f + 0.7f * u.BuildFraction[s]);
            sites++;
        }
        foreach (var r in _b.Placement.Reserved)
        {
            if (!W.Blueprints[r.Bp].Has(UnitCategory.PowerGenerator)) continue;
            if (sites > 0) { sites--; continue; } // already counted as a site
            p += W.Blueprints[r.Bp].Economy.ProductionEnergy * 0.3f;
        }
        return p;
    }

    /// <summary>Mass has been piling up in storage for a while: spend it (more factories, engineers, defenses).</summary>
    public bool Floating => _floatThinks >= 20;
    private int _floatThinks;

    private int TotalWithReserved(int bp) => bp < 0 ? 0 : V.Count[bp] + _b.Placement.ReservedCount(bp);

    private bool WantLandFactory()
    {
        if (_b.BpLandFactory < 0) return false;
        int n = CountTiers(_b.R.LandFactory) + _b.Placement.ReservedCount(_b.BpLandFactory);
        int cap = Floating ? Math.Min(Cfg.MaxLandFactoriesFloat, Math.Max(Cfg.MaxLandFactories + 3, (int)(MassIncome / 4.5f) + 1)) : Cfg.MaxLandFactories;
        if (n >= cap) return false;
        if (n == 0) return true;
        if (InProgress(_b.BpLandFactory) > 0 || EnergyStall || EnergyRatio < 0.25f) return false;
        if (n < Cfg.MaxLandFactories && MassIncome >= 4.5f * n + 1f) return true;
        return (Floating || MassRatio > 0.7f) && EnergyRatio > 0.5f && _b.GameTime > 90;
    }

    private bool WantAirFactory()
    {
        if (_b.BpAirFactory < 0 || Cfg.MaxAirFactories <= 0) return false;
        int n = CountTiers(_b.R.AirFactory) + _b.Placement.ReservedCount(_b.BpAirFactory);
        if (n >= Cfg.MaxAirFactories || InProgress(_b.BpAirFactory) > 0) return false;
        if (V.LandFactories.Count < 2 || EnergyStall) return false;
        bool time = _b.GameTime >= Cfg.AirFactoryTime * (1 + n) || (n == 0 && _b.Perception.EnemyAirCountMem > 2 && _b.GameTime > 180);
        return time && MassIncome >= 10f + 8f * n && EnergyIncome >= 140f;
    }

    // ------------------------------------------------------------------ factories

    /// <summary>Factories allowed to start new units this think (the others let their queues run out), so that the
    /// planned mass spending stays near income and engineers keep building eco at full speed.</summary>
    public float FactoryBudget;

    private void ManageFactories()
    {
        var eco = W.Armies[_b.Army].Eco;
        float budget = MassIncome * Cfg.SpendRatio + (MassRatio > 0.3f ? eco.Mass / 60f : 0f);
        int busy = 0;
        foreach (int e in V.Engineers) if (W.Units.Builder[e].Mode is BuilderMode.Build or BuilderMode.Repair) busy++;
        float engineerSpend = busy * 2.5f + (V.Commander >= 0 && W.Units.Builder[V.Commander].Mode == BuilderMode.Build ? 5f : 0f);
        // Budget in T1-land-factory equivalents (4 mass/s each); bigger factories weigh by build rate.
        FactoryBudget = MathF.Max(1f, (budget - engineerSpend * 0.7f - UpgradeSpend * 0.8f) / 4f);
        if (Floating) FactoryBudget += 2;
        FactoryBudget = MathF.Max(FactoryBudget, budget * Cfg.ArmyShare / 4f); // army-first floor (Budget.cs)
        float slot = 0;
        bool airFirst = _b.Perception.EnemyAirCountMem > 4;
        if (airFirst) foreach (int f in V.AirFactories) Factory(f, air: true, (slot += FactoryWeight(f)) <= FactoryBudget + 0.5f);
        foreach (int f in V.LandFactories) Factory(f, air: false, (slot += FactoryWeight(f)) <= FactoryBudget + 0.5f);
        if (!airFirst) foreach (int f in V.AirFactories) Factory(f, air: true, (slot += FactoryWeight(f)) <= FactoryBudget + 0.5f);
        _b.Navy.ManageFactories(ref slot, FactoryBudget + 0.5f);
        ManageGateways(ref slot);
    }

    private void Factory(int f, bool air, bool allowed)
    {
        ref var info = ref _b.Info(f);
        if (!info.RallySet)
        {
            info.RallySet = true;
            info.Role = AiRole.Factory;
            _b.Cmd.Order(f, Order.MoveTo(air ? _b.AirRally : _b.Rally));
        }
        var q = W.Units.Factory[f].Queue;
        int len = q?.Count ?? 0;
        if (len >= Cfg.QueueDepth) return;
        // Over the mass budget: only engineers (when short of them) may still be queued here.
        if (!allowed && (len > 0 || _b.BpEngineer < 0 || air || TotalEngineers() >= EngineerTarget())) return;
        if (len >= 1 && (EnergyStall || MassStall)) return;
        // Deep energy stall: let the power catch up instead of starting more units (unless we lack engineers).
        if (EnergyStall && EnergyIncome < EnergyDemand * 0.6f && TotalEngineers() >= 2) return;
        var army = W.Armies[_b.Army];
        if (army.UnitCount + 4 >= army.UnitCap) return;
        int bp = air ? ChooseAir(f) : ChooseLand(f);
        if (bp < 0) return;
        _b.Cmd.Enqueue(f, bp);
        V.Queued[bp]++;
    }

    public int EngineerTarget()
    {
        if (_b.GameTime < 150) return Cfg.EarlyEngineers;
        int t = Cfg.EarlyEngineers + (int)(MassIncome / 5f);
        return Math.Min(Cfg.MaxEngineers + (Floating ? 2 : 0), t + (Floating ? 2 : 0));
    }

    private int ChooseLand(int factory)
    {
        if (_b.BpEngineer >= 0 && TotalEngineers() < EngineerTarget()) return ChooseEngineer(factory);
        if (_b.Scouting.WantLandScout())
        {
            ScoutsOrdered++;
            _b.Scouting.LandScoutOrdered();
            return _b.BpScout;
        }
        var fbp = W.BpOf(factory);
        int tier = Math.Min(MaxTier, fbp.Tech);
        var r = _b.R;
        float airMem = _b.Perception.EnemyAirStrengthMem;
        bool enemyAir = _b.Perception.EnemyAirCountMem > 0.5f || V.EnemyHasAirFactory;
        float aa = enemyAir ? Math.Clamp(0.1f + airMem / (airMem + MathF.Max(V.EnemyLandStrength, _b.Perception.EnemyLandEstimate) + 200f), 0.12f, 0.4f) : 0f;
        float arty = _b.GameTime > 150 ? Cfg.ArtilleryShare : 0.1f;
        float shield = tier >= 2 && AiRoster.CanBuild(fbp, r.MobileShield2) ? 0.06f : 0f;
        float tank = 1f - aa - arty - shield;
        // Shares by mass over every tier (a heavy tank counts as much as 3.5 light ones).
        float mT = Mass(r.Tank1) + Mass(r.HeavyTank2) + Mass(r.Assault3) + Mass(r.Siege3) + Mass(r.Amphibious2);
        float mA = Mass(r.Artillery1) + Mass(r.Mml2) + Mass(r.Artillery3);
        float mAA = Mass(r.MobileAa1) + Mass(r.MobileAa2);
        float mS = Mass(r.MobileShield2);
        float n = mT + mA + mAA + mS + 1f;
        float dT = tank - mT / n, dA = arty - mA / n, dAA = aa - mAA / n, dS = shield - mS / n;
        if (dS > 0.03f && dS > dT && dS > dA && dS > dAA) return r.MobileShield2;
        if (dAA > dT && dAA > dA)
        {
            int b = Pick(fbp, tier >= 2 ? r.MobileAa2 : -1, r.MobileAa1);
            if (b >= 0) return b;
        }
        if (dA > dT)
        {
            int b = Pick(fbp, tier >= 3 ? r.Artillery3 : -1, tier >= 2 ? r.Mml2 : -1, r.Artillery1);
            if (b >= 0) return b;
        }
        int heavy3 = tier >= 3 && V.Total(r.Siege3) * 2 < V.Total(r.Assault3) + 1 ? r.Siege3 : -1;
        int d = Pick(fbp, heavy3, tier >= 3 ? r.Assault3 : -1, tier >= 2 ? r.HeavyTank2 : -1, r.Tank1);
        return d >= 0 ? d : Pick(fbp, r.Artillery1, r.Mml2);
    }

    private float Mass(int bp) => bp < 0 ? 0f : V.Total(bp) * W.Blueprints[bp].Economy.BuildCostMass;

    /// <summary>First of the candidates the factory can build (-1 entries skipped).</summary>
    private static int Pick(UnitBlueprint f, int a, int b = -1, int c = -1, int d = -1)
    {
        if (AiRoster.CanBuild(f, a)) return a;
        if (AiRoster.CanBuild(f, b)) return b;
        if (AiRoster.CanBuild(f, c)) return c;
        if (AiRoster.CanBuild(f, d)) return d;
        return -1;
    }

    private int ChooseAir(int factory)
    {
        if (_b.Scouting.WantAirScout())
        {
            AirScoutsOrdered++;
            _b.Scouting.AirScoutOrdered();
            return _b.BpAirScout;
        }
        int transport = _b.Transport.ChooseAir(factory);
        if (transport >= 0) return transport;
        var fbp = W.BpOf(factory);
        int tier = Math.Min(MaxTier, fbp.Tech);
        var r = _b.R;
        int inter = V.Total(r.Interceptor1) + V.Total(r.AirSuperiority3);
        int bomb = V.Total(r.Bomber1) + V.Total(r.Gunship2) + V.Total(r.FighterBomber2) + V.Total(r.StratBomber3);
        int airCount = (int)MathF.Ceiling(_b.Perception.EnemyAirCountMem);
        int wantInter = Math.Min(24, 2 + airCount + (int)(_b.Perception.EnemyAirStrengthMem / 200f));
        int fighter = Pick(fbp, tier >= 3 ? r.AirSuperiority3 : -1, r.Interceptor1);
        if (fighter >= 0 && inter < wantInter) return fighter;
        if (bomb < 12 && bomb <= inter + 2)
        {
            int strat = tier >= 3 && V.Total(r.StratBomber3) * 3 < bomb + 1 ? r.StratBomber3 : -1;
            int t2 = V.Total(r.Gunship2) <= V.Total(r.FighterBomber2) ? r.Gunship2 : r.FighterBomber2;
            int b = Pick(fbp, strat, tier >= 2 ? t2 : -1, r.Bomber1);
            if (b >= 0) return b;
        }
        return fighter >= 0 ? fighter : Pick(fbp, r.Bomber1, r.Gunship2);
    }

    // ------------------------------------------------------------------ builders

    private void MarkWorkedSites()
    {
        _stamp++;
        var u = W.Units;
        void Mark(int i)
        {
            ref var b = ref u.Builder[i];
            if (b.Mode != BuilderMode.None && u.IsActive(b.Target)) _workedStamp[b.Target.Index] = _stamp;
            // Units walking to finish a site count as working on it.
            var q = u.Orders[i];
            if (!q.IsEmpty && q.Current.Type is OrderType.Repair or OrderType.Guard && u.IsActive(q.Current.Target))
                _workedStamp[q.Current.Target.Index] = _stamp;
            if (!q.IsEmpty && q.Current.Type == OrderType.Build && u.IsActive(q.Current.Spawned))
                _workedStamp[q.Current.Spawned.Index] = _stamp;
        }
        foreach (int e in V.Engineers) Mark(e);
        if (V.Commander >= 0) Mark(V.Commander);
    }

    private void ManageCommander()
    {
        int c = V.Commander;
        if (c < 0) return;
        ref var info = ref _b.Info(c);
        info.Role = AiRole.Builder;
        if (info.HoldUntil > W.Tick) return; // the military side is moving it (retreat)
        if (W.Units.Upgrade[c].Active) return; // building an enhancement: any order would cancel it
        var q = W.Units.Orders[c];
        if (!OpeningDone)
        {
            // Keep up to two opening orders queued so the commander never idles between them.
            int pending = q.Count, attempts = 0;
            while (!OpeningDone && pending <= 1 && attempts++ < 4)
                if (IssueOpening(c, _opening[_openingIndex++], queue: pending > 0)) pending++;
            return;
        }
        bool idle = q.IsEmpty;
        if (!idle && W.Tick > info.TaskDeadline && info.Task is AiTask.Build or AiTask.Finish or AiTask.Reclaim or AiTask.Return)
            idle = true;
        if (!idle && info.Task == AiTask.Assist && (W.Tick - info.TaskTick > 200 || MassStall || EnergyStall || NeedPower()))
            idle = true;
        if (!idle) return;
        if (!TryAssign(c, isCommander: true)) GoHome(c, 6f);
    }

    private bool IssueOpening(int c, Kind kind, bool queue)
    {
        var from = _b.BasePos;
        switch (kind)
        {
            case Kind.Mex: return TryMex(c, true, queue, from);
            case Kind.Hydro: return TryHydro(c, true, queue, from);
            case Kind.Pgen: return TryBuildInBase(c, _b.BpPgen, -_b.EnemyDir, 7, 26, queue);
            case Kind.LandFactory: return TryBuildInBase(c, _b.BpLandFactory, _b.EnemyDir, 9, 30, queue);
            case Kind.AirFactory: return TryBuildInBase(c, _b.BpAirFactory, Perp(_b.EnemyDir), 10, 34, queue);
        }
        return false;
    }

    private static Vector2 Perp(Vector2 d) => new(-d.Y, d.X);

    private void ManageEngineers()
    {
        var u = W.Units;
        var rng = _b.Rng(0xE61);
        int maxAssign = Cfg.Difficulty == AiDifficulty.Hard ? 6 : 4;
        foreach (int e in V.Engineers)
        {
            ref var info = ref _b.Info(e);
            if (info.HoldUntil > W.Tick) continue;
            // Safety: engineers away from home flee from enemy firepower (and avoid that deposit for a while).
            var epos = u.Pos[e];
            if (Vector2.DistanceSquared(epos, _b.BasePos) > 30f * 30f && V.ThreatAt(epos, 12f) > 0)
            {
                if (info.Deposit >= 0) _depositBlockedUntil[info.Deposit] = W.Tick + 900;
                info.Role = AiRole.Builder;
                GoHome(e, 10f);
                info.HoldUntil = W.Tick + 80;
                continue;
            }
            bool fresh = info.Role != AiRole.Builder;
            if (fresh) { info.Role = AiRole.Builder; info.Task = AiTask.None; }
            var q = u.Orders[e];
            bool idle = fresh || q.IsEmpty;
            if (!idle)
            {
                if (W.Tick > info.TaskDeadline && info.Task is AiTask.Build or AiTask.Finish or AiTask.Reclaim or AiTask.Return or AiTask.Repair)
                {
                    if (info.Deposit >= 0) _depositBlockedUntil[info.Deposit] = W.Tick + 1200;
                    idle = true;
                }
                else if (info.Task == AiTask.Assist && (W.Tick - info.TaskTick > 250 || MassStall || EnergyStall))
                    idle = true;
            }
            if (!idle) continue;
            if (!fresh && Cfg.IdlePickupChance < 1f && !rng.Chance(Cfg.IdlePickupChance)) continue;
            if (_assignedThisThink >= maxAssign) break;
            _assignedThisThink++;
            if (!TryAssign(e, isCommander: false))
            {
                GoHome(e, 10f);
                info.HoldUntil = W.Tick + 30;
            }
        }
    }

    public void GoHome(int unit, float radius)
    {
        ref var info = ref _b.Info(unit);
        var u = W.Units;
        if (Vector2.Distance(u.Pos[unit], _b.BasePos) <= radius + 4f)
        {
            if (info.Task == AiTask.Assist || info.Task == AiTask.Return) return;
            if (!u.Orders[unit].IsEmpty) _b.Cmd.Stop(unit);
            info.Task = AiTask.None;
            return;
        }
        var rng = _b.Rng(unit);
        var p = _b.FindStandable(_b.BasePos + DMath.Dir(rng.Range(0f, DMath.TwoPi)) * rng.Range(3f, radius), MotionLayer.Amphibious);
        _b.Cmd.Order(unit, Order.MoveTo(p));
        SetTask(unit, AiTask.Return, p, 0, -1);
    }

    public void SetTask(int unit, AiTask task, Vector2 at, float workTime, int deposit, bool queue = false)
    {
        ref var info = ref _b.Info(unit);
        var u = W.Units;
        var bp = W.BpOf(unit);
        float speed = MathF.Max(0.5f, bp.Movement.MaxSpeed);
        float travel = Vector2.Distance(u.Pos[unit], at) / speed;
        int ticks = (int)((travel * 1.8f + workTime * 1.5f) * SimWorld.TicksPerSecond) + 200;
        info.Task = task;
        info.TaskTick = W.Tick;
        info.TaskDeadline = queue ? Math.Max(info.TaskDeadline, W.Tick) + ticks : W.Tick + ticks;
        if (!queue || deposit >= 0) info.Deposit = deposit;
    }

    /// <summary>Issues a build order (and reserves the site) for a builder.</summary>
    public void IssueBuild(int unit, int bp, Vector2 site, int deposit, bool queue)
    {
        var sbp = W.Blueprints[bp];
        float heading = sbp.Has(UnitCategory.Factory) ? DMath.HeadingOf(_b.EnemyDir) : 0f;
        _b.Cmd.Order(unit, Order.BuildAt(bp, site, heading), queue);
        _b.Placement.Reserve(bp, site, unit, deposit);
        if (sbp.Has(UnitCategory.Experimental)) _b.Stats.Experimentals++;
        float rate = MathF.Max(1f, W.BpOf(unit).Economy.BuildRate);
        SetTask(unit, AiTask.Build, site, sbp.Economy.BuildTime / rate, deposit, queue);
    }

    /// <summary>Picks and issues the best job for an idle builder. Returns false if there is nothing to do.</summary>
    private bool TryAssign(int unit, bool isCommander)
    {
        var pos = W.Units.Pos[unit];
        if (TryFinishOrphan(unit, isCommander)) return true;
        if (NeedPower())
        {
            if (TryHydro(unit, isCommander, false, pos)) return true;
            int pg = ChoosePgen(W.BpOf(unit));
            if (pg >= 0 && TryPowerNearFactory(unit, pg, isCommander)) return true;
            if (TryBuildInBase(unit, pg, -_b.EnemyDir, pg == _b.BpPgen ? 7 : 10, isCommander ? Cfg.CommanderRadius : 40, false)) return true;
        }
        if (EnergyStall) return TryAssistProject(unit, isCommander, powerOnly: true);
        if (TryMex(unit, isCommander, false, pos)) return true;
        if (TryHydro(unit, isCommander, false, pos)) return true;
        if (MassRatio < 0.25f && TryReclaim(unit, isCommander, 6f)) return true;
        if (!MassStall)
        {
            if (WantLandFactory() && TryBuildInBase(unit, _b.BpLandFactory, _b.EnemyDir, 9, isCommander ? Cfg.CommanderRadius : 40, false)) return true;
            if (WantAirFactory() && TryBuildInBase(unit, _b.BpAirFactory, Perp(_b.EnemyDir), 10, isCommander ? Cfg.CommanderRadius : 42, false)) return true;
            if (_b.Navy.TryBuild(unit, isCommander)) return true;
            if (TryAssistUpgrade(unit, isCommander)) return true;
            if (TryCapture(unit, isCommander)) return true;
            if (TryDefense(unit, isCommander)) return true;
            if (TryProjects(unit, isCommander)) return true;
            if (_b.Transport.TryBuild(unit, isCommander)) return true;
            if (TryStorageOrRadar(unit, isCommander)) return true;
        }
        if (TryRepair(unit, isCommander)) return true;
        if (TryReclaim(unit, isCommander, 3f)) return true;
        if (TryAssistProject(unit, isCommander, powerOnly: false)) return true;
        if (TryAssistFactory(unit)) return true;
        return false;
    }

    private bool InBuilderRange(bool isCommander, Vector2 p) =>
        !isCommander || Vector2.DistanceSquared(p, _b.BasePos) <= Cfg.CommanderRadius * Cfg.CommanderRadius;

    /// <summary>Own construction sites nobody is working on (builder died, order dropped): finish them.</summary>
    private bool TryFinishOrphan(int unit, bool isCommander)
    {
        var u = W.Units;
        int best = -1;
        float bd = float.MaxValue;
        foreach (int s in V.Unfinished)
        {
            if (_workedStamp[s] == _stamp) continue;
            if (!InBuilderRange(isCommander, u.Pos[s])) continue;
            if (V.ThreatAt(u.Pos[s], 20f) > 0) continue;
            float d = Vector2.DistanceSquared(u.Pos[s], u.Pos[unit]);
            if (d < bd) { bd = d; best = s; }
        }
        if (best < 0) return false;
        _b.Cmd.Order(unit, Order.RepairUnit(u.HandleOf(best)));
        _workedStamp[best] = _stamp;
        var sbp = W.BpOf(best);
        SetTask(unit, AiTask.Finish, u.Pos[best], sbp.Economy.BuildTime * (1 - u.BuildFraction[best]) / MathF.Max(1, W.BpOf(unit).Economy.BuildRate), -1);
        return true;
    }

    /// <summary>Helps an own structure under construction (big projects, or power when energy stalls).</summary>
    private bool TryAssistProject(int unit, bool isCommander, bool powerOnly)
    {
        var u = W.Units;
        int best = -1;
        float bd = float.MaxValue;
        foreach (int s in V.Unfinished)
        {
            var sbp = W.BpOf(s);
            bool power = sbp.Has(UnitCategory.PowerGenerator);
            if (powerOnly ? !power : sbp.Economy.BuildTime < 250 && !power) continue;
            if (!InBuilderRange(isCommander, u.Pos[s])) continue;
            float d = Vector2.DistanceSquared(u.Pos[s], u.Pos[unit]);
            if (d > 45f * 45f) continue;
            if (d < bd) { bd = d; best = s; }
        }
        if (best < 0) return false;
        _b.Cmd.Order(unit, Order.RepairUnit(u.HandleOf(best)));
        var b = W.BpOf(best);
        SetTask(unit, AiTask.Finish, u.Pos[best], b.Economy.BuildTime / MathF.Max(1, W.BpOf(unit).Economy.BuildRate), -1);
        return true;
    }

    /// <summary>Mass extractor on the free deposit nearest the base (engineers chain a few more nearby).</summary>
    private bool TryMex(int unit, bool isCommander, bool queue, Vector2 from)
    {
        if (_b.BpMex < 0) return false;
        int d = BestDeposit(DepositType.Mass, unit, isCommander, from, -1, out var site);
        if (d < 0) return false;
        IssueBuild(unit, _b.BpMex, site, d, queue);
        if (!isCommander && !queue)
        {
            var last = W.Terrain.Deposits[d].Pos;
            for (int c = 0; c < Cfg.MexChain; c++)
            {
                int n = BestDeposit(DepositType.Mass, unit, false, last, 30f, out var s2);
                if (n < 0) break;
                IssueBuild(unit, _b.BpMex, s2, n, queue: true);
                last = W.Terrain.Deposits[n].Pos;
            }
        }
        return true;
    }

    private bool TryHydro(int unit, bool isCommander, bool queue, Vector2 from)
    {
        if (_b.BpHydro < 0) return false;
        int d = BestDeposit(DepositType.Hydrocarbon, unit, isCommander, from, -1, out var site);
        if (d < 0) return false;
        IssueBuild(unit, _b.BpHydro, site, d, queue);
        return true;
    }

    /// <summary>
    /// Free deposit of the type with the best score (distance to base first, then to the builder), not reserved,
    /// not blacklisted, not threatened, reachable, not inside an enemy base. maxFromAnchor &gt; 0 limits the distance
    /// from 'from' (mex chains).
    /// </summary>
    private int BestDeposit(DepositType type, int unit, bool isCommander, Vector2 from, float maxFromAnchor, out Vector2 site)
    {
        site = default;
        var deps = W.Terrain.Deposits;
        int bpx = type == DepositType.Mass ? _b.BpMex : _b.BpHydro;
        if (bpx < 0) return -1;
        var unitBp = W.BpOf(unit);
        int best = -1;
        float bestScore = float.MaxValue;
        for (int d = 0; d < deps.Length; d++)
        {
            if (deps[d].Type != type) continue;
            var p = deps[d].Pos;
            if (_depositBlockedUntil[d] > W.Tick) continue;
            if (!InBuilderRange(isCommander, p)) continue;
            float dBase = Vector2.Distance(p, _b.BasePos);
            float dFrom = Vector2.Distance(p, from);
            if (maxFromAnchor > 0 && dFrom > maxFromAnchor) continue;
            float score = dBase + 0.5f * dFrom;
            // Deposits closer to an enemy base than to ours are expensive to hold: only when nothing better is left.
            float dEnemy = float.MaxValue;
            foreach (var es in _b.EnemyStarts) dEnemy = MathF.Min(dEnemy, Vector2.Distance(es, p));
            if (dEnemy < 45f && dEnemy < dBase) continue;
            if (dEnemy < dBase) score += (dBase - dEnemy) * 1.5f + 40f;
            if (score >= bestScore) continue;
            if (_b.Placement.IsDepositReserved(d)) continue;
            if (!_b.Placement.DepositFree(d, bpx, out var s)) continue;
            if (V.ThreatAt(p, 24f) > 0) continue;
            if (!_b.CanReachNear(unitBp.Movement.Layer, unitBp.Radius, W.Units.Pos[unit], p, 1.5f)) continue;
            best = d;
            bestScore = score;
            site = s;
        }
        return best;
    }

    /// <summary>Debug: one line per mass/hydro deposit (distance to base, occupant, blacklist, threat, reachability).</summary>
    public string DescribeDeposits()
    {
        var sb = new System.Text.StringBuilder();
        var deps = W.Terrain.Deposits;
        var u = W.Units;
        for (int d = 0; d < deps.Length; d++)
        {
            var p = deps[d].Pos;
            int occ = W.Occupancy.At((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));
            string who = occ < 0 ? "free" : u.Army[occ] == _b.Army ? (u.IsComplete(occ) ? "OURS" : "ours-site") : $"army{u.Army[occ]}";
            int bpx = deps[d].Type == DepositType.Mass ? _b.BpMex : _b.BpHydro;
            bool looksFree = bpx >= 0 && _b.Placement.DepositFree(d, bpx, out _);
            bool reach = _b.CanReachNear(MotionLayer.Amphibious, 0.5f, _b.BasePos, p, 1.5f);
            sb.Append($"    dep{d} {deps[d].Type} ({p.X:F0},{p.Y:F0}) dBase={Vector2.Distance(p, _b.BasePos):F0} {who} looksFree={looksFree} " +
                      $"blocked={Math.Max(0, _depositBlockedUntil.Length > d ? _depositBlockedUntil[d] - W.Tick : 0) / 10}s threat={V.ThreatAt(p, 24f):F0} reserved={_b.Placement.IsDepositReserved(d)} reach={reach}\n");
        }
        return sb.ToString();
    }

    /// <summary>Base structure at a free spot found by spiral search (preferred direction from the base center).</summary>
    private bool TryBuildInBase(int unit, int bp, Vector2 dir, float minR, float maxR, bool queue)
    {
        if (bp < 0) return false;
        if (!_b.Placement.FindSite(bp, _b.BasePos + dir * (minR * 0.5f), dir, minR * 0.5f, maxR + 8f, out var site, cooldownTicks: 400)) return false;
        IssueBuild(unit, bp, site, -1, queue);
        return true;
    }

    private const UnitCategory PdCat = UnitCategory.Defense | UnitCategory.DirectFire;
    private const UnitCategory AaCat = UnitCategory.Defense | UnitCategory.AntiAir;

    private bool TryDefense(int unit, bool isCommander)
    {
        var ubp = W.BpOf(unit);
        // Anti-air at the base once the enemy has (or builds) aircraft; bigger tiers with income.
        int airSeen = (int)MathF.Ceiling(_b.Perception.EnemyAirCountMem);
        int aaBp = BestBuildable(_b.R.AntiAir, ubp, Math.Min(MaxTier, MassIncome > 60f || airSeen > 12 ? 3 : 2));
        if (aaBp >= 0 && (airSeen > 0 || V.EnemyHasAirFactory) && MassRatio > 0.1f)
        {
            int want = Math.Min(Cfg.BaseAntiAir + (MassIncome > 40f ? 2 : 0), 1 + airSeen / 3);
            if (CountCat(AaCat) < want)
            {
                var dir = DMath.Dir(_b.Rng(0xAA).Range(0f, DMath.TwoPi));
                if (TryBuildInBase(unit, aaBp, dir, 10, isCommander ? Cfg.CommanderRadius : 30, false)) return true;
            }
        }
        int pdBp = BestBuildable(_b.R.PointDefense, ubp, Math.Min(MaxTier, MassIncome > 60f ? 3 : MassIncome > 20f ? 2 : 1));
        if (pdBp < 0) return false;
        // Point defense at the base, facing the enemy.
        int extra = (Floating ? 2 : 0) + (MassIncome > 30f ? 1 : 0);
        if (_b.GameTime > 300 && MassRatio > 0.3f && CountNearCat(PdCat, _b.BasePos, 35f) + ReservedCat(PdCat) < Cfg.BasePointDefense + extra)
        {
            var side = Perp(_b.EnemyDir) * (_b.Rng(0xBD).Chance(0.5f) ? 1 : -1);
            if (TryBuildInBase(unit, pdBp, Vector2.Normalize(_b.EnemyDir * 2f + side), 22, isCommander ? Cfg.CommanderRadius : 32, false)) return true;
        }
        // Point defense next to outlying mass extractors.
        if (isCommander || _b.GameTime < 420 || MassRatio < 0.4f || Cfg.ExpansionPointDefense <= 0) return false;
        int expansionPd = CountCat(PdCat) - CountNearCat(PdCat, _b.BasePos, 35f);
        if (expansionPd >= Cfg.ExpansionPointDefense + (Floating ? 2 : 0)) return false;
        var u = W.Units;
        int bestMex = -1;
        float bd = float.MaxValue;
        foreach (int s in V.Structures)
        {
            if (!W.BpOf(s).Has(UnitCategory.MassExtractor) || !u.IsComplete(s)) continue;
            var p = u.Pos[s];
            float dBase = Vector2.Distance(p, _b.BasePos);
            if (dBase < 45f) continue;
            if (CountNearCat(PdCat, p, 16f) > 0) continue;
            float d = Vector2.DistanceSquared(p, u.Pos[unit]);
            if (d < bd) { bd = d; bestMex = s; }
        }
        if (bestMex < 0) return false;
        var mp = u.Pos[bestMex];
        if (!_b.Placement.FindSite(pdBp, mp, _b.EnemyStarts.Count > 0 ? _b.EnemyStarts[0] - mp : _b.EnemyDir, 3.5f, 10f, out var site)) return false;
        IssueBuild(unit, pdBp, site, -1, false);
        return true;
    }

    private int CountNearCat(UnitCategory cat, Vector2 p, float r)
    {
        var u = W.Units;
        int n = 0;
        foreach (int s in V.Structures)
            if (W.BpOf(s).Has(cat) && Vector2.DistanceSquared(u.Pos[s], p) <= r * r) n++;
        return n;
    }

    private int ReservedCat(UnitCategory cat)
    {
        int n = 0;
        foreach (var r in _b.Placement.Reserved) if (W.Blueprints[r.Bp].Has(cat)) n++;
        return n;
    }

    private int CountTiers(int[] tiers)
    {
        int n = 0;
        for (int t = 1; t <= 3; t++) if (tiers[t] >= 0) n += V.Count[tiers[t]];
        return n;
    }

    private int CountNear(int bp, Vector2 p, float r)
    {
        if (bp < 0) return 0;
        var u = W.Units;
        int n = 0;
        foreach (int s in V.Structures)
            if (u.Bp[s] == bp && Vector2.DistanceSquared(u.Pos[s], p) <= r * r) n++;
        return n;
    }

    private bool TryStorageOrRadar(int unit, bool isCommander)
    {
        float maxR = isCommander ? Cfg.CommanderRadius : 34;
        if (_b.BpMassStorage >= 0 && MassRatio > 0.9f && MassIncome > 10f && TotalWithReserved(_b.BpMassStorage) < Cfg.MaxMassStorage)
            if (TryBuildInBase(unit, _b.BpMassStorage, -_b.EnemyDir, 8, maxR, false)) return true;
        if (_b.BpEnergyStorage >= 0 && EnergyRatio > 0.95f && EnergyIncome > 150f && TotalWithReserved(_b.BpEnergyStorage) < Cfg.MaxEnergyStorage)
            if (TryBuildInBase(unit, _b.BpEnergyStorage, -_b.EnemyDir, 8, maxR, false)) return true;
        return TryRadar(unit, isCommander);
    }

    /// <summary>Radar coverage: one at the base, then next to outlying extractors no own radar covers yet.</summary>
    private bool TryRadar(int unit, bool isCommander)
    {
        if (_b.BpRadar < 0 || _b.GameTime < Cfg.RadarTime || EnergyIncome < 60f || EnergyStall) return false;
        int have = TotalWithReserved(_b.BpRadar);
        if (have >= Cfg.MaxRadars) return false;
        var u = W.Units;
        float cover = MathF.Max(20f, W.Blueprints[_b.BpRadar].Intel.RadarRadius * 0.7f);
        if (have == 0) return TryBuildInBase(unit, _b.BpRadar, _b.EnemyDir, 12, isCommander ? Cfg.CommanderRadius : 34, false);
        if (isCommander || MassStall) return false;
        int best = -1;
        float bd = float.MaxValue;
        foreach (int s in V.Structures)
        {
            if (!W.BpOf(s).Has(UnitCategory.MassExtractor) || !u.IsComplete(s)) continue;
            var p = u.Pos[s];
            if (Covered(p, cover)) continue;
            if (V.ThreatAt(p, 20f) > 0) continue;
            float d = Vector2.DistanceSquared(p, u.Pos[unit]);
            if (d < bd) { bd = d; best = s; }
        }
        if (best < 0) return false;
        var mp = u.Pos[best];
        if (!_b.Placement.FindSite(_b.BpRadar, mp, _b.BasePos - mp, 3.5f, 10f, out var site)) return false;
        IssueBuild(unit, _b.BpRadar, site, -1, false);
        return true;
    }

    private bool Covered(Vector2 p, float cover)
    {
        var u = W.Units;
        foreach (int s in V.Structures)
            if (u.Bp[s] == _b.BpRadar && Vector2.DistanceSquared(u.Pos[s], p) < cover * cover) return true;
        foreach (var r in _b.Placement.Reserved)
            if (r.Bp == _b.BpRadar && Vector2.DistanceSquared(r.Pos, p) < cover * cover) return true;
        return false;
    }

    /// <summary>Repair damaged own structures/commander/units near the base.</summary>
    private bool TryRepair(int unit, bool isCommander)
    {
        var u = W.Units;
        int best = -1;
        float bd = float.MaxValue;
        void Consider(int s)
        {
            if (s == unit || !u.IsActive(s) || !u.IsComplete(s)) return;
            var bp = W.BpOf(s);
            if (u.Health[s] > bp.MaxHealth * 0.75f) return;
            var p = u.Pos[s];
            if (!InBuilderRange(isCommander, p) || Vector2.DistanceSquared(p, _b.BasePos) > 50f * 50f) return;
            if (_workedStamp[s] == _stamp) return;
            if (V.ThreatAt(p, 18f) > 0 && !bp.Has(UnitCategory.Commander)) return;
            float d = Vector2.DistanceSquared(p, u.Pos[unit]);
            if (bp.Has(UnitCategory.Commander)) d *= 0.25f;
            if (d < bd) { bd = d; best = s; }
        }
        foreach (int s in V.Structures) Consider(s);
        if (V.Commander >= 0 && !isCommander) Consider(V.Commander);
        foreach (int s in V.LandCombat) Consider(s);
        if (best < 0) return false;
        _b.Cmd.Order(unit, Order.RepairUnit(u.HandleOf(best)));
        _workedStamp[best] = _stamp;
        SetTask(unit, AiTask.Repair, u.Pos[best], 20f, -1);
        return true;
    }

    /// <summary>Reclaims the most valuable nearby wreck (mass per distance).</summary>
    private bool TryReclaim(int unit, bool isCommander, float minMass)
    {
        var props = W.Props;
        if (props.Count == 0) return false;
        var u = W.Units;
        var pos = u.Pos[unit];
        float radius = isCommander ? Cfg.CommanderRadius : 45f;
        int best = -1;
        float bestScore = 0;
        foreach (int p in props.AliveList)
        {
            float m = props.Mass[p];
            if (m < minMass) continue;
            var pp = props.Pos[p];
            if (isCommander ? Vector2.DistanceSquared(pp, _b.BasePos) > radius * radius : Vector2.DistanceSquared(pp, pos) > radius * radius) continue;
            if (!_b.Perception.CanSeeProp(p)) continue;
            float score = m / (8f + Vector2.Distance(pp, pos));
            if (score <= bestScore) continue;
            if (ClaimedProp(p, unit)) continue;
            if (V.ThreatAt(pp, 20f) > 0) continue;
            best = p;
            bestScore = score;
        }
        if (best < 0) return false;
        _b.Cmd.Order(unit, Order.ReclaimAt(UnitHandle.None, props.Pos[best]));
        // Queue further wrecks around it (reclaim fields).
        int queued = 0;
        foreach (int p in props.AliveList)
        {
            if (queued >= 3) break;
            if (p == best || props.Mass[p] < minMass) continue;
            if (Vector2.DistanceSquared(props.Pos[p], props.Pos[best]) > 12f * 12f) continue;
            _b.Cmd.Order(unit, Order.ReclaimAt(UnitHandle.None, props.Pos[p]), queue: true);
            queued++;
        }
        SetTask(unit, AiTask.Reclaim, props.Pos[best], 10f + 8f * queued, -1);
        return true;
    }

    private bool ClaimedProp(int prop, int self)
    {
        var u = W.Units;
        var pp = W.Props.Pos[prop];
        foreach (int e in V.Engineers)
        {
            if (e == self) continue;
            var q = u.Orders[e];
            if (q.IsEmpty || q.Current.Type != OrderType.Reclaim) continue;
            if (Vector2.DistanceSquared(q.Current.Pos, pp) < 1f) return true;
        }
        return false;
    }

    /// <summary>Assist (guard) the factory with the longest queue and the fewest helpers when resources allow.</summary>
    private bool TryAssistFactory(int unit)
    {
        if (MassStall || EnergyStall || MassRatio < 0.08f || EnergyRatio < 0.2f) return false;
        var u = W.Units;
        int best = -1;
        float bestScore = float.MaxValue;
        foreach (int f in V.LandFactories)
            Score(f);
        foreach (int f in V.AirFactories)
            Score(f);
        void Score(int f)
        {
            var q = u.Factory[f].Queue;
            if (q == null || q.Count == 0) return;
            int helpers = 0;
            foreach (int e in V.Engineers)
            {
                var eq = u.Orders[e];
                if (!eq.IsEmpty && eq.Current.Type == OrderType.Guard && eq.Current.Target.Index == f) helpers++;
            }
            if (V.Commander >= 0)
            {
                var cq = u.Orders[V.Commander];
                if (!cq.IsEmpty && cq.Current.Type == OrderType.Guard && cq.Current.Target.Index == f) helpers++;
            }
            float s = helpers * 40f + Vector2.Distance(u.Pos[f], u.Pos[unit]);
            if (s < bestScore) { bestScore = s; best = f; }
        }
        if (best < 0) return false;
        ref var info = ref _b.Info(unit);
        var cur = u.Orders[unit];
        if (!cur.IsEmpty && cur.Current.Type == OrderType.Guard && cur.Current.Target.Index == best)
        {
            info.TaskTick = W.Tick; // already assisting it: keep going
            return true;
        }
        _b.Cmd.Order(unit, Order.GuardUnit(u.HandleOf(best)));
        SetTask(unit, AiTask.Assist, u.Pos[best], 0, -1);
        return true;
    }
}

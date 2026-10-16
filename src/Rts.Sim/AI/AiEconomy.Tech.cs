using System.Numerics;
using Rts.Sim.Commander;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Tech side of the economy (wave 5k): structure upgrades (extractors, factories) within a share of the mass income,
/// commander enhancements picked by effect, tiered engineers/power/defenses, mass fabricators paused when energy
/// runs short, the quantum gateway's SCUs and the big projects (base shields, TMD, anti-nuke, TML, artillery,
/// nukes, experimentals, gateway, mass storage next to upgraded extractors). Faction-agnostic: every unit comes
/// from <see cref="AiRoster"/> (roles) or from categories/effects, never from ids.
/// </summary>
public sealed partial class AiEconomy
{
    private AiRoster Ro => _b.R;

    // Counted at each think by CountTech (not saved state: rebuilt from the view).
    private readonly int[] _engTier = new int[4];
    private int _mexUpgrades, _factoryUpgrades;
    /// <summary>Mass per second the running upgrades take (estimate, own build power only).</summary>
    public float UpgradeSpend;
    private float _projectedEnergy;

    /// <summary>Highest tech the factory/engineers may go for (difficulty cap).</summary>
    private int MaxTier => Math.Clamp(Cfg.MaxTier, 1, 3);

    private bool IsScu(UnitBlueprint bp) => Ro.Scu >= 0 && (bp.Index == Ro.Scu || bp.EnhancementBase == Ro.Scu);

    private int TotalEngineers()
    {
        int n = 0;
        for (int t = 1; t <= 3; t++)
        {
            n += _engTier[t];
            if (Ro.Engineer[t] >= 0) n += V.Queued[Ro.Engineer[t]];
        }
        return n;
    }

    /// <summary>Engineer blueprint for this factory: higher tiers while the army has fewer of them than wanted.</summary>
    private int ChooseEngineer(int factory)
    {
        var fbp = W.BpOf(factory);
        int total = TotalEngineers();
        for (int t = Math.Min(MaxTier, fbp.Tech); t >= 2; t--)
        {
            int bp = Ro.Engineer[t];
            if (!AiRoster.CanBuild(fbp, bp)) continue;
            int have = _engTier[t] + V.Queued[bp];
            int want = t == 3 ? 2 + total / 4 : 2 + total / 3;
            if (have < want) return bp;
        }
        if (AiRoster.CanBuild(fbp, Ro.Engineer[1])) return Ro.Engineer[1];
        for (int t = 2; t <= 3; t++) if (AiRoster.CanBuild(fbp, Ro.Engineer[t])) return Ro.Engineer[t];
        return -1;
    }

    /// <summary>Highest tier (≤ maxTier) of a tiered role that this builder can build, or -1.</summary>
    private int BestBuildable(int[] tiers, UnitBlueprint builder, int maxTier)
    {
        for (int t = Math.Min(maxTier, 3); t >= 1; t--)
            if (AiRoster.CanBuild(builder, tiers[t])) return tiers[t];
        return -1;
    }

    private void CountTech()
    {
        Array.Clear(_engTier);
        foreach (int e in V.Engineers)
        {
            var bp = W.BpOf(e);
            if (IsScu(bp)) continue;
            _engTier[Math.Clamp(bp.Tech, 1, 3)]++;
        }
        _mexUpgrades = 0;
        _factoryUpgrades = 0;
        UpgradeSpend = 0;
        var u = W.Units;
        var db = W.Blueprints;
        float e2 = 0;
        foreach (int s in V.Structures)
        {
            var bp = W.BpOf(s);
            e2 += bp.Economy.MaintenanceEnergy;
            if (!u.Upgrade[s].Active) continue;
            int target = u.Upgrade[s].TargetBp;
            if ((uint)target >= (uint)db.Count) continue;
            var te = db[target].Economy;
            float time = MathF.Max(1f, te.BuildTime / UpgradeSystem.SelfRate(bp));
            UpgradeSpend += te.BuildCostMass / time;
            e2 += te.BuildCostEnergy / time;
            if (bp.Has(UnitCategory.MassExtractor)) _mexUpgrades++;
            else if (bp.Has(UnitCategory.Factory)) _factoryUpgrades++;
        }
        // Build spending of busy builders (energy per build-rate point, by what they usually make).
        foreach (int f in V.LandFactories) { var b = W.BpOf(f); e2 += b.Economy.BuildRate * (b.Tech >= 3 ? 2.2f : 1f); }
        foreach (int f in V.AirFactories) e2 += W.BpOf(f).Economy.BuildRate * 4.5f;
        foreach (int f in V.NavalFactories) e2 += W.BpOf(f).Economy.BuildRate * 2.5f;
        foreach (int f in V.Gateways) e2 += W.BpOf(f).Economy.BuildRate * 6f;
        foreach (int e in V.Engineers) e2 += W.BpOf(e).Economy.BuildRate * 4f;
        if (V.Commander >= 0) e2 += W.BpOf(V.Commander).Economy.BuildRate * 4f;
        _projectedEnergy = e2;
    }

    // ------------------------------------------------------------------ upgrades, enhancements, fabs

    private void ManageTech()
    {
        CountTech();
        ManageFabs();
        if (V.Commander >= 0) ManageEnhancements(V.Commander);
        if (MassStall || EnergyStall || EnergyIncome < EnergyDemand * 0.9f) return;
        float room = MassIncome * Cfg.UpgradeShare - UpgradeSpend;
        if (room <= 0) return;
        // Factories first when their tier time has come (they unlock units, engineers and T2/T3 structures).
        if (TryUpgradeFactory(V.LandFactories, LandWant(), ref room)) return;
        if (TryUpgradeFactory(V.AirFactories, AirWant(), ref room)) return;
        TryUpgradeMex(ref room);
    }

    /// <summary>Wanted number of land factories at tier 2 and 3 (index), from game time and mass income.</summary>
    private (int t2, int t3) LandWant()
    {
        float t = _b.GameTime;
        int w2 = t < Cfg.LandT2Time || MassIncome < 16f ? 0 : 1 + (int)((MassIncome - 16f) / 22f);
        int w3 = MaxTier < 3 || t < Cfg.LandT3Time || MassIncome < 40f ? 0 : 1 + (int)((MassIncome - 40f) / 45f);
        return (w2, w3);
    }

    private (int t2, int t3) AirWant()
    {
        float t = _b.GameTime;
        int w2 = t < Cfg.AirT2Time || MassIncome < 30f ? 0 : 1;
        int w3 = MaxTier < 3 || t < Cfg.AirT3Time || MassIncome < 60f ? 0 : 1;
        return (w2, w3);
    }

    private bool TryUpgradeFactory(List<int> factories, (int t2, int t3) want, ref float room)
    {
        if (factories.Count == 0 || _factoryUpgrades >= Cfg.MaxFactoryUpgrades) return false;
        var u = W.Units;
        int at2 = 0, at3 = 0;
        foreach (int f in factories)
        {
            int tier = W.BpOf(f).Tech;
            if (u.Upgrade[f].Active) tier++;
            if (tier >= 2) at2++;
            if (tier >= 3) at3++;
        }
        int fromTier;
        if (at3 < want.t3 && at2 > at3) fromTier = 2;
        else if (at2 < Math.Min(want.t2, factories.Count)) fromTier = 1;
        else return false;
        int best = -1;
        float bd = float.MaxValue;
        foreach (int f in factories)
        {
            var bp = W.BpOf(f);
            if (bp.Tech != fromTier || bp.UpgradesToIndex < 0 || !UpgradeSystem.CanStart(W, f)) continue;
            if (V.ThreatAt(u.Pos[f], 25f) > 0) continue;
            float d = Vector2.DistanceSquared(u.Pos[f], _b.BasePos);
            if (d < bd) { bd = d; best = f; }
        }
        if (best < 0) return false;
        var te = W.Blueprints[W.BpOf(best).UpgradesToIndex].Economy;
        float rate = te.BuildCostMass / MathF.Max(1f, te.BuildTime / UpgradeSystem.SelfRate(W.BpOf(best)));
        if (rate > room && UpgradeSpend > 0) return false;
        _b.Cmd.Upgrade(best);
        _factoryUpgrades++;
        UpgradeSpend += rate;
        room -= rate;
        return true;
    }

    private void TryUpgradeMex(ref float room)
    {
        float t = _b.GameTime;
        if (t < Cfg.MexT2Time || _mexUpgrades >= Cfg.MaxMexUpgrades) return;
        var u = W.Units;
        int best = -1;
        float bestScore = float.MaxValue;
        foreach (int s in V.Structures)
        {
            var bp = W.BpOf(s);
            if (!bp.Has(UnitCategory.MassExtractor) || bp.UpgradesToIndex < 0) continue;
            if (bp.Tech + 1 > MaxTier || (bp.Tech >= 2 && t < Cfg.MexT3Time)) continue;
            if (!u.IsComplete(s) || u.Upgrade[s].Active) continue;
            var p = u.Pos[s];
            float dBase = Vector2.Distance(p, _b.BasePos);
            // Safe extractors near home first; lower tiers first.
            float score = dBase + bp.Tech * 120f;
            if (score >= bestScore) continue;
            if (V.ThreatAt(p, 24f) > 0) continue;
            if (!UpgradeSystem.CanStart(W, s)) continue;
            best = s;
            bestScore = score;
        }
        if (best < 0) return;
        var te = W.Blueprints[W.BpOf(best).UpgradesToIndex].Economy;
        float rate = te.BuildCostMass / MathF.Max(1f, te.BuildTime / UpgradeSystem.SelfRate(W.BpOf(best)));
        if (rate > room && UpgradeSpend > 0) return;
        // Energy for the upgraded extractor's upkeep and the upgrade itself.
        if (EnergyIncome - EnergyDemand < te.BuildCostEnergy / MathF.Max(1f, te.BuildTime / UpgradeSystem.SelfRate(W.BpOf(best))) * 0.5f
            && EnergyRatio < 0.5f) return;
        _b.Cmd.Upgrade(best);
        _mexUpgrades++;
        UpgradeSpend += rate;
        room -= rate;
    }

    /// <summary>Commander enhancements by effect: engineering (new build list) first, then a shield, then the next
    /// engineering step, then weapons. Only at home, unthreatened, with the economy to pay for it.</summary>
    private void ManageEnhancements(int c)
    {
        var u = W.Units;
        if (_b.GameTime < Cfg.EnhanceTime || u.Upgrade[c].Active) return;
        if (MassStall || EnergyStall || MassIncome < 14f) return;
        ref var info = ref _b.Info(c);
        if (info.HoldUntil > W.Tick) return;
        var pos = u.Pos[c];
        if (Vector2.DistanceSquared(pos, _b.BasePos) > 28f * 28f || V.ThreatAt(pos, 30f) > 0) return;
        var bp = W.BpOf(c);
        if (!bp.IsEnhanceable || bp.Enhancements.Length == 0) return;
        int pick = -1, bestRank = int.MaxValue;
        for (int e = 0; e < bp.Enhancements.Length; e++)
        {
            if (!EnhancementCatalog.CanInstall(bp, e)) continue;
            var x = bp.Enhancements[e];
            var fx = x.Effects;
            int rank;
            if (fx.BuildListFrom.Length > 0 && fx.BuildRateAdd > 0)
                rank = x.PrerequisiteIndex < 0 ? 0 : (V.LandTier >= 3 || MassIncome > 45f) && MaxTier >= 3 ? 2 : 99;
            else if (fx.Shield != null) rank = x.PrerequisiteIndex < 0 ? 1 : 4;
            else if (fx.Weapons.Length > 0 && fx.AddWeapons.Length == 0 && fx.Silo == null) rank = 3;
            else continue;
            if (rank >= 99) continue;
            // Afford it within about a minute of income.
            if (x.BuildCostMass > MassIncome * 75f) continue;
            if (rank < bestRank) { bestRank = rank; pick = e; }
        }
        if (pick < 0 || !EnhancementSystem.CanStart(W, c, pick)) return;
        var ex = bp.Enhancements[pick];
        float time = ex.BuildTime / MathF.Max(1f, bp.Economy.BuildRate);
        if (EnergyIncome - EnergyDemand < ex.BuildCostEnergy / time * 0.4f && EnergyRatio < 0.6f) return;
        _b.Cmd.Enhance(c, ex.Id);
        info.Task = AiTask.None;
        info.HoldUntil = W.Tick + 3; // no other order this think: it would cancel the enhancement
    }

    /// <summary>Mass fabricators run only while energy is plentiful.</summary>
    private void ManageFabs()
    {
        var u = W.Units;
        foreach (int s in V.Structures)
        {
            var bp = W.BpOf(s);
            if (!bp.Has(UnitCategory.MassFabricator) || !u.IsComplete(s)) continue;
            bool paused = (u.Flags[s] & UnitFlags.Paused) != 0;
            if (!paused && (EnergyStall || EnergyRatio < 0.25f)) _b.Cmd.SetPaused(s, true);
            else if (paused && !EnergyStall && EnergyRatio > 0.6f) _b.Cmd.SetPaused(s, false);
        }
    }

    // ------------------------------------------------------------------ gateway

    private void ManageGateways(ref float slot)
    {
        if (V.Gateways.Count == 0 || Ro.Scu < 0 || Cfg.MaxScus <= 0) return;
        int scus = 0;
        foreach (int e in V.Engineers) if (IsScu(W.BpOf(e))) scus++;
        scus += V.Queued[Ro.Scu];
        var u = W.Units;
        foreach (int g in V.Gateways)
        {
            ref var info = ref _b.Info(g);
            if (!info.RallySet)
            {
                info.RallySet = true;
                info.Role = AiRole.Factory;
                _b.Cmd.Order(g, Order.MoveTo(_b.Rally));
            }
            var q = u.Factory[g].Queue;
            if ((q?.Count ?? 0) > 0 || scus >= Cfg.MaxScus || MassStall || EnergyStall) continue;
            if (!AiRoster.CanBuild(W.BpOf(g), Ro.Scu)) continue;
            slot += FactoryWeight(g);
            if (slot > FactoryBudget + 2) continue;
            _b.Cmd.Enqueue(g, Ro.Scu);
            V.Queued[Ro.Scu]++;
            scus++;
        }
    }

    /// <summary>Factory budget weight: mass per second it spends relative to a T1 land factory.</summary>
    public float FactoryWeight(int f) => MathF.Max(1f, W.BpOf(f).Economy.BuildRate / 20f);

    // ------------------------------------------------------------------ projects

    /// <summary>Own structures (any state) plus reservations having all the categories.</summary>
    private int CountCat(UnitCategory cat)
    {
        int n = 0;
        foreach (int s in V.Structures) if (W.BpOf(s).Has(cat)) n++;
        foreach (var r in _b.Placement.Reserved) if (W.Blueprints[r.Bp].Has(cat)) n++;
        return n;
    }

    private int CountBp(int bp)
    {
        if (bp < 0) return 0;
        return V.Count[bp] + _b.Placement.ReservedCount(bp);
    }

    /// <summary>Center of the base core (mean of the factories, else the base position).</summary>
    private Vector2 CoreCenter()
    {
        var u = W.Units;
        Vector2 sum = default;
        int n = 0;
        foreach (int f in V.LandFactories) { sum += u.Pos[f]; n++; }
        foreach (int f in V.AirFactories) { sum += u.Pos[f]; n++; }
        return n > 0 ? sum / n : _b.BasePos;
    }

    /// <summary>Big and strategic builds (T2+ builders): anti-nuke, TMD, storage adjacency, shields, TML, artillery,
    /// gateway, nuke, experimentals, mass fabricators.</summary>
    private bool TryProjects(int unit, bool isCommander)
    {
        var ubp = W.BpOf(unit);
        if (ubp.Tech < 2 && !isCommander) return TryMassStorageAdjacent(unit, ubp);
        float maxR = isCommander ? Cfg.CommanderRadius : 40f;
        float t = _b.GameTime;
        // Anti-nuke once an enemy strategic launcher is known (intel-fair: perception only sees what was seen).
        if (V.EnemyNukes > 0 && AiRoster.CanBuild(ubp, Ro.AntiNuke) && CountBp(Ro.AntiNuke) < 1)
            if (TryBuildAt(unit, Ro.AntiNuke, CoreCenter(), 2, maxR)) return true;
        // Tactical missile defense when a known enemy TML can reach the base.
        if (V.EnemyTml > 0 && AiRoster.CanBuild(ubp, Ro.Tmd) && Vector2.Distance(V.EnemyTmlPos, _b.BasePos) < 300f
            && CountBp(Ro.Tmd) < Math.Min(3, 1 + V.EnemyTml))
            if (TryBuildAt(unit, Ro.Tmd, CoreCenter() + Vector2.Normalize(V.EnemyTmlPos - _b.BasePos + new Vector2(0.01f, 0)) * 8f, 2, maxR)) return true;
        if (TryMassStorageAdjacent(unit, ubp)) return true;
        if (MassRatio < 0.05f) return false;
        // Shields over the base core.
        if (t >= Cfg.ShieldTime && MassIncome > 22f)
        {
            int sh = BestBuildable(Ro.Shield, ubp, MaxTier);
            int want = Math.Min(Cfg.MaxShields, 1 + (int)((MassIncome - 22f) / 30f));
            if (sh >= 0 && CountCat(UnitCategory.Shield | UnitCategory.Structure) < want)
                if (TryBuildAt(unit, sh, CoreCenter(), 3, 16)) return true;
        }
        // Tactical missile launchers at the front of the base (fired by AiStrategic).
        if (t >= Cfg.TmlTime && Cfg.MaxTml > 0 && MassIncome > 28f && AiRoster.CanBuild(ubp, Ro.Tml) && CountBp(Ro.Tml) < Cfg.MaxTml)
            if (TryBuildInBase(unit, Ro.Tml, _b.EnemyDir, 10, maxR, false)) return true;
        // Artillery installation when known enemy structures are in its range from the front of the base.
        if (t >= Cfg.TmlTime && MassIncome > 35f && AiRoster.CanBuild(ubp, Ro.ArtilleryInst2) && CountBp(Ro.ArtilleryInst2) < 1 + (Floating ? 1 : 0)
            && EnemyStructuresWithin(_b.BasePos + _b.EnemyDir * 24f, W.Blueprints[Ro.ArtilleryInst2].Weapons.Length > 0 ? W.Blueprints[Ro.ArtilleryInst2].Weapons[0].Range - 6f : 0f) >= 3)
            if (TryBuildInBase(unit, Ro.ArtilleryInst2, _b.EnemyDir, 16, maxR + 6, false)) return true;
        if (t < Cfg.StrategicTime || MaxTier < 3 || isCommander) return false;
        // Late projects: one at a time (engineers assist through TryAssistProject).
        if (BigProjectInProgress()) return false;
        if (MassIncome > 45f && Cfg.MaxScus > 0 && AiRoster.CanBuild(ubp, Ro.Gateway) && CountBp(Ro.Gateway) < 1)
            if (TryBuildInBase(unit, Ro.Gateway, Perp(_b.EnemyDir), 12, maxR + 6, false)) return true;
        if (MassIncome > 55f && AiRoster.CanBuild(ubp, Ro.ExpLand) && CountExperimentals() < 1 + (int)(MassIncome / 90f))
            if (TryBuildInBase(unit, Ro.ExpLand, _b.EnemyDir, 12, maxR + 8, false)) return true;
        if (MassIncome > 70f && AiRoster.CanBuild(ubp, Ro.NukeSilo) && CountBp(Ro.NukeSilo) < 1)
            if (TryBuildInBase(unit, Ro.NukeSilo, -_b.EnemyDir, 10, maxR + 6, false)) return true;
        if (MassIncome > 70f && AiRoster.CanBuild(ubp, Ro.ArtilleryInst3) && CountBp(Ro.ArtilleryInst3) < 1
            && EnemyStructuresWithin(_b.BasePos, W.Blueprints[Ro.ArtilleryInst3].Weapons.Length > 0 ? W.Blueprints[Ro.ArtilleryInst3].Weapons[0].Range - 10f : 0f) >= 5)
            if (TryBuildInBase(unit, Ro.ArtilleryInst3, _b.EnemyDir, 12, maxR + 6, false)) return true;
        // Mass fabricators with spare energy.
        if (EnergyRatio > 0.9f && EnergyIncome - EnergyDemand > 0)
        {
            int fab = BestBuildable(Ro.MassFab, ubp, MaxTier);
            if (fab >= 0 && EnergyIncome - EnergyDemand > W.Blueprints[fab].Economy.MaintenanceEnergy * 1.1f && CountCat(UnitCategory.MassFabricator) < 4)
                if (TryBuildInBase(unit, fab, -_b.EnemyDir, 10, maxR + 6, false)) return true;
        }
        return false;
    }

    private int CountExperimentals()
    {
        int n = 0;
        foreach (int s in V.Unfinished) if (W.BpOf(s).Has(UnitCategory.Experimental)) n++;
        foreach (int s in V.LandCombat) if (W.BpOf(s).Has(UnitCategory.Experimental)) n++;
        foreach (var r in _b.Placement.Reserved) if (W.Blueprints[r.Bp].Has(UnitCategory.Experimental)) n++;
        return n;
    }

    /// <summary>An unfinished structure/experimental costing more than 2500 mass is being built (or reserved).</summary>
    private bool BigProjectInProgress()
    {
        foreach (int s in V.Unfinished) if (W.BpOf(s).Economy.BuildCostMass >= 2500f) return true;
        foreach (var r in _b.Placement.Reserved) if (W.Blueprints[r.Bp].Economy.BuildCostMass >= 2500f) return true;
        return false;
    }

    private int EnemyStructuresWithin(Vector2 p, float r)
    {
        if (r <= 0) return 0;
        int n = 0;
        foreach (var k in _b.Perception.Structures)
            if (Vector2.DistanceSquared(k.Pos, p) <= r * r) n++;
        return n;
    }

    private bool TryBuildAt(int unit, int bp, Vector2 center, float minR, float maxR)
    {
        if (bp < 0) return false;
        if (!_b.Placement.FindSite(bp, center, _b.EnemyDir, minR, maxR, out var site, cooldownTicks: 300)) return false;
        IssueBuild(unit, bp, site, -1, false);
        return true;
    }

    /// <summary>Mass storage flush against an upgraded extractor's side (adjacency: storage boosts its output).</summary>
    private bool TryMassStorageAdjacent(int unit, UnitBlueprint ubp)
    {
        int st = Ro.MassStorage;
        if (st < 0 || !AiRoster.CanBuild(ubp, st) || MassIncome < 12f || MassStall) return false;
        if (_b.Placement.ReservedCount(st) >= 2) return false;
        var u = W.Units;
        int best = -1;
        float bd = float.MaxValue;
        foreach (int s in V.Structures)
        {
            var bp = W.BpOf(s);
            if (!bp.Has(UnitCategory.MassExtractor) || !u.IsComplete(s)) continue;
            // T2+ extractors (or one upgrading to T2) are worth surrounding.
            if (bp.Tech < 2 && !u.Upgrade[s].Active) continue;
            var p = u.Pos[s];
            float d = Vector2.DistanceSquared(p, u.Pos[unit]) + Vector2.DistanceSquared(p, _b.BasePos);
            if (d >= bd) continue;
            if (V.ThreatAt(p, 24f) > 0) continue;
            if (!_b.Placement.FindAdjacentSite(st, s, default, out _)) continue;
            best = s;
            bd = d;
        }
        if (best < 0) return false;
        if (!_b.Placement.FindAdjacentSite(st, best, default, out var site)) return false;
        IssueBuild(unit, st, site, -1, false);
        return true;
    }

    /// <summary>Engineers help own structures that are upgrading (extractors first, near the builder).</summary>
    private bool TryAssistUpgrade(int unit, bool isCommander)
    {
        if (MassStall || EnergyStall || V.Upgrading == 0) return false;
        var u = W.Units;
        int best = -1;
        float bd = float.MaxValue;
        foreach (int s in V.Structures)
        {
            if (!u.Upgrade[s].Active) continue;
            var p = u.Pos[s];
            if (!InBuilderRange(isCommander, p)) continue;
            float d = Vector2.DistanceSquared(p, u.Pos[unit]);
            if (d > 60f * 60f || V.ThreatAt(p, 20f) > 0) continue;
            if (W.BpOf(s).Has(UnitCategory.MassExtractor)) d *= 0.5f;
            if (d >= bd || Helpers(s) >= (Cfg.Difficulty == AiDifficulty.Hard ? 3 : 2)) continue;
            if (d < bd) { bd = d; best = s; }
        }
        if (best < 0) return false;
        var cur = u.Orders[unit];
        if (!cur.IsEmpty && cur.Current.Type == OrderType.Guard && cur.Current.Target.Index == best)
        {
            _b.Info(unit).TaskTick = W.Tick;
            return true;
        }
        if (Helpers(best) >= (Cfg.Difficulty == AiDifficulty.Hard ? 3 : 2)) return false;
        _b.Cmd.Order(unit, Order.GuardUnit(u.HandleOf(best)));
        SetTask(unit, AiTask.Assist, u.Pos[best], 0, -1);
        return true;
    }

    /// <summary>T2/T3 power generator flush against a factory's side (adjacency: cheaper factory energy), away from
    /// its exit. Only for big generators (T1 ones are placed by the base spiral).</summary>
    private bool TryPowerNearFactory(int unit, int pg, bool isCommander)
    {
        if (W.Blueprints[pg].Tech < 2) return false;
        var u = W.Units;
        foreach (int f in V.LandFactories)
        {
            if (!InBuilderRange(isCommander, u.Pos[f]) || Vector2.DistanceSquared(u.Pos[f], _b.BasePos) > 45f * 45f) continue;
            if (!_b.Placement.FindAdjacentSite(pg, f, _b.EnemyDir, out var site)) continue;
            IssueBuild(unit, pg, site, -1, false);
            return true;
        }
        return false;
    }

    /// <summary>Own builders whose current order guards (assists) the unit.</summary>
    private int Helpers(int target)
    {
        var u = W.Units;
        int n = 0;
        foreach (int e in V.Engineers)
        {
            var q = u.Orders[e];
            if (!q.IsEmpty && q.Current.Type == OrderType.Guard && q.Current.Target.Index == target) n++;
        }
        if (V.Commander >= 0)
        {
            var q = u.Orders[V.Commander];
            if (!q.IsEmpty && q.Current.Type == OrderType.Guard && q.Current.Target.Index == target) n++;
        }
        return n;
    }

    /// <summary>Best power generator this builder can make for the current energy income (bigger tiers once the
    /// income makes them sensible).</summary>
    private int ChoosePgen(UnitBlueprint builder)
    {
        if (EnergyIncome >= 1200f && AiRoster.CanBuild(builder, Ro.Pgen[3]) && MaxTier >= 3) return Ro.Pgen[3];
        if (EnergyIncome >= 180f && AiRoster.CanBuild(builder, Ro.Pgen[2]) && MaxTier >= 2) return Ro.Pgen[2];
        return AiRoster.CanBuild(builder, Ro.Pgen[1]) ? Ro.Pgen[1] : -1;
    }
}

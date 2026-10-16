using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// SupCom-style in-place structure upgrades (T1→T2→T3 factories, mass extractors, radars), driven by the blueprint's
/// <c>UpgradesTo</c>. SEQUENTIAL (commands, ConstructionSystem and FactorySystem phases).
/// <list type="bullet">
/// <item><see cref="UpgradeCommand"/> starts it on a complete own structure: <see cref="UnitStore.Upgrade"/> holds the
///   target blueprint and the progress. The structure builds its upgrade with its own BuildRate (structures without
///   one, e.g. mexes and radars, use <see cref="DefaultRatePerTech"/> × tech) using the TARGET blueprint's
///   BuildTime/cost, through the normal flow economy (<see cref="ConstructionSystem.Work"/> with
///   <see cref="BuilderMode.Upgrade"/>: pay first, then progress, scaled by efficiency).</item>
/// <item>Engineers assist it with Guard (or Repair once it is at full health), like factory assist.</item>
/// <item>While upgrading the structure keeps working as its current blueprint (a mex keeps producing, a radar keeps
///   scanning), except factories: production pauses (queue and the unit inside are kept) and resumes afterwards.</item>
/// <item><see cref="CancelUpgradeCommand"/> refunds what was spent (cost × progress, like a cancelled build).</item>
/// <item>On completion the unit becomes the target blueprint IN PLACE, keeping its slot and <see cref="UnitHandle"/>
///   (selection, AI references, rally orders, factory queue and veterancy survive): health keeps its fraction, the
///   footprint is re-stamped if it differs, and every module re-initializes from the new blueprint
///   (<see cref="UnitLifecycle.OnSpawn"/>: weapons, builder, factory, economy...).</item>
/// </list>
/// </summary>
public static class UpgradeSystem
{
    /// <summary>Build power of structures without a BuildRate (mexes, radars) when upgrading themselves, per tech level.</summary>
    public const float DefaultRatePerTech = 10f;
    private const float CompleteEpsilon = 1e-4f;

    public static bool IsUpgrading(SimWorld w, int i) => w.Units.Upgrade[i].Active;

    /// <summary>Cost and build time of turning unit i into <paramref name="target"/>: the enhancement's own cost when the
    /// target is an enhancement variant of i's blueprint (Commander/EnhancementCatalog), else the target blueprint's.</summary>
    public static (float Mass, float Energy, float Time) Cost(SimWorld w, int i, int target)
    {
        if (Commander.EnhancementCatalog.TryGetCost(w.Blueprints, w.Units.Bp[i], target, out var c)) return c;
        var e = w.Blueprints[target].Economy;
        return (e.BuildCostMass, e.BuildCostEnergy, e.BuildTime);
    }

    /// <summary>Build power a structure applies to its own upgrade.</summary>
    public static float SelfRate(UnitBlueprint bp) =>
        bp.Economy.BuildRate > 0 ? bp.Economy.BuildRate : DefaultRatePerTech * Math.Max(1, bp.Tech);

    /// <summary>Build power builder i applies to the upgrade of structure t (its own rate, or <see cref="SelfRate"/>
    /// when it upgrades itself).</summary>
    public static float BuildPower(SimWorld w, int i, int t) =>
        i == t ? SelfRate(w.BpOf(i)) : w.BpOf(i).Economy.BuildRate;

    /// <summary>True if unit i is a complete structure with an UpgradesTo that is not upgrading yet and whose new
    /// footprint (if different) fits.</summary>
    public static bool CanStart(SimWorld w, int i)
    {
        var u = w.Units;
        if (!u.IsActive(i) || !u.IsComplete(i) || u.Upgrade[i].Active) return false;
        var bp = w.BpOf(i);
        if (!bp.IsStructure || bp.UpgradesToIndex < 0) return false;
        return FootprintFits(w, i, bp, w.Blueprints[bp.UpgradesToIndex]);
    }

    public static void Apply(SimWorld w, UpgradeCommand c)
    {
        foreach (var h in c.Units)
            if (CommandSystem.Owns(w, c.Army, h)) Start(w, h.Index);
    }

    public static void Apply(SimWorld w, CancelUpgradeCommand c)
    {
        foreach (var h in c.Units)
            if (CommandSystem.Owns(w, c.Army, h)) Cancel(w, h.Index);
    }

    /// <summary>Starts the upgrade of structure i. Returns false if it can't upgrade now.</summary>
    public static bool Start(SimWorld w, int i)
    {
        if (!CanStart(w, i)) return false;
        var u = w.Units;
        int to = w.BpOf(i).UpgradesToIndex;
        u.Upgrade[i] = new UpgradeState { Active = true, TargetBp = to };
        var tbp = w.Blueprints[to];
        w.Events.Effects.Add(new EffectEvent
        {
            Kind = EffectKind.BuildStarted,
            Pos = DMath.To3(u.Pos[i], u.Elevation[i]),
            Size = MathF.Max(tbp.Visual.Size.X, tbp.Visual.Size.Z),
            Army = u.Army[i],
            BlueprintIndex = to,
        });
        return true;
    }

    /// <summary>Cancels the upgrade of structure i, refunding cost × progress (capped by storage).</summary>
    public static void Cancel(SimWorld w, int i)
    {
        var u = w.Units;
        ref var up = ref u.Upgrade[i];
        if (!up.Active) return;
        var cost = Cost(w, i, up.TargetBp);
        var eco = w.Armies[u.Army[i]].Eco;
        eco.Mass = MathF.Min(eco.MassMax, eco.Mass + cost.Mass * up.Fraction);
        eco.Energy = MathF.Min(eco.EnergyMax, eco.Energy + cost.Energy * up.Fraction);
        up = default;
        // Drop the upgrade work requested last tick (the structure's and its assistants'), so nothing more is paid.
        for (int k = 0; k < u.AliveCount; k++)
        {
            int j = u.AliveList[k];
            ref var b = ref u.Builder[j];
            if (b.Mode == BuilderMode.Upgrade && b.Target.Index == i && u.Alive[j]) ConstructionSystem.StopBuilder(w, j);
        }
        if (u.Builder[i].Mode == BuilderMode.Upgrade) ConstructionSystem.StopBuilder(w, i);
    }

    /// <summary>Builder i requests upgrade work on structure t (from <see cref="ConstructionSystem.Work"/>).
    /// Capped by the remaining work minus what other builders reserved this tick.</summary>
    internal static void RequestWork(SimWorld w, int i, int t, ref BuilderState b)
    {
        var u = w.Units;
        ref var up = ref u.Upgrade[t];
        if (!up.Active) return;
        var cost = Cost(w, t, up.TargetBp);
        float step = BuildPower(w, i, t) / cost.Time * SimWorld.Dt;
        float reserved = up.ReservedTick == w.Tick ? up.ReservedWork : 0;
        float work = MathF.Min(step, 1f - up.Fraction - reserved);
        if (work <= 0) return;
        b.PendingWork = work;
        up.ReservedWork = reserved + work;
        up.ReservedTick = w.Tick;
        ref var e = ref u.Econ[i];
        e.MassRequest += cost.Mass * work / SimWorld.Dt;
        e.EnergyRequest += cost.Energy * work / SimWorld.Dt;
    }

    /// <summary>Paid upgrade work arrives (from ConstructionSystem's apply phase); completes the upgrade at 100%.</summary>
    internal static void AddProgress(SimWorld w, int t, float delta)
    {
        ref var up = ref w.Units.Upgrade[t];
        if (!up.Active) return;
        up.Fraction = MathF.Min(1f, up.Fraction + delta);
        if (up.Fraction >= 1f - CompleteEpsilon) Complete(w, t);
    }

    /// <summary>The structure becomes its upgrade blueprint in place (same slot, same handle). SEQUENTIAL.</summary>
    public static void Complete(SimWorld w, int t)
    {
        var u = w.Units;
        ref var up = ref u.Upgrade[t];
        if (!up.Active) return;
        var obp = w.BpOf(t);
        var nbp = w.Blueprints[up.TargetBp];
        float oldMax = Combat.Veterancy.MaxHealth(obp, u.Kills[t]); // veterancy-effective max health
        float hpFrac = oldMax > 0 ? u.Health[t] / oldMax : 1f;
        up = default;

        if (obp.FootprintX != nbp.FootprintX || obp.FootprintZ != nbp.FootprintZ)
        {
            w.Occupancy.Clear(obp, u.Pos[t], t);
            u.Pos[t] = OccupancyGrid.SnapStructure(nbp, u.Pos[t]);
            w.Occupancy.Stamp(nbp, u.Pos[t], t);
        }
        u.Bp[t] = nbp.Index;
        float newMax = Combat.Veterancy.MaxHealth(nbp, u.Kills[t]);
        u.Health[t] = Math.Clamp(hpFrac * newMax, 1f, newMax);
        // Fresh weapon states (targets, ground targets) as for a new unit, then every module re-initializes.
        var ws = u.WeaponsOf(t);
        ws.Clear();
        for (int k = 0; k < ws.Length; k++) ws[k].Target = UnitHandle.None;
        UnitLifecycle.OnSpawn(w, t, nbp);

        w.Events.Effects.Add(new EffectEvent
        {
            Kind = EffectKind.BuildComplete,
            Pos = DMath.To3(u.Pos[t], u.Elevation[t]),
            Size = MathF.Max(nbp.Visual.Size.X, nbp.Visual.Size.Z),
            Army = u.Army[t],
            BlueprintIndex = nbp.Index,
        });
    }

    /// <summary>End of ConstructionSystem: upgrading non-factory structures request their own upgrade work
    /// (factories do it in FactorySystem instead of producing).</summary>
    internal static void Run(SimWorld w)
    {
        var u = w.Units;
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            if (!u.Upgrade[i].Active || !u.IsActive(i)) continue;
            var bp = w.BpOf(i);
            // A unit enhancing itself (commander/SCU) is interrupted by any new order: cancelled with refund (SupCom).
            if (bp.IsEnhanceable && u.Orders[i].Count > 0) { Cancel(w, i); continue; }
            if (bp.Has(UnitCategory.Factory)) continue;
            WorkOnSelf(w, i);
        }
    }

    /// <summary>Structure i spends its build power on its own upgrade this tick.</summary>
    internal static void WorkOnSelf(SimWorld w, int i)
    {
        var u = w.Units;
        ref var e = ref u.Econ[i];
        e.MassRequest = 0;
        e.EnergyRequest = 0;
        if ((u.Flags[i] & (UnitFlags.Paused | UnitFlags.Stunned)) != 0) { ConstructionSystem.Idle(w, i); return; }
        ConstructionSystem.Work(w, i, u.HandleOf(i), BuilderMode.Upgrade);
    }

    /// <summary>Same footprint, or every new cell is inside the map, free (or ours) and on suitable terrain.</summary>
    private static bool FootprintFits(SimWorld w, int i, UnitBlueprint obp, UnitBlueprint nbp)
    {
        if (obp.FootprintX == nbp.FootprintX && obp.FootprintZ == nbp.FootprintZ) return true;
        var pos = OccupancyGrid.SnapStructure(nbp, w.Units.Pos[i]);
        var (x0, z0, x1, z1) = OccupancyGrid.Footprint(nbp, pos);
        bool naval = nbp.HasAny(UnitCategory.Naval);
        var occ = w.Occupancy;
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            if (x < 0 || z < 0 || x >= occ.Width || z >= occ.Height) return false;
            int o = occ.At(x, z);
            if (o >= 0 && o != i) return false;
            var p = w.Terrain.PassAt(x, z);
            if (naval ? (p & Map.PassFlags.WaterSurface) == 0 : (p & Map.PassFlags.Land) == 0) return false;
        }
        return true;
    }
}

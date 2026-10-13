using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Commander;

/// <summary>
/// Commander / SCU enhancements (SupCom ACU style), data-driven by <see cref="UnitBlueprint.Enhancements"/>.
/// Every slot combination is a generated blueprint variant (<see cref="EnhancementCatalog"/>), so installing or
/// removing an enhancement swaps the unit's blueprint in place and every system just reads the new data.
/// Building an enhancement reuses the in-place upgrade machinery (<see cref="UpgradeSystem"/>):
/// <c>Units.Upgrade[i]</c> targets the variant, the unit spends its own build power on it (engineers can assist with
/// Guard), cost/time come from the enhancement (<see cref="UpgradeSystem.Cost"/>), cancel refunds, and any new order
/// interrupts it (UpgradeSystem.Run). SEQUENTIAL (command phase).
/// </summary>
public static class EnhancementSystem
{
    public static void Apply(SimWorld w, EnhanceCommand c)
    {
        foreach (var h in c.Units)
        {
            if (!CommandSystem.Owns(w, c.Army, h)) continue;
            int e = EnhancementCatalog.Find(w.BpOf(h.Index), c.Enhancement);
            if (e >= 0) Start(w, h.Index, e);
        }
    }

    public static void Apply(SimWorld w, RemoveEnhancementCommand c)
    {
        foreach (var h in c.Units)
            if (CommandSystem.Owns(w, c.Army, h)) Remove(w, h.Index, c.Slot);
    }

    /// <summary>True if unit i can start building enhancement <paramref name="e"/> now.</summary>
    public static bool CanStart(SimWorld w, int i, int e)
    {
        var u = w.Units;
        if (!u.IsActive(i) || !u.IsComplete(i) || u.Upgrade[i].Active) return false;
        return EnhancementCatalog.CanInstall(w.BpOf(i), e);
    }

    /// <summary>Unit i stops and starts building enhancement e on itself. Returns false if it can't.</summary>
    public static bool Start(SimWorld w, int i, int e)
    {
        if (!CanStart(w, i, e)) return false;
        var bp = w.BpOf(i);
        int target = EnhancementCatalog.WithSlot(w.Blueprints, bp, bp.Enhancements[e].Slot, e);
        if (target < 0) return false;
        var u = w.Units;
        CommandSystem.StopUnit(w, i);
        u.Upgrade[i] = new UpgradeState { Active = true, TargetBp = target };
        w.Events.Effects.Add(new EffectEvent
        {
            Kind = EffectKind.BuildStarted,
            Pos = DMath.To3(u.Pos[i], u.Elevation[i]),
            Size = MathF.Max(bp.Visual.Size.X, bp.Visual.Size.Z),
            Army = u.Army[i],
            BlueprintIndex = target,
        });
        return true;
    }

    /// <summary>Removes the enhancement in <paramref name="slot"/> (and its prerequisites) at once, without refund.</summary>
    public static bool Remove(SimWorld w, int i, EnhancementSlot slot)
    {
        var u = w.Units;
        if (!u.IsActive(i) || !u.IsComplete(i) || u.Upgrade[i].Active) return false;
        var bp = w.BpOf(i);
        if (!bp.IsEnhanceable || bp.Installed[(int)slot] < 0) return false;
        int target = EnhancementCatalog.WithSlot(w.Blueprints, bp, slot, -1);
        if (target < 0) return false;
        u.Upgrade[i] = new UpgradeState { Active = true, TargetBp = target };
        UpgradeSystem.Complete(w, i);
        return true;
    }
}

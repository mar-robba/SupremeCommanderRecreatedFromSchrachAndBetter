using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>Bonus one structure gives another (fractions, see <see cref="EconState.AdjMassProd"/>).</summary>
public readonly record struct AdjacencyEffect(float MassProd, float EnergyProd, float EnergyUse)
{
    public bool IsZero => MassProd == 0 && EnergyProd == 0 && EnergyUse == 0;
}

/// <summary>
/// SupCom adjacency bonus rules (pure functions; used by <see cref="AdjacencySystem"/> and by the client for
/// placement previews). Two complete structures of the same army are adjacent when their footprints share an edge
/// (corners don't count). A giver's bonus is scaled by the shared edge length / the receiver's footprint side, so
/// fully surrounding a receiver gives about 4× the per-side value whatever the sizes:
/// <list type="bullet">
/// <item>power generator → structures that use energy (factories' build energy, mass fabricators, shields,
///   radar/sonar/omni, mex upkeep, silos, upgrades): energy-use reduction T1 10%, hydrocarbon 12.5%, T2 15%,
///   T3 20% per side; total capped at <see cref="MaxEnergyUseReduction"/>;</item>
/// <item>mass storage → mass extractor: +12.5% mass production per side;</item>
/// <item>energy storage → power generator: +12.5% energy production per side.</item>
/// </list>
/// </summary>
public static class AdjacencyRules
{
    public const float StorageBonus = 0.125f;
    public const float MaxEnergyUseReduction = 0.75f;

    /// <summary>Energy-use reduction a power generator gives per full receiver side.</summary>
    public static float PowerReduction(UnitBlueprint pgen) =>
        pgen.Has(UnitCategory.HydroCarbon) ? 0.125f : pgen.Tech >= 3 ? 0.20f : pgen.Tech == 2 ? 0.15f : 0.10f;

    /// <summary>True for structures whose energy consumption power generators reduce.</summary>
    public static bool UsesEnergy(UnitBlueprint bp) =>
        bp.IsStructure && !bp.Has(UnitCategory.PowerGenerator)
        && (bp.Economy.BuildRate > 0 || bp.Economy.MaintenanceEnergy > 0 || bp.Silo != null || bp.UpgradesToIndex >= 0);

    /// <summary>Can this blueprint give or receive any adjacency bonus? (cheap filter)</summary>
    public static bool Participates(UnitBlueprint bp) =>
        bp.IsStructure && (bp.HasAny(UnitCategory.PowerGenerator | UnitCategory.MassStorage | UnitCategory.EnergyStorage
                                     | UnitCategory.MassExtractor) || UsesEnergy(bp));

    /// <summary>Length (cells) of the footprint edge shared by two structures (0 = not adjacent / overlapping).</summary>
    public static int SharedEdge(UnitBlueprint a, Vector2 pa, UnitBlueprint b, Vector2 pb)
    {
        var (ax0, az0, ax1, az1) = OccupancyGrid.Footprint(a, pa);
        var (bx0, bz0, bx1, bz1) = OccupancyGrid.Footprint(b, pb);
        if (ax1 + 1 == bx0 || bx1 + 1 == ax0)
            return Math.Max(0, Math.Min(az1, bz1) - Math.Max(az0, bz0) + 1);
        if (az1 + 1 == bz0 || bz1 + 1 == az0)
            return Math.Max(0, Math.Min(ax1, bx1) - Math.Max(ax0, bx0) + 1);
        return 0;
    }

    /// <summary>The bonus the giver gives the receiver for one shared edge of <paramref name="edge"/> cells.</summary>
    public static AdjacencyEffect Effect(UnitBlueprint giver, UnitBlueprint receiver, int edge)
    {
        if (edge <= 0 || !giver.IsStructure || !receiver.IsStructure) return default;
        float side = MathF.Max(1, MathF.Max(receiver.FootprintX, receiver.FootprintZ));
        float f = MathF.Min(1f, edge / side);
        float mass = 0, energy = 0, use = 0;
        if (giver.Has(UnitCategory.PowerGenerator) && UsesEnergy(receiver)) use = PowerReduction(giver) * f;
        if (giver.Has(UnitCategory.MassStorage) && receiver.Has(UnitCategory.MassExtractor)) mass = StorageBonus * f;
        if (giver.Has(UnitCategory.EnergyStorage) && receiver.Has(UnitCategory.PowerGenerator)) energy = StorageBonus * f;
        return new AdjacencyEffect(mass, energy, use);
    }

    /// <summary>Bonus of giver at pg to receiver at pr (0 if not adjacent).</summary>
    public static AdjacencyEffect Effect(UnitBlueprint giver, Vector2 pg, UnitBlueprint receiver, Vector2 pr) =>
        Effect(giver, receiver, SharedEdge(giver, pg, receiver, pr));
}

using Rts.Sim.Data;
using Rts.Sim.Map;

namespace Rts.Sim.Naval;

/// <summary>
/// Terrain rules for water structures (blueprints with Structure + Naval, e.g. naval factories, sonar, torpedo
/// launchers). Shared by the sim (<see cref="World.OccupancyGrid.CanPlace"/>) and the client placement ghost, so
/// both always agree. Pure functions of the immutable <see cref="Terrain"/>: thread-safe.
/// </summary>
public static class NavalRules
{
    /// <summary>Water depth over the highest corner of a cell (negative = above water). Same corner rule as
    /// <see cref="PassFlags.DeepWater"/> (depth &gt; <see cref="Terrain.MinShipDepth"/>).</summary>
    public static float CellDepth(Terrain t, int cx, int cz)
    {
        float h00 = t.VertexHeight(cx, cz), h10 = t.VertexHeight(cx + 1, cz);
        float h01 = t.VertexHeight(cx, cz + 1), h11 = t.VertexHeight(cx + 1, cz + 1);
        return t.WaterLevel - MathF.Max(MathF.Max(h00, h10), MathF.Max(h01, h11));
    }

    /// <summary>True for structures that must be built on water.</summary>
    public static bool IsWaterStructure(UnitBlueprint bp) => bp.IsStructure && bp.HasAny(UnitCategory.Naval);

    /// <summary>
    /// Static terrain rule for one footprint cell of a structure (occupancy not included): land structures need
    /// buildable land; water structures need water at least <see cref="UnitBlueprint.MinWaterDepth"/> deep.
    /// </summary>
    public static bool CellBuildable(Terrain t, UnitBlueprint bp, int cx, int cz)
    {
        var p = t.PassAt(cx, cz);
        if (!bp.HasAny(UnitCategory.Naval)) return (p & PassFlags.Land) != 0;
        if ((p & PassFlags.WaterSurface) == 0) return false;
        return bp.MinWaterDepth <= 0 || CellDepth(t, cx, cz) >= bp.MinWaterDepth;
    }
}

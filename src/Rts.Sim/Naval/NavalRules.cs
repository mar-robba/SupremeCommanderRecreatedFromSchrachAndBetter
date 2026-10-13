using System.Numerics;
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

    /// <summary>Every footprint cell inside the map and <see cref="CellBuildable"/> (mobile naval units built in place
    /// by engineers, e.g. the Atlantis, must start on water).</summary>
    public static bool FootprintBuildable(Terrain t, UnitBlueprint bp, Vector2 pos)
    {
        var (x0, z0, x1, z1) = World.OccupancyGrid.Footprint(bp, pos);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
            if (!t.CellInBounds(x, z) || !CellBuildable(t, bp, x, z)) return false;
        return true;
    }

    /// <summary>
    /// Nearest valid (<see cref="World.OccupancyGrid.CanPlace"/>) snapped site for a structure within maxRadius of
    /// `near` (ring scan, ties by scan order: deterministic). Useful to find a coastal spot for a naval factory.
    /// Read-only: call from sequential sim code, tests or setup (not concurrently with structure changes).
    /// </summary>
    public static bool FindSite(World.OccupancyGrid occ, Terrain t, UnitBlueprint bp, Vector2 near, float maxRadius, out Vector2 site)
    {
        site = near;
        int cx = (int)MathF.Floor(near.X), cz = (int)MathF.Floor(near.Y);
        int maxR = (int)MathF.Ceiling(maxRadius);
        float best = float.MaxValue;
        for (int r = 0; r <= maxR; r++)
        {
            if (best < float.MaxValue && (r - 1) * (r - 1) > best) break; // every later ring is farther
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                int x = cx + dx, z = cz + dz;
                if (!t.CellInBounds(x, z) || !CellBuildable(t, bp, x, z)) continue; // cheap reject first
                var p = World.OccupancyGrid.SnapStructure(bp, new Vector2(x + 0.5f, z + 0.5f));
                float d = Vector2.DistanceSquared(p, near);
                if (d >= best || d > maxRadius * maxRadius || !occ.CanPlace(bp, p, t)) continue;
                best = d;
                site = p;
            }
        }
        return best < float.MaxValue;
    }
}

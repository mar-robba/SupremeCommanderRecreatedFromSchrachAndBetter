using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Map;

namespace Rts.Sim.World;

/// <summary>
/// Which structure occupies each terrain cell (-1 = free). Updated sequentially when structures are
/// placed/destroyed; read by placement validation and pathfinding. <see cref="Version"/> increments on every
/// change so path caches can be invalidated.
/// </summary>
public sealed class OccupancyGrid
{
    public readonly int Width;
    public readonly int Height;
    public readonly int[] Cells;
    public int Version { get; private set; }

    public OccupancyGrid(int width, int height)
    {
        Width = width;
        Height = height;
        Cells = new int[width * height];
        Array.Fill(Cells, -1);
    }

    public bool IsBlocked(int cx, int cz) =>
        cx < 0 || cz < 0 || cx >= Width || cz >= Height || Cells[cz * Width + cx] >= 0;

    public int At(int cx, int cz) =>
        cx < 0 || cz < 0 || cx >= Width || cz >= Height ? -1 : Cells[cz * Width + cx];

    /// <summary>Footprint rectangle (in cells) of a structure centered at pos.</summary>
    public static (int x0, int z0, int x1, int z1) Footprint(UnitBlueprint bp, Vector2 pos)
    {
        int x0 = (int)MathF.Round(pos.X - bp.FootprintX * 0.5f);
        int z0 = (int)MathF.Round(pos.Y - bp.FootprintZ * 0.5f);
        return (x0, z0, x0 + bp.FootprintX - 1, z0 + bp.FootprintZ - 1);
    }

    /// <summary>Snaps a structure center so its footprint aligns to cell boundaries.</summary>
    public static Vector2 SnapStructure(UnitBlueprint bp, Vector2 pos)
    {
        var (x0, z0, _, _) = Footprint(bp, pos);
        return new Vector2(x0 + bp.FootprintX * 0.5f, z0 + bp.FootprintZ * 0.5f);
    }

    public void Stamp(UnitBlueprint bp, Vector2 pos, int unitIndex)
    {
        var (x0, z0, x1, z1) = Footprint(bp, pos);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
            if (x >= 0 && z >= 0 && x < Width && z < Height) Cells[z * Width + x] = unitIndex;
        Version++;
    }

    public void Clear(UnitBlueprint bp, Vector2 pos, int unitIndex)
    {
        var (x0, z0, x1, z1) = Footprint(bp, pos);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
            if (x >= 0 && z >= 0 && x < Width && z < Height && Cells[z * Width + x] == unitIndex) Cells[z * Width + x] = -1;
        Version++;
    }

    /// <summary>True if the footprint is inside the map, unoccupied and on suitable terrain for the structure.</summary>
    public bool CanPlace(UnitBlueprint bp, Vector2 pos, Terrain terrain)
    {
        var (x0, z0, x1, z1) = Footprint(bp, pos);
        bool naval = bp.HasAny(UnitCategory.Naval);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            if (IsBlocked(x, z)) return false;
            var p = terrain.PassAt(x, z);
            if (naval ? (p & PassFlags.WaterSurface) == 0 : (p & PassFlags.Land) == 0) return false;
        }
        if (bp.Economy.RequiresMassDeposit || bp.Economy.RequiresHydroDeposit)
        {
            var type = bp.Economy.RequiresMassDeposit ? DepositType.Mass : DepositType.Hydrocarbon;
            bool found = false;
            foreach (var d in terrain.Deposits)
                if (d.Type == type && Vector2.DistanceSquared(d.Pos, pos) < 1.0f) { found = true; break; }
            if (!found) return false;
        }
        return true;
    }
}

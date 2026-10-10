using System.Numerics;

namespace Rts.Sim.World;

/// <summary>Callback for allocation-free spatial queries. Return false to stop early.</summary>
public interface IUnitVisitor
{
    bool Visit(int unitIndex);
}

/// <summary>
/// Uniform grid over unit positions, rebuilt once per tick (counting sort) from positions at that moment.
/// Read-only during parallel phases, so any number of threads may query it concurrently.
/// Query results are in deterministic order (cell order, then ascending unit index).
/// </summary>
public sealed class SpatialGrid
{
    public readonly float CellSize;
    public readonly int Cols;
    public readonly int Rows;
    private readonly int[] _cellStart;
    private readonly int[] _entries;
    private readonly int[] _cellOfUnit;
    private readonly float _inv;

    public SpatialGrid(float worldWidth, float worldHeight, float cellSize, int unitCapacity)
    {
        CellSize = cellSize;
        _inv = 1f / cellSize;
        Cols = Math.Max(1, (int)MathF.Ceiling(worldWidth / cellSize));
        Rows = Math.Max(1, (int)MathF.Ceiling(worldHeight / cellSize));
        _cellStart = new int[Cols * Rows + 1];
        _entries = new int[unitCapacity];
        _cellOfUnit = new int[unitCapacity];
    }

    public int CellIndexOf(Vector2 p)
    {
        int cx = Math.Clamp((int)(p.X * _inv), 0, Cols - 1);
        int cz = Math.Clamp((int)(p.Y * _inv), 0, Rows - 1);
        return cz * Cols + cx;
    }

    public void Rebuild(UnitStore units)
    {
        Array.Clear(_cellStart);
        int n = units.AliveCount;
        var list = units.AliveList;
        for (int k = 0; k < n; k++)
        {
            int i = list[k];
            int c = CellIndexOf(units.Pos[i]);
            _cellOfUnit[i] = c;
            _cellStart[c + 1]++;
        }
        for (int c = 0; c < Cols * Rows; c++) _cellStart[c + 1] += _cellStart[c];
        // Stable placement in ascending unit order (AliveList is ascending) -> deterministic.
        Span<int> cursor = _cellStart.Length <= 4096 ? stackalloc int[_cellStart.Length] : new int[_cellStart.Length];
        _cellStart.AsSpan().CopyTo(cursor);
        for (int k = 0; k < n; k++)
        {
            int i = list[k];
            _entries[cursor[_cellOfUnit[i]]++] = i;
        }
    }

    public ReadOnlySpan<int> Cell(int cx, int cz)
    {
        int c = cz * Cols + cx;
        return _entries.AsSpan(_cellStart[c], _cellStart[c + 1] - _cellStart[c]);
    }

    /// <summary>Visits units whose CELL overlaps the circle; the visitor must check exact distance itself.</summary>
    public void ForEachInRadius<T>(Vector2 center, float radius, ref T visitor) where T : struct, IUnitVisitor
    {
        int x0 = Math.Clamp((int)((center.X - radius) * _inv), 0, Cols - 1);
        int x1 = Math.Clamp((int)((center.X + radius) * _inv), 0, Cols - 1);
        int z0 = Math.Clamp((int)((center.Y - radius) * _inv), 0, Rows - 1);
        int z1 = Math.Clamp((int)((center.Y + radius) * _inv), 0, Rows - 1);
        for (int cz = z0; cz <= z1; cz++)
        for (int cx = x0; cx <= x1; cx++)
        {
            int c = cz * Cols + cx;
            for (int e = _cellStart[c]; e < _cellStart[c + 1]; e++)
                if (!visitor.Visit(_entries[e])) return;
        }
    }

    /// <summary>Convenience (allocating-friendly) query: appends units within radius (exact distance) to results.</summary>
    public void QueryRadius(UnitStore units, Vector2 center, float radius, List<int> results)
    {
        float r2 = radius * radius;
        int x0 = Math.Clamp((int)((center.X - radius) * _inv), 0, Cols - 1);
        int x1 = Math.Clamp((int)((center.X + radius) * _inv), 0, Cols - 1);
        int z0 = Math.Clamp((int)((center.Y - radius) * _inv), 0, Rows - 1);
        int z1 = Math.Clamp((int)((center.Y + radius) * _inv), 0, Rows - 1);
        for (int cz = z0; cz <= z1; cz++)
        for (int cx = x0; cx <= x1; cx++)
        {
            int c = cz * Cols + cx;
            for (int e = _cellStart[c]; e < _cellStart[c + 1]; e++)
            {
                int i = _entries[e];
                if (Vector2.DistanceSquared(units.Pos[i], center) <= r2) results.Add(i);
            }
        }
    }
}

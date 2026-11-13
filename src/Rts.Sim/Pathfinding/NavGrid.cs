using System.Numerics;
using System.Runtime.CompilerServices;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Sim.Pathfinding;

/// <summary>
/// Clearance grid of one surface motion layer (Land, Hover, Amphibious, Water; Sub shares Water) and one slope class.
/// (11a-1) <see cref="Clear"/>[cell] = squared Euclidean distance, in HALF cells, from the cell center to the nearest
/// point of a cell that is blocked for this layer (terrain slope/water for <see cref="MaxSlope"/>, structures via
/// <see cref="OccupancyGrid"/>, the map border): ax^2 + az^2 with ax = max(0, 2|dx| - 1), capped at <see cref="MaxReq"/>.
/// 0 = blocked, 1 = free with a blocked edge neighbour, 2 = only a diagonal one, 9 = the 3x3 block is free, ...
/// A unit of radius r stands where Clear &gt;= <see cref="ClearanceFor"/>(r) ("one question, one function": the path
/// search, the local movement, FindNearestPassable and teleport landing all use <see cref="Passable(int,int,int)"/>).
/// Written only in sequential phases (PathService.UpdateNav); read concurrently by parallel systems.
/// </summary>
public sealed class NavGrid
{
    /// <summary>Default cap: the requirement of radius 3.0 (the largest mobile radius, DEV_GUIDE "Sim pitfalls").</summary>
    public const int MaxClearance = 34;

    public readonly MotionLayer Layer;
    /// <summary>Steepest cell (vertex height range) this grid's units may stand on (Land/Hover/Amphibious).</summary>
    public readonly float MaxSlope;
    /// <summary>Cap of <see cref="Clear"/> (largest requirement of any unit using this grid).</summary>
    public readonly int MaxReq;
    /// <summary>Window in cells: blocked cells Reach or more cells away never lower a value below MaxReq.</summary>
    public readonly int Reach;
    public readonly int Width;
    public readonly int Height;
    public readonly byte[] Clear;
    /// <summary>Incremented whenever any clearance value changes (structures placed/removed).</summary>
    public int Version;

    private readonly Terrain _terrain;
    private readonly OccupancyGrid _occ;
    /// <summary>Static terrain traversability for this layer and slope (1 = traversable).</summary>
    private readonly byte[] _terrainOk;

    public NavGrid(Terrain terrain, OccupancyGrid occ, MotionLayer layer)
        : this(terrain, occ, layer, Terrain.DefaultMaxSlope, MaxClearance) { }

    public NavGrid(Terrain terrain, OccupancyGrid occ, MotionLayer layer, float maxSlope, int maxReq)
    {
        Layer = layer;
        MaxSlope = maxSlope;
        MaxReq = Math.Clamp(maxReq, 1, 255);
        Reach = ReachFor(MaxReq);
        _terrain = terrain;
        _occ = occ;
        Width = terrain.Width;
        Height = terrain.Height;
        Clear = new byte[Width * Height];
        _terrainOk = new byte[Width * Height];
        int w = Width;
        var ok = _terrainOk;
        Par.For(Height, 32, (_, z0, z1) =>
        {
            for (int z = z0; z < z1; z++)
            for (int x = 0; x < w; x++)
                ok[z * w + x] = Traversable(terrain, layer, maxSlope, x, z) ? (byte)1 : (byte)0;
        });
        RecomputeAll();
    }

    /// <summary>
    /// Clearance requirement of a unit of the given radius: the smallest value <see cref="Clear"/> can take that is
    /// &gt;= (2 (r - 0.25))^2, i.e. the unit's disc at a cell center may overlap a blocked cell by at most 0.25
    /// (a gap of W cells admits the unit when W &gt;= 2r + 0.5, never when W &lt; 2r - 0.5). r &lt; 0.75 -&gt; 1 (any
    /// free cell, as before). Only values Clear can take, so radii that fit the same cells share one graph.
    /// </summary>
    public static int ClearanceFor(float radius)
    {
        if (radius < 0.75f) return 1;
        float x = 2f * (radius - 0.25f);
        int t = Math.Max(1, (int)MathF.Ceiling(x * x - 1e-4f));
        return Math.Min(255, CanonicalReq(t));
    }

    /// <summary>Smallest a^2 + b^2 &gt;= t with a, b in {0, 1, 3, 5, ...} (the values Clear can take).</summary>
    public static int CanonicalReq(int t)
    {
        if (t <= 1) return Math.Max(t, 0);
        int best = int.MaxValue;
        for (int a = 0; a * a < best && a <= 63; a = a == 0 ? 1 : a + 2)
        {
            int rest = t - a * a, b = 0;
            if (rest > 0)
            {
                b = 1;
                while (b * b < rest) b += 2;
            }
            best = Math.Min(best, a * a + b * b);
        }
        return best;
    }

    /// <summary>Cells needed around a cell to compute values up to maxReq: smallest d with (2d - 1)^2 &gt;= maxReq.</summary>
    public static int ReachFor(int maxReq)
    {
        int d = 1;
        while ((2 * d - 1) * (2 * d - 1) < maxReq) d++;
        return d;
    }

    /// <summary>
    /// Terrain traversability of a cell for a layer and slope limit. At <see cref="Terrain.DefaultMaxSlope"/> this is
    /// exactly <see cref="Terrain.CanTraverse"/>; other limits redo its slope test with the same float operations.
    /// </summary>
    public static bool Traversable(Terrain t, MotionLayer layer, float maxSlope, int cx, int cz)
    {
        if (maxSlope == Terrain.DefaultMaxSlope || layer is not (MotionLayer.Land or MotionLayer.Hover or MotionLayer.Amphibious))
            return t.CanTraverse(layer, cx, cz);
        if (!t.CellInBounds(cx, cz)) return false;
        float h00 = t.VertexHeight(cx, cz), h10 = t.VertexHeight(cx + 1, cz);
        float h01 = t.VertexHeight(cx, cz + 1), h11 = t.VertexHeight(cx + 1, cz + 1);
        float min = MathF.Min(MathF.Min(h00, h10), MathF.Min(h01, h11));
        float max = MathF.Max(MathF.Max(h00, h10), MathF.Max(h01, h11));
        bool gentle = max - min <= maxSlope;
        return layer switch
        {
            MotionLayer.Land => gentle && min >= t.WaterLevel,
            MotionLayer.Hover => gentle && min >= t.WaterLevel || min < t.WaterLevel,
            _ => gentle, // Amphibious: land or sea bed
        };
    }

    private bool BlockedRaw(int x, int z) =>
        (uint)x >= (uint)Width || (uint)z >= (uint)Height || _terrainOk[z * Width + x] == 0 || _occ.Cells[z * Width + x] >= 0;

    public bool Passable(int cx, int cz, int req) =>
        (uint)cx < (uint)Width && (uint)cz < (uint)Height && Clear[cz * Width + cx] >= req;

    public bool Passable(int cell, int req) => Clear[cell] >= req;

    /// <summary>True if a unit (point) may stand at p (cell free of terrain/structure blocking).</summary>
    public bool CanStand(Vector2 p) => Passable((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), 1);

    /// <summary>(11a-1) True if a unit with requirement req (<see cref="ClearanceFor"/>) may stand at p.</summary>
    public bool Fits(Vector2 p, int req) => Passable((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), req);

    /// <summary>(11a-1) <see cref="Clear"/> of the cell containing p (0 outside the map).</summary>
    public int ClearAt(Vector2 p)
    {
        int cx = (int)MathF.Floor(p.X), cz = (int)MathF.Floor(p.Y);
        return (uint)cx < (uint)Width && (uint)cz < (uint)Height ? Clear[cz * Width + cx] : 0;
    }

    public int CellOf(Vector2 p)
    {
        int cx = Math.Clamp((int)MathF.Floor(p.X), 0, Width - 1);
        int cz = Math.Clamp((int)MathF.Floor(p.Y), 0, Height - 1);
        return cz * Width + cx;
    }

    public Vector2 CenterOf(int cell) => new(cell % Width + 0.5f, cell / Width + 0.5f);

    // ------------------------------------------------------------------ clearance computation

    /// <summary>
    /// Full recompute. Separable: rowD = 1D cell distance to the nearest blocked cell in the row, then
    /// d(x,z) = min over |dz| &lt; Reach of Sq(|dz|) + Sq(rowD(x, z+dz)). Rows are independent -> parallel.
    /// </summary>
    private void RecomputeAll()
    {
        var rowD = new byte[Width * Height];
        Par.For(Height, 32, (_, z0, z1) =>
        {
            for (int z = z0; z < z1; z++) RowDistance(z, 0, Width - 1, rowD.AsSpan(z * Width, Width));
        });
        Par.For(Height, 32, (_, z0, z1) =>
        {
            for (int z = z0; z < z1; z++)
            for (int x = 0; x < Width; x++)
                Clear[z * Width + x] = ColumnCombine(x, z, rowD, Width, 0, 0);
        });
        Version++;
    }

    /// <summary>
    /// Recomputes clearance for cells within Reach-1 of the changed rectangle (inclusive cell coords).
    /// Returns the rectangle of cells whose clearance actually changed (empty if none: x0 &gt; x1).
    /// </summary>
    public (int x0, int z0, int x1, int z1) RecomputeAround(int cx0, int cz0, int cx1, int cz1)
    {
        int C = Reach;
        int x0 = Math.Max(0, cx0 - (C - 1)), x1 = Math.Min(Width - 1, cx1 + (C - 1));
        int z0 = Math.Max(0, cz0 - (C - 1)), z1 = Math.Min(Height - 1, cz1 + (C - 1));
        int rz0 = Math.Max(0, z0 - (C - 1)), rz1 = Math.Min(Height - 1, z1 + (C - 1));
        int w = x1 - x0 + 1;
        var rowD = new byte[w * (rz1 - rz0 + 1)];
        for (int z = rz0; z <= rz1; z++) RowDistance(z, x0, x1, rowD.AsSpan((z - rz0) * w, w));
        int mx0 = int.MaxValue, mz0 = int.MaxValue, mx1 = int.MinValue, mz1 = int.MinValue;
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            byte d = ColumnCombine(x, z, rowD, w, x0, rz0, rz1);
            ref byte c = ref Clear[z * Width + x];
            if (c == d) continue;
            c = d;
            mx0 = Math.Min(mx0, x); mx1 = Math.Max(mx1, x);
            mz0 = Math.Min(mz0, z); mz1 = Math.Max(mz1, z);
        }
        if (mx0 == int.MaxValue) return (1, 1, 0, 0);
        Version++;
        return (mx0, mz0, mx1, mz1);
    }

    /// <summary>1D cell distance (capped at Reach) to the nearest blocked cell of row z, for columns xa..xb;
    /// out-of-map is blocked.</summary>
    private void RowDistance(int z, int xa, int xb, Span<byte> outD)
    {
        int C = Reach;
        int lo = xa - C, hi = xb + C;
        int last = lo - C;
        for (int x = lo; x <= hi; x++)
        {
            if (BlockedRaw(x, z)) last = x;
            if (x >= xa && x <= xb) outD[x - xa] = (byte)Math.Min(C, x - last);
        }
        int next = hi + C;
        for (int x = hi; x >= lo; x--)
        {
            if (BlockedRaw(x, z)) next = x;
            if (x >= xa && x <= xb)
            {
                int d = next - x;
                if (d < outD[x - xa]) outD[x - xa] = (byte)d;
            }
        }
    }

    /// <summary>Squared half-cell distance from a cell center to the near edge of a cell d cells away (0 -> 0).</summary>
    private static int Sq(int d) => d == 0 ? 0 : (2 * d - 1) * (2 * d - 1);

    private byte ColumnCombine(int x, int z, byte[] rowD, int stride, int xOff, int zOff, int zMax = int.MaxValue)
    {
        int C = Reach;
        int best = MaxReq;
        if (zMax == int.MaxValue) zMax = Height - 1;
        for (int dz = -(C - 1); dz <= C - 1; dz++)
        {
            int rz = z + dz;
            int sz = Sq(Math.Abs(dz));
            if (sz >= best) continue;
            int cand;
            if (rz < 0 || rz >= Height) cand = sz; // outside the map counts as blocked
            else if (rz < zOff || rz > zMax) continue; // never happens: callers include all needed rows
            else cand = sz + Sq(rowD[(rz - zOff) * stride + (x - xOff)]);
            if (cand < best) best = cand;
        }
        return (byte)best;
    }

    // ------------------------------------------------------------------ queries

    /// <summary>
    /// Line of sight for a unit of requirement req: every cell crossed by segment a-&gt;b (supercover: both side cells
    /// at exact corner crossings) must have Clear &gt;= req. The start cell is not tested (units may stand on a
    /// blocked cell, e.g. right after a structure was placed on them).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool LineOfSight(Vector2 a, Vector2 b, int req)
    {
        int x = (int)MathF.Floor(a.X), z = (int)MathF.Floor(a.Y);
        int tx = (int)MathF.Floor(b.X), tz = (int)MathF.Floor(b.Y);
        if (x == tx && z == tz) return true;
        float dx = b.X - a.X, dz = b.Y - a.Y;
        int stepX = dx > 0 ? 1 : dx < 0 ? -1 : 0;
        int stepZ = dz > 0 ? 1 : dz < 0 ? -1 : 0;
        float tDeltaX = stepX != 0 ? 1f / MathF.Abs(dx) : float.MaxValue;
        float tDeltaZ = stepZ != 0 ? 1f / MathF.Abs(dz) : float.MaxValue;
        float tMaxX = stepX > 0 ? (x + 1 - a.X) * tDeltaX : stepX < 0 ? (a.X - x) * tDeltaX : float.MaxValue;
        float tMaxZ = stepZ > 0 ? (z + 1 - a.Y) * tDeltaZ : stepZ < 0 ? (a.Y - z) * tDeltaZ : float.MaxValue;
        int steps = Math.Abs(tx - x) + Math.Abs(tz - z);
        const float Eps = 1e-5f;
        while (steps > 0)
        {
            if (tMaxX < tMaxZ - Eps)
            {
                x += stepX; tMaxX += tDeltaX; steps--;
            }
            else if (tMaxZ < tMaxX - Eps)
            {
                z += stepZ; tMaxZ += tDeltaZ; steps--;
            }
            else
            {
                // Passing (almost) exactly through a corner: both side cells must be free (no corner cutting).
                if (!Passable(x + stepX, z, req) || !Passable(x, z + stepZ, req)) return false;
                x += stepX; z += stepZ; tMaxX += tDeltaX; tMaxZ += tDeltaZ; steps -= 2;
            }
            if (!Passable(x, z, req)) return false;
        }
        return true;
    }

    /// <summary>
    /// Nearest cell (by squared distance between cell centers, ties by ring scan order) with Clear &gt;= req within
    /// maxRadius cells of (cx, cz). Deterministic.
    /// </summary>
    public bool FindNearestPassable(int cx, int cz, int req, int maxRadius, out int ox, out int oz)
    {
        ox = cx; oz = cz;
        if (Passable(cx, cz, req)) return true;
        int best = int.MaxValue;
        for (int r = 1; r <= maxRadius; r++)
        {
            if (r * r > best) break; // every cell of this ring is at least r away
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                int d2 = dx * dx + dz * dz;
                if (d2 >= best || !Passable(cx + dx, cz + dz, req)) continue;
                best = d2; ox = cx + dx; oz = cz + dz;
            }
        }
        return best != int.MaxValue;
    }
}

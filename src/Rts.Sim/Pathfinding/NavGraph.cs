using System.Numerics;
using Rts.Sim.Core;

namespace Rts.Sim.Pathfinding;

/// <summary>
/// Hierarchical abstraction (HPA*-style) of a <see cref="NavGrid"/> for one clearance class.
/// The map is cut into <see cref="S"/>x<see cref="S"/> sectors; inside each sector the cells passable for the
/// clearance class are split into 4-connected <b>regions</b>. Regions of neighbouring sectors that touch are linked
/// by portal edges (cost = rep -> portal midpoint -> rep). Connected components over that graph give O(1)
/// reachability tests. Structures change only the sectors they touch: those are relabelled and the global arrays
/// are rebuilt (cheap: a few thousand regions), bumping <see cref="Version"/>.
/// Mutated only in sequential phases; read concurrently by path solvers.
/// </summary>
public sealed class NavGraph
{
    public const int S = 16;
    public const ushort NoRegion = 0xFFFF;

    public readonly NavGrid Grid;
    public readonly int Req;
    /// <summary>(11a-1) Index in PathService's graph table (grid slot * stride + Req): the goal-field cache key.</summary>
    public int Slot;
    public readonly int SX, SY, SectorCount;
    /// <summary>Local region id of each cell inside its sector (NoRegion = not passable for Req).</summary>
    public readonly ushort[] Local;
    public int Version;

    private struct RegionInfo
    {
        public int Rep;       // representative cell (member cell closest to the region centroid)
        public int Cells;
        public int MinX, MinZ, MaxX, MaxZ; // bounding box in cells
    }

    private struct Portal
    {
        public ushort A, B;   // local region in the west/north sector, local region in the east/south sector
        public Vector2 Mid;   // portal midpoint (on the shared border)
    }

    private readonly RegionInfo[][] _sectorRegions;
    private readonly int[] _sectorRegionCount;
    /// <summary>Portals per border: index sector*2 + 0 (east border) / + 1 (south border).</summary>
    private readonly List<Portal>[] _borders;

    // Global arrays (rebuilt by Finalize).
    public int RegionCount { get; private set; }
    public int[] SectorBase = [];
    public int[] RegSector = [];
    public int[] RegRep = [];
    public int[] RegComp = [];
    public int[] EdgeStart = [];
    public int[] EdgeTo = [];
    public int[] EdgeCost = [];

    public NavGraph(NavGrid grid, int req)
    {
        Grid = grid;
        Req = req;
        SX = (grid.Width + S - 1) / S;
        SY = (grid.Height + S - 1) / S;
        SectorCount = SX * SY;
        Local = new ushort[grid.Width * grid.Height];
        _sectorRegions = new RegionInfo[SectorCount][];
        _sectorRegionCount = new int[SectorCount];
        _borders = new List<Portal>[SectorCount * 2];
        for (int i = 0; i < _borders.Length; i++) _borders[i] = new List<Portal>();
        Par.For(SectorCount, 16, (_, a, b) => { for (int s = a; s < b; s++) LabelSector(s); });
        Par.For(SectorCount, 16, (_, a, b) => { for (int s = a; s < b; s++) { ScanBorder(s, 0); ScanBorder(s, 1); } });
        Rebuild();
    }

    public int SectorOfCell(int cell) => (cell % Grid.Width) / S + (cell / Grid.Width) / S * SX;

    /// <summary>Global region of a cell, or -1 if the cell is not passable for this clearance class.</summary>
    public int RegionOfCell(int cell)
    {
        ushort l = Local[cell];
        return l == NoRegion ? -1 : SectorBase[SectorOfCell(cell)] + l;
    }

    public int ComponentOfCell(int cell)
    {
        int r = RegionOfCell(cell);
        return r < 0 ? -1 : RegComp[r];
    }

    /// <summary>Marks the sectors overlapping the (inclusive) cell rectangle for relabelling. SEQUENTIAL.</summary>
    public void MarkDirty(int x0, int z0, int x1, int z1)
    {
        _dirty ??= new bool[SectorCount];
        int sx0 = Math.Max(0, x0 / S), sx1 = Math.Min(SX - 1, x1 / S);
        int sz0 = Math.Max(0, z0 / S), sz1 = Math.Min(SY - 1, z1 / S);
        for (int sz = sz0; sz <= sz1; sz++)
        for (int sx = sx0; sx <= sx1; sx++)
            _dirty[sz * SX + sx] = true;
        _anyDirty = true;
    }

    /// <summary>Relabels dirty sectors, rescans the borders touching them and rebuilds the graph once. SEQUENTIAL.</summary>
    public void CommitDirty()
    {
        if (!_anyDirty || _dirty == null) return;
        _anyDirty = false;
        for (int s = 0; s < SectorCount; s++)
            if (_dirty[s]) LabelSector(s);
        for (int s = 0; s < SectorCount; s++)
        {
            int sx = s % SX, sz = s / SX;
            bool self = _dirty[s];
            // East border touches s and s+1, south border touches s and s+SX.
            if (self || (sx + 1 < SX && _dirty[s + 1])) ScanBorder(s, 0);
            if (self || (sz + 1 < SY && _dirty[s + SX])) ScanBorder(s, 1);
        }
        Array.Clear(_dirty);
        Rebuild();
    }

    private bool[]? _dirty;
    private bool _anyDirty;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private void LabelSector(int s)
    {
        int w = Grid.Width;
        int sx = s % SX, sz = s / SX;
        int cx0 = sx * S, cz0 = sz * S;
        int cx1 = Math.Min(Grid.Width, cx0 + S) - 1, cz1 = Math.Min(Grid.Height, cz0 + S) - 1;
        for (int z = cz0; z <= cz1; z++) Local.AsSpan(z * w + cx0, cx1 - cx0 + 1).Fill(NoRegion);
        Span<int> stack = stackalloc int[S * S];
        var regions = _sectorRegions[s] ??= new RegionInfo[4];
        int count = 0;
        for (int z = cz0; z <= cz1; z++)
        for (int x = cx0; x <= cx1; x++)
        {
            int c = z * w + x;
            if (Local[c] != NoRegion || Grid.Clear[c] < Req) continue;
            ushort id = (ushort)count;
            int sp = 0, n = 0;
            long sumX = 0, sumZ = 0;
            var info = new RegionInfo { MinX = x, MinZ = z, MaxX = x, MaxZ = z };
            Local[c] = id;
            stack[sp++] = c;
            while (sp > 0)
            {
                int cur = stack[--sp];
                int px = cur % w, pz = cur / w;
                n++; sumX += px; sumZ += pz;
                if (px < info.MinX) info.MinX = px;
                if (px > info.MaxX) info.MaxX = px;
                if (pz < info.MinZ) info.MinZ = pz;
                if (pz > info.MaxZ) info.MaxZ = pz;
                if (px > cx0 && Local[cur - 1] == NoRegion && Grid.Clear[cur - 1] >= Req) { Local[cur - 1] = id; stack[sp++] = cur - 1; }
                if (px < cx1 && Local[cur + 1] == NoRegion && Grid.Clear[cur + 1] >= Req) { Local[cur + 1] = id; stack[sp++] = cur + 1; }
                if (pz > cz0 && Local[cur - w] == NoRegion && Grid.Clear[cur - w] >= Req) { Local[cur - w] = id; stack[sp++] = cur - w; }
                if (pz < cz1 && Local[cur + w] == NoRegion && Grid.Clear[cur + w] >= Req) { Local[cur + w] = id; stack[sp++] = cur + w; }
            }
            info.Cells = n;
            // Representative: member cell closest to the centroid (ties: first in row-major order).
            float mx = (float)sumX / n, mz = (float)sumZ / n;
            float best = float.MaxValue;
            for (int rz = info.MinZ; rz <= info.MaxZ; rz++)
            for (int rx = info.MinX; rx <= info.MaxX; rx++)
            {
                int rc = rz * w + rx;
                if (Local[rc] != id) continue;
                float d = (rx - mx) * (rx - mx) + (rz - mz) * (rz - mz);
                if (d < best) { best = d; info.Rep = rc; }
            }
            if (count == regions.Length) Array.Resize(ref regions, count * 2);
            regions[count++] = info;
        }
        _sectorRegions[s] = regions;
        _sectorRegionCount[s] = count;
    }

    /// <summary>Finds portals (runs of touching cell pairs between the same two regions) on a sector border.</summary>
    private void ScanBorder(int s, int dir)
    {
        var list = _borders[s * 2 + dir];
        list.Clear();
        int w = Grid.Width;
        int sx = s % SX, sz = s / SX;
        if (dir == 0 && sx + 1 >= SX) return;
        if (dir == 1 && sz + 1 >= SY) return;
        int len = dir == 0 ? Math.Min(Grid.Height, sz * S + S) - sz * S : Math.Min(Grid.Width, sx * S + S) - sx * S;
        int runA = -1, runB = -1, runStart = 0;
        for (int k = 0; k <= len; k++)
        {
            int a = -1, b = -1;
            if (k < len)
            {
                int ca, cb;
                if (dir == 0) { int x = sx * S + S - 1, z = sz * S + k; ca = z * w + x; cb = ca + 1; }
                else { int x = sx * S + k, z = sz * S + S - 1; ca = z * w + x; cb = ca + w; }
                if (Local[ca] != NoRegion && Local[cb] != NoRegion) { a = Local[ca]; b = Local[cb]; }
            }
            if (a == runA && b == runB && a >= 0) continue;
            if (runA >= 0)
            {
                float mid = (runStart + k - 1) * 0.5f + 0.5f;
                var p = dir == 0
                    ? new Vector2(sx * S + S, sz * S + mid)
                    : new Vector2(sx * S + mid, sz * S + S);
                list.Add(new Portal { A = (ushort)runA, B = (ushort)runB, Mid = p });
            }
            runA = a; runB = b; runStart = k;
        }
    }

    /// <summary>Rebuilds global region ids, CSR adjacency and connected components.</summary>
    private void Rebuild()
    {
        if (SectorBase.Length != SectorCount + 1) SectorBase = new int[SectorCount + 1];
        int n = 0;
        for (int s = 0; s < SectorCount; s++) { SectorBase[s] = n; n += _sectorRegionCount[s]; }
        SectorBase[SectorCount] = n;
        RegionCount = n;
        if (RegSector.Length < n)
        {
            int cap = Math.Max(n, RegSector.Length * 3 / 2);
            RegSector = new int[cap];
            RegRep = new int[cap];
            RegComp = new int[cap];
            EdgeStart = new int[cap + 1];
        }
        for (int s = 0; s < SectorCount; s++)
        {
            var regs = _sectorRegions[s];
            for (int l = 0; l < _sectorRegionCount[s]; l++)
            {
                RegSector[SectorBase[s] + l] = s;
                RegRep[SectorBase[s] + l] = regs[l].Rep;
            }
        }

        // Degree count, then fill (each portal yields two directed edges).
        Array.Clear(EdgeStart, 0, n + 1);
        int edges = 0;
        for (int s = 0; s < SectorCount; s++)
        for (int dir = 0; dir < 2; dir++)
        {
            int t = dir == 0 ? s + 1 : s + SX;
            foreach (var p in _borders[s * 2 + dir])
            {
                EdgeStart[SectorBase[s] + p.A + 1]++;
                EdgeStart[SectorBase[t] + p.B + 1]++;
                edges += 2;
            }
        }
        for (int r = 0; r < n; r++) EdgeStart[r + 1] += EdgeStart[r];
        if (EdgeTo.Length < edges)
        {
            EdgeTo = new int[Math.Max(edges, EdgeTo.Length * 3 / 2)];
            EdgeCost = new int[EdgeTo.Length];
        }
        var cursor = new int[n];
        for (int r = 0; r < n; r++) cursor[r] = EdgeStart[r];
        int w = Grid.Width;
        for (int s = 0; s < SectorCount; s++)
        for (int dir = 0; dir < 2; dir++)
        {
            int t = dir == 0 ? s + 1 : s + SX;
            foreach (var p in _borders[s * 2 + dir])
            {
                int ra = SectorBase[s] + p.A, rb = SectorBase[t] + p.B;
                var pa = new Vector2(RegRep[ra] % w + 0.5f, RegRep[ra] / w + 0.5f);
                var pb = new Vector2(RegRep[rb] % w + 0.5f, RegRep[rb] / w + 0.5f);
                int cost = (int)((Vector2.Distance(pa, p.Mid) + Vector2.Distance(p.Mid, pb)) * 10f) + 1;
                EdgeTo[cursor[ra]] = rb; EdgeCost[cursor[ra]++] = cost;
                EdgeTo[cursor[rb]] = ra; EdgeCost[cursor[rb]++] = cost;
            }
        }

        // Components (union-find); component id = smallest region index in it (deterministic).
        var parent = new int[n];
        for (int r = 0; r < n; r++) parent[r] = r;
        int Find(int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }
        for (int r = 0; r < n; r++)
        for (int e = EdgeStart[r]; e < EdgeStart[r + 1]; e++)
        {
            int a = Find(r), b = Find(EdgeTo[e]);
            if (a == b) continue;
            if (a < b) parent[b] = a; else parent[a] = b;
        }
        for (int r = 0; r < n; r++) RegComp[r] = Find(r);
        Version++;
    }

    /// <summary>
    /// Nearest passable cell (to point p) whose component is comp. Searches sector rings outward from p; regions are
    /// pruned by bounding box. Deterministic (fixed scan order, strict improvement). Returns -1 if none.
    /// </summary>
    public int NearestCellInComponent(Vector2 p, int comp)
    {
        int w = Grid.Width;
        int psx = Math.Clamp((int)(p.X / S), 0, SX - 1), psz = Math.Clamp((int)(p.Y / S), 0, SY - 1);
        float best = float.MaxValue;
        int bestCell = -1;
        int maxRing = Math.Max(SX, SY);
        for (int ring = 0; ring <= maxRing; ring++)
        {
            float lb = (ring - 1) * S;
            if (lb > 0 && lb * lb >= best) break;
            for (int sz = psz - ring; sz <= psz + ring; sz++)
            {
                if (sz < 0 || sz >= SY) continue;
                for (int sx = psx - ring; sx <= psx + ring; sx++)
                {
                    if (sx < 0 || sx >= SX) continue;
                    if (Math.Max(Math.Abs(sx - psx), Math.Abs(sz - psz)) != ring) continue;
                    int s = sz * SX + sx;
                    var regs = _sectorRegions[s];
                    for (int l = 0; l < _sectorRegionCount[s]; l++)
                    {
                        if (RegComp[SectorBase[s] + l] != comp) continue;
                        ref var ri = ref regs[l];
                        float bx = Math.Clamp(p.X, ri.MinX + 0.5f, ri.MaxX + 0.5f) - p.X;
                        float bz = Math.Clamp(p.Y, ri.MinZ + 0.5f, ri.MaxZ + 0.5f) - p.Y;
                        if (bx * bx + bz * bz >= best) continue;
                        for (int z = ri.MinZ; z <= ri.MaxZ; z++)
                        for (int x = ri.MinX; x <= ri.MaxX; x++)
                        {
                            int c = z * w + x;
                            if (Local[c] != l) continue;
                            float dx = x + 0.5f - p.X, dz = z + 0.5f - p.Y;
                            float d = dx * dx + dz * dz;
                            if (d < best) { best = d; bestCell = c; }
                        }
                    }
                }
            }
        }
        return bestCell;
    }
}

/// <summary>
/// Region-level "flow field" toward one goal region: Dijkstra over the region graph from the goal. Every region of
/// the goal's component learns its distance and the next region on the way. Shared (cached) by all units of the
/// same layer/clearance heading to the same goal region, whatever their start.
/// </summary>
public sealed class GoalField
{
    public readonly int GraphVersion;
    public readonly int GoalRegion;
    public readonly int[] Dist;
    public readonly int[] Next;
    public int LastUsedTick;

    [ThreadStatic, Persistence.NoSave] private static PriorityQueue<int, long>? t_heap;

    /// <param name="svc">Recycles arrays of evicted fields (null = new arrays). The arrays may be longer than the
    /// region count (entries past it stay unreached).</param>
    public GoalField(NavGraph g, int goalRegion, Systems.PathService? svc = null)
    {
        GraphVersion = g.Version;
        GoalRegion = goalRegion;
        int n = g.RegionCount;
        Dist = svc?.RentFieldArray(n) ?? new int[n];
        Next = svc?.RentFieldArray(n) ?? new int[n];
        Array.Fill(Dist, int.MaxValue);
        Array.Fill(Next, -1);
        // Per-thread reusable heap (fields are built inside parallel solves): keys are unique, so the order does not
        // depend on the heap's history; saves the queue and its growing arrays for every new field.
        var heap = t_heap ??= new PriorityQueue<int, long>();
        heap.Clear();
        Dist[goalRegion] = 0;
        heap.Enqueue(goalRegion, goalRegion);
        while (heap.TryDequeue(out int r, out long key))
        {
            int d = (int)(key >> 24);
            if (d != Dist[r]) continue;
            for (int e = g.EdgeStart[r]; e < g.EdgeStart[r + 1]; e++)
            {
                int t = g.EdgeTo[e];
                int nd = d + g.EdgeCost[e];
                if (nd >= Dist[t]) continue;
                Dist[t] = nd;
                Next[t] = r;
                heap.Enqueue(t, ((long)nd << 24) | (uint)t);
            }
        }
    }
}

using System.Numerics;
using System.Runtime.CompilerServices;
using Rts.Sim.Systems;

namespace Rts.Sim.Pathfinding;

/// <summary>Output of one path solve (reused buffer).</summary>
public sealed class PathResult
{
    public Vector2[] Points = new Vector2[16];
    public int Count;
    /// <summary>Path covers only the first window of a long route (ends at an intermediate region).</summary>
    public bool Partial;
    /// <summary>Goal unreachable: the path ends at the nearest reachable cell.</summary>
    public bool ToNearest;
    /// <summary>No path at all (unit boxed in): steer directly.</summary>
    public bool Failed;
    public int NavVersion;
    /// <summary>A* nodes expanded (stats).</summary>
    public int Expanded;

    public void Reset()
    {
        Count = 0;
        Partial = ToNearest = Failed = false;
        Expanded = 0;
    }

    public void Add(Vector2 p)
    {
        if (Count == Points.Length) Array.Resize(ref Points, Count * 2);
        Points[Count++] = p;
    }
}

/// <summary>
/// Fine-level solver (one instance per worker thread, see <see cref="ForThread"/>). Each solve:
/// 1. straight line if the goal is in line of sight;
/// 2. goal replaced by the nearest reachable cell if it is blocked or in another connected component;
/// 3. the region route comes from the cached <see cref="GoalField"/> (shared by every unit heading to that goal
///    region); its first <see cref="MaxRouteRegions"/> regions, dilated by one sector, form the corridor;
/// 4. octile A* restricted to the corridor (8-neighbour, no corner cutting, integer costs 10/14, deterministic
///    tie-breaking), with corridor-local node indices so scratch memory stays small;
/// 5. string pulling: turning points only, then greedy line-of-sight skipping.
/// Results never depend on which thread ran the solve.
/// </summary>
internal sealed class PathSolver
{
    public const int MaxRouteRegions = 40;
    private const int MaxSlots = 512;
    private const int S = NavGraph.S;
    private const int SC = S * S;

    [ThreadStatic, Persistence.NoSave] private static PathSolver? t_solver;
    public static PathSolver ForThread() => t_solver ??= new PathSolver();

    private int[] _sectorSlot = [];
    private int[] _sectorStamp = [];
    private int _corridorStamp;
    private readonly int[] _slotSector = new int[MaxSlots];
    private int _slotCount;

    private readonly int[] _g = new int[MaxSlots * SC];
    private readonly int[] _parent = new int[MaxSlots * SC];
    private readonly int[] _openStamp = new int[MaxSlots * SC];
    private readonly int[] _closedStamp = new int[MaxSlots * SC];
    private int _searchStamp;
    private ulong[] _heap = new ulong[4096];
    private int _heapCount;

    private readonly List<int> _cells = new(512);
    private readonly List<Vector2> _cand = new(64);

    public void Solve(PathService svc, NavGraph graph, Vector2 start, Vector2 goal, PathResult res)
    {
        res.Reset();
        var grid = graph.Grid;
        int req = graph.Req;
        int w = grid.Width;
        res.NavVersion = grid.Version;

        int sc = grid.CellOf(start);
        bool escaped = false;
        if (!grid.Passable(sc, req))
        {
            if (!grid.FindNearestPassable(sc % w, sc / w, req, 12, out int ex, out int ez)) { res.Failed = true; return; }
            sc = ez * w + ex;
            escaped = true;
        }
        goal = new Vector2(Math.Clamp(goal.X, 0.5f, grid.Width - 0.5f), Math.Clamp(goal.Y, 0.5f, grid.Height - 0.5f));
        int gc = grid.CellOf(goal);
        bool goalOk = grid.Passable(gc, req);
        if (goalOk && !escaped && grid.LineOfSight(start, goal, req)) { res.Add(goal); return; }

        int sr = graph.RegionOfCell(sc);
        int comp = graph.RegComp[sr];
        int tc;
        Vector2 endPos;
        if (goalOk && graph.ComponentOfCell(gc) == comp)
        {
            tc = gc;
            endPos = goal;
        }
        else
        {
            tc = graph.NearestCellInComponent(goal, comp);
            if (tc < 0) { res.Failed = true; return; }
            res.ToNearest = true;
            endPos = grid.CenterOf(tc);
            if (tc == sc || (!escaped && grid.LineOfSight(start, endPos, req))) { res.Add(endPos); return; }
        }

        int gr = graph.RegionOfCell(tc);
        var field = svc.GetField(graph, gr);

        // Corridor: route regions (from the shared goal field) dilated by one sector.
        if (_sectorStamp.Length < graph.SectorCount)
        {
            _sectorStamp = new int[graph.SectorCount];
            _sectorSlot = new int[graph.SectorCount];
            _corridorStamp = 0;
        }
        _corridorStamp++;
        _slotCount = 0;
        int r = sr, steps = 0, windowEnd = -1, prev = -1;
        while (true)
        {
            if (!AddSectorDilated(graph, graph.RegSector[r])) { windowEnd = prev; break; }
            if (r == gr) break;
            if (++steps >= MaxRouteRegions) { windowEnd = r; break; }
            int next = field.Next[r];
            if (next < 0) break; // cannot happen (same component); A* will still search the corridor
            prev = r;
            r = next;
        }
        if (windowEnd >= 0 && windowEnd != gr)
        {
            tc = graph.RegRep[windowEnd];
            endPos = grid.CenterOf(tc);
            res.Partial = true;
            res.ToNearest = false;
        }

        if (!AStar(graph, sc, tc, res))
        {
            res.Add(endPos);
            return;
        }
        Smooth(grid, req, start, escaped, endPos, res);
    }

    /// <summary>
    /// Reuses a leader's path for a follower with a nearby start and goal (same layer/clearance, same start and
    /// goal blocks): enter the leader path at the furthest of its first waypoints in line of sight (or through the
    /// leader's start point), leave it at the first waypoint (or the leader's own goal) from which the follower's
    /// goal is in line of sight. Every spliced segment is LOS-validated, so the result is a valid path.
    /// Returns false if no valid splice exists (the unit is then solved individually). Thread-safe (read-only).
    /// </summary>
    public static bool Splice(NavGraph graph, PathResult leader, Vector2 leaderStart, Vector2 start, Vector2 goal, PathResult res)
    {
        res.Reset();
        var grid = graph.Grid;
        int req = graph.Req;
        res.NavVersion = grid.Version;
        if (leader.Failed || leader.ToNearest || leader.Count == 0) return false;
        goal = new Vector2(Math.Clamp(goal.X, 0.5f, grid.Width - 0.5f), Math.Clamp(goal.Y, 0.5f, grid.Height - 0.5f));
        if (!grid.CanStand(start)) return false; // needs an escape: solve individually
        bool goalOk = grid.Passable(grid.CellOf(goal), req);
        if (!leader.Partial && goalOk && grid.LineOfSight(start, goal, req))
        {
            res.Add(goal);
            return true;
        }
        var p = leader.Points;
        int n = leader.Count;
        int entry = -1;
        bool viaLeaderStart = false;
        for (int k = Math.Min(2, n - 1); k >= 0; k--)
            if (grid.LineOfSight(start, p[k], req)) { entry = k; break; }
        if (entry < 0)
        {
            if (!grid.LineOfSight(start, leaderStart, req) || !grid.LineOfSight(leaderStart, p[0], req)) return false;
            entry = 0;
            viaLeaderStart = true;
        }
        if (leader.Partial)
        {
            // The window end is shared: follow the leader to it, the continuation is requested near the end.
            if (viaLeaderStart) res.Add(leaderStart);
            for (int k = entry; k < n; k++) res.Add(p[k]);
            res.Partial = true;
            return true;
        }
        if (!goalOk) return false;
        for (int m = entry; m < n; m++)
        {
            if (!grid.LineOfSight(p[m], goal, req)) continue;
            if (viaLeaderStart) res.Add(leaderStart);
            for (int k = entry; k <= m; k++) res.Add(p[k]);
            res.Add(goal);
            return true;
        }
        return false;
    }

    private bool AddSectorDilated(NavGraph g, int sector)
    {
        if (_slotCount + 9 > MaxSlots) return false;
        int sx = sector % g.SX, sz = sector / g.SX;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = sx + dx, nz = sz + dz;
            if ((uint)nx >= (uint)g.SX || (uint)nz >= (uint)g.SY) continue;
            int s = nz * g.SX + nx;
            if (_sectorStamp[s] == _corridorStamp) continue;
            _sectorStamp[s] = _corridorStamp;
            _sectorSlot[s] = _slotCount;
            _slotSector[_slotCount++] = s;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int LocalOf(NavGraph g, int cx, int cz)
    {
        int s = (cx / S) + (cz / S) * g.SX;
        if (_sectorStamp[s] != _corridorStamp) return -1;
        return _sectorSlot[s] * SC + (cz % S) * S + (cx % S);
    }

    private static int Octile(int dx, int dz)
    {
        dx = Math.Abs(dx); dz = Math.Abs(dz);
        return dx > dz ? 10 * dx + 4 * dz : 10 * dz + 4 * dx;
    }

    /// <summary>Heuristic weight in 1/4 steps (weighted A*: bounded suboptimality, far fewer expansions around
    /// obstacles; the string pulling pass removes most of the extra length).</summary>
    public const int HeuristicWeightQuarters = 6;

    private static int Heur(int dx, int dz) => Octile(dx, dz) * HeuristicWeightQuarters >> 2;

    private static readonly int[] DX =[1, -1, 0, 0, 1, 1, -1, -1];
    private static readonly int[] DZ = [0, 0, 1, -1, 1, -1, 1, -1];

    // Hot loops: skip tiered JIT so the first (burst) ticks after a big order already run optimized code.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool AStar(NavGraph g, int startCell, int targetCell, PathResult res)
    {
        var grid = g.Grid;
        int w = grid.Width, h = grid.Height, req = g.Req;
        var clear = grid.Clear;
        int sx = startCell % w, sz = startCell / w;
        int tx = targetCell % w, tz = targetCell / w;
        int sLoc = LocalOf(g, sx, sz), tLoc = LocalOf(g, tx, tz);
        if (sLoc < 0 || tLoc < 0) return false;

        _searchStamp++;
        if (_searchStamp == int.MaxValue)
        {
            Array.Clear(_openStamp); Array.Clear(_closedStamp);
            _searchStamp = 1;
        }
        int stamp = _searchStamp;
        _heapCount = 0;
        _g[sLoc] = 0;
        _parent[sLoc] = -1;
        _openStamp[sLoc] = stamp;
        int h0 = Heur(tx - sx, tz - sz);
        Push(((ulong)h0 << 40) | ((ulong)h0 << 20) | (uint)sLoc);
        int expanded = 0;
        bool found = false;
        while (_heapCount > 0)
        {
            ulong key = Pop();
            int n = (int)(key & 0xFFFFF);
            if (_closedStamp[n] == stamp) continue;
            _closedStamp[n] = stamp;
            if (n == tLoc) { found = true; break; }
            expanded++;
            int slot = n / SC, within = n % SC;
            int sector = _slotSector[slot];
            int cx = (sector % g.SX) * S + within % S;
            int cz = (sector / g.SX) * S + within / S;
            int gn = _g[n];
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + DX[d], nz = cz + DZ[d];
                if ((uint)nx >= (uint)w || (uint)nz >= (uint)h) continue;
                if (clear[nz * w + nx] < req) continue;
                int cost = 10;
                if (d >= 4)
                {
                    // No corner cutting: both orthogonal neighbours must be passable.
                    if (clear[cz * w + nx] < req || clear[nz * w + cx] < req) continue;
                    cost = 14;
                }
                int m = LocalOf(g, nx, nz);
                if (m < 0 || _closedStamp[m] == stamp) continue;
                int ng = gn + cost;
                if (_openStamp[m] == stamp && ng >= _g[m]) continue;
                _openStamp[m] = stamp;
                _g[m] = ng;
                _parent[m] = n;
                int hh = Heur(tx - nx, tz - nz);
                Push(((ulong)(ng + hh) << 40) | ((ulong)hh << 20) | (uint)m);
            }
        }
        res.Expanded = expanded;
        if (!found) return false;

        _cells.Clear();
        for (int n = tLoc; n >= 0; n = _parent[n])
        {
            int slot = n / SC, within = n % SC;
            int sector = _slotSector[slot];
            _cells.Add(((sector / g.SX) * S + within / S) * w + (sector % g.SX) * S + within % S);
        }
        _cells.Reverse();
        return true;
    }

    private void Smooth(NavGrid grid, int req, Vector2 start, bool escaped, Vector2 endPos, PathResult res)
    {
        int w = grid.Width;
        _cand.Clear();
        int n = _cells.Count;
        if (escaped && n > 1) _cand.Add(grid.CenterOf(_cells[0]));
        for (int i = 1; i < n - 1; i++)
        {
            int a = _cells[i - 1], b = _cells[i], c = _cells[i + 1];
            int d1x = b % w - a % w, d1z = b / w - a / w;
            int d2x = c % w - b % w, d2z = c / w - b / w;
            if (d1x != d2x || d1z != d2z) _cand.Add(grid.CenterOf(b));
        }
        _cand.Add(endPos);

        var anchor = start;
        int k = 0;
        while (k < _cand.Count)
        {
            int j = k;
            while (j + 1 < _cand.Count && grid.LineOfSight(anchor, _cand[j + 1], req)) j++;
            res.Add(_cand[j]);
            anchor = _cand[j];
            k = j + 1;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void Push(ulong key)
    {
        if (_heapCount == _heap.Length) Array.Resize(ref _heap, _heap.Length * 2);
        int i = _heapCount++;
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            if (_heap[p] <= key) break;
            _heap[i] = _heap[p];
            i = p;
        }
        _heap[i] = key;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private ulong Pop()
    {
        ulong top = _heap[0];
        ulong last = _heap[--_heapCount];
        int i = 0;
        int n = _heapCount;
        while (true)
        {
            int l = 2 * i + 1;
            if (l >= n) break;
            int r = l + 1;
            int m = r < n && _heap[r] < _heap[l] ? r : l;
            if (_heap[m] >= last) break;
            _heap[i] = _heap[m];
            i = m;
        }
        if (n > 0) _heap[i] = last;
        return top;
    }
}

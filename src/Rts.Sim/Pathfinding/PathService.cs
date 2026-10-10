using System.Collections.Concurrent;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Pathfinding;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Pathfinding service (one per world).
///
/// <para><b>Algorithm (why hierarchical, not per-goal flow fields):</b> maps go up to 1024x1024 cells and most
/// orders are formation moves where every unit has its own slot, so a full-map flow field per goal would cost
/// ~1M cells per distinct goal. Instead:
/// <list type="number">
/// <item><see cref="NavGrid"/> per surface layer (Land, Hover, Amphibious, Water/Sub; Air needs none) stores a
///   clearance value per cell (terrain + structures), so unit size is respected by clearance class.</item>
/// <item><see cref="NavGraph"/> per (layer, clearance): HPA*-style 16x16 sectors split into connected regions,
///   linked by portals; connected components answer reachability in O(1). Structures only relabel the sectors
///   they touch (OccupancyGrid.Version is watched and diffed).</item>
/// <item><see cref="GoalField"/>: Dijkstra over the region graph from the goal region, cached and shared by all
///   units of the same layer/clearance heading to the same goal region ("region-level flow field").</item>
/// <item>Per unit: A* on cells restricted to a corridor (route regions dilated by one sector), then string pulling
///   with line-of-sight. Long routes are solved in windows (<see cref="PathSolver.MaxRouteRegions"/> regions);
///   PathSystem extends them as the unit approaches the window end. Unreachable goal -> nearest reachable cell.</item>
/// </list></para>
/// Threading: everything that mutates (nav updates, path assignment) runs in sequential phases; solves run in
/// parallel on read-only data with thread-local scratch; <see cref="CurrentWaypoint"/> is allocation-free and
/// only writes the caller's MoveState.
/// </summary>
public sealed class PathService
{
    private readonly SimWorld _w;
    private readonly NavGrid?[] _grids = new NavGrid?[7];
    private readonly NavGraph?[] _graphs = new NavGraph?[7 * (NavGrid.MaxClearance + 1)];
    private int[]? _occSnapshot;
    private int _occVersion;
    private readonly ConcurrentDictionary<long, Lazy<GoalField>> _fields = new();
    private int _nextGroup;

    // Per-unit path storage (indexed by unit slot; PathId == unit index when the unit has a path).
    private readonly Vector2[][] _pts;
    private readonly float[][] _suffix;
    private readonly int[] _count;

    // Movement scratch (owned by MovementSystem; snapshot of last tick's motion so parallel steps never read
    // state another thread is writing).
    internal readonly SepInfo[] Sep;
    internal readonly Dictionary<int, GroupPace> Groups = new();
    /// <summary>Largest radius of any mobile blueprint (bounds separation queries).</summary>
    internal readonly float MaxMobileRadius;

    /// <summary>Compact per-unit snapshot read by separation (cache friendly, never written during the parallel step).</summary>
    internal struct SepInfo
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public float Radius;
        public int Team;
        /// <summary>0 = ignore (structure / under construction), 1 = surface unit, 2 = air unit.</summary>
        public byte Kind;
        /// <summary>1 if the unit has a movement goal (moving units push idle friends aside).</summary>
        public byte Moving;
    }

    internal struct GroupPace
    {
        public float Time;      // max over members of remaining distance / max speed
        public float MinSpeed;  // slowest member's max speed
    }

    /// <summary>Max unique path solves per tick (deterministic cap; remaining requests wait, oldest first).</summary>
    public int MaxSolvesPerTick = 128;
    /// <summary>Max cached goal fields.</summary>
    public int MaxGoalFields = 1024;

    public readonly PathStats Stats = new();

    public PathService(SimWorld w)
    {
        _w = w;
        int cap = w.Units.Capacity;
        _pts = new Vector2[cap][];
        _suffix = new float[cap][];
        _count = new int[cap];
        Sep = new SepInfo[cap];
        foreach (var bp in w.Blueprints.All)
            if (bp.IsMobile) MaxMobileRadius = MathF.Max(MaxMobileRadius, bp.Radius);
    }

    // ------------------------------------------------------------------ nav data

    private static int GridIndex(MotionLayer layer) => layer switch
    {
        MotionLayer.Land => 1,
        MotionLayer.Hover => 2,
        MotionLayer.Amphibious => 3,
        MotionLayer.Water or MotionLayer.Sub => 4,
        _ => -1,
    };

    /// <summary>Builds the per-layer clearance grids on first use. SEQUENTIAL.</summary>
    public void EnsureGrids()
    {
        if (_occSnapshot != null) return;
        var t = _w.Terrain;
        _grids[1] = new NavGrid(t, _w.Occupancy, MotionLayer.Land);
        _grids[2] = new NavGrid(t, _w.Occupancy, MotionLayer.Hover);
        _grids[3] = new NavGrid(t, _w.Occupancy, MotionLayer.Amphibious);
        _grids[4] = new NavGrid(t, _w.Occupancy, MotionLayer.Water);
        _occSnapshot = (int[])_w.Occupancy.Cells.Clone();
        _occVersion = _w.Occupancy.Version;
    }

    /// <summary>Clearance grid of a surface layer (null for Air/None). Grids exist after the first PathSystem run.</summary>
    public NavGrid? GridFor(MotionLayer layer)
    {
        int gi = GridIndex(layer);
        return gi < 0 ? null : _grids[gi];
    }

    internal static int GraphIndex(MotionLayer layer, int req) => GridIndex(layer) * (NavGrid.MaxClearance + 1) + req;

    /// <summary>Region graph for a layer and clearance class, built on first use. SEQUENTIAL (may build).</summary>
    public NavGraph? GraphFor(MotionLayer layer, int req)
    {
        int gi = GridIndex(layer);
        if (gi < 0) return null;
        EnsureGrids();
        int k = GraphIndex(layer, req);
        return _graphs[k] ??= new NavGraph(_grids[gi]!, req);
    }

    internal NavGraph GraphByIndex(int k) => _graphs[k]!;

    internal void EnsureGraph(int k)
    {
        if (_graphs[k] != null) return;
        int gi = k / (NavGrid.MaxClearance + 1), req = k % (NavGrid.MaxClearance + 1);
        _graphs[k] = new NavGraph(_grids[gi]!, req);
    }

    /// <summary>Clearance class of a unit blueprint (1..NavGrid.MaxClearance).</summary>
    public static int ClearanceOf(UnitBlueprint bp) => NavGrid.ClearanceFor(bp.Radius);

    /// <summary>True if a unit of this layer may stand at p (terrain + structures). Air: always. Thread-safe.</summary>
    public bool CanStand(MotionLayer layer, Vector2 p)
    {
        if (layer == MotionLayer.Air) return true;
        var g = GridFor(layer);
        return g != null && g.CanStand(p);
    }

    /// <summary>True if a unit of this layer/radius can walk from 'from' to 'to'. SEQUENTIAL (may build a graph).</summary>
    public bool CanReach(MotionLayer layer, float radius, Vector2 from, Vector2 to)
    {
        if (layer == MotionLayer.Air) return true;
        var g = GraphFor(layer, NavGrid.ClearanceFor(radius));
        if (g == null) return false;
        int a = g.ComponentOfCell(g.Grid.CellOf(from)), b = g.ComponentOfCell(g.Grid.CellOf(to));
        return a >= 0 && a == b;
    }

    /// <summary>
    /// Applies structure changes (OccupancyGrid.Version) to the grids and graphs: diff against the last snapshot,
    /// recompute clearance around changed cells, relabel affected sectors. SEQUENTIAL.
    /// </summary>
    internal void UpdateNav()
    {
        EnsureGrids();
        var occ = _w.Occupancy;
        if (occ.Version == _occVersion) return;
        _occVersion = occ.Version;
        int w = occ.Width, h = occ.Height;
        var snap = _occSnapshot!;
        const int B = NavGraph.S;
        int bx = (w + B - 1) / B;
        var boxes = new Dictionary<int, (int x0, int z0, int x1, int z1)>();
        var order = new List<int>();
        for (int z = 0; z < h; z++)
        {
            var cur = occ.Cells.AsSpan(z * w, w);
            var old = snap.AsSpan(z * w, w);
            if (cur.SequenceEqual(old)) continue;
            for (int x = 0; x < w; x++)
            {
                if (cur[x] == old[x]) continue;
                int b = (z / B) * bx + x / B;
                if (boxes.TryGetValue(b, out var r)) boxes[b] = (Math.Min(r.x0, x), Math.Min(r.z0, z), Math.Max(r.x1, x), Math.Max(r.z1, z));
                else { boxes[b] = (x, z, x, z); order.Add(b); }
            }
            cur.CopyTo(old);
        }
        order.Sort();
        foreach (int b in order)
        {
            var r = boxes[b];
            for (int gi = 1; gi <= 4; gi++)
            {
                var grid = _grids[gi]!;
                var ch = grid.RecomputeAround(r.x0, r.z0, r.x1, r.z1);
                if (ch.x0 > ch.x1) continue;
                for (int req = 1; req <= NavGrid.MaxClearance; req++)
                    _graphs[gi * (NavGrid.MaxClearance + 1) + req]?.MarkDirty(ch.x0, ch.z0, ch.x1, ch.z1);
            }
        }
        foreach (var g in _graphs) g?.CommitDirty();
        // Drop goal fields of graphs that changed (results stay deterministic: fields are pure functions of the graph).
        foreach (var kv in _fields)
        {
            var f = kv.Value.Value;
            if (f.GraphVersion != _graphs[(int)(kv.Key >> 32)]!.Version) _fields.TryRemove(kv.Key, out _);
        }
    }

    internal GoalField GetField(NavGraph g, int goalRegion)
    {
        long key = ((long)GraphIndex(g.Grid.Layer, g.Req) << 32) | (uint)goalRegion;
        var lazy = _fields.GetOrAdd(key, static (_, arg) => new Lazy<GoalField>(() => new GoalField(arg.g, arg.r)), (g, r: goalRegion));
        var f = lazy.Value;
        f.LastUsedTick = _w.Tick;
        return f;
    }

    /// <summary>Evicts least recently used goal fields beyond MaxGoalFields. SEQUENTIAL. Cache-only: no effect on results.</summary>
    internal void TrimFields()
    {
        if (_fields.Count <= MaxGoalFields) return;
        var all = new List<(int tick, long key)>();
        foreach (var kv in _fields) all.Add((kv.Value.Value.LastUsedTick, kv.Key));
        all.Sort();
        for (int k = 0; k < all.Count - MaxGoalFields * 3 / 4; k++) _fields.TryRemove(all[k].key, out _);
    }

    public int CachedGoalFields => _fields.Count;

    // ------------------------------------------------------------------ per-unit paths

    /// <summary>Goal drift tolerated before a path is recomputed (lets units follow slowly moving goals).</summary>
    public static float GoalTolerance(Vector2 pos, Vector2 goal) => MathF.Max(1.5f, 0.2f * Vector2.Distance(pos, goal));

    /// <summary>True if the unit's stored path was computed for (approximately) its current goal.</summary>
    public bool HasValidPath(in MoveState mv, Vector2 pos)
    {
        if (mv.PathId < 0) return false;
        float tol = GoalTolerance(pos, mv.Goal);
        return Vector2.DistanceSquared(mv.Goal, mv.PathGoal) <= tol * tol;
    }

    /// <summary>The unit's current path points (empty if none). Valid until the next PathSystem run.</summary>
    public ReadOnlySpan<Vector2> PathOf(int unit) =>
        _pts[unit] == null || _w.Units.Move[unit].PathId < 0 ? default : _pts[unit].AsSpan(0, _count[unit]);

    /// <summary>
    /// Next point the unit should steer towards; advances PathIndex past reached waypoints (corner lookahead
    /// proportional to speed) and periodically skips a waypoint when the following one is in line of sight.
    /// Called from parallel MovementSystem: thread-safe, allocation-free, only writes the given MoveState.
    /// </summary>
    public Vector2 CurrentWaypoint(int unit, ref MoveState mv, Vector2 pos)
    {
        if (!HasValidPath(mv, pos)) return mv.Goal;
        var pts = _pts[unit];
        int n = _count[unit];
        int k = Math.Min(mv.PathIndex, n - 1);
        float adv = MathF.Max(1.0f, mv.Speed * 0.6f);
        while (k < n - 1 && Vector2.DistanceSquared(pos, pts[k]) <= adv * adv) k++;
        if (k < n - 1 && ((_w.Tick + unit) & 3) == 0)
        {
            var bp = _w.BpOf(unit);
            var grid = GridFor(bp.Movement.Layer);
            if (grid != null && grid.LineOfSight(pos, pts[k + 1], ClearanceOf(bp))) k++;
        }
        mv.PathIndex = k;
        if (k == n - 1 && !mv.PathPartial && !mv.PathToNearest) return mv.Goal;
        return pts[k];
    }

    /// <summary>Remaining travel distance along the path (straight distance to the goal without a path).</summary>
    public float RemainingDistance(int unit, in MoveState mv, Vector2 pos)
    {
        if (!HasValidPath(mv, pos)) return Vector2.Distance(pos, mv.Goal);
        int n = _count[unit];
        int k = Math.Min(mv.PathIndex, n - 1);
        float d = Vector2.Distance(pos, _pts[unit][k]) + _suffix[unit][k];
        if (mv.PathPartial) d += Vector2.Distance(_pts[unit][n - 1], mv.Goal);
        return d;
    }

    /// <summary>Where the unit will stop: the goal, or the nearest reachable point when the goal is unreachable.</summary>
    public Vector2 PathEnd(int unit, in MoveState mv, Vector2 pos) =>
        mv.PathToNearest && HasValidPath(mv, pos) ? _pts[unit][_count[unit] - 1] : mv.Goal;

    internal void AssignPath(int unit, ref MoveState mv, PathResult res, Vector2 goal)
    {
        int n = Math.Max(1, res.Count);
        var pts = _pts[unit];
        if (pts == null || pts.Length < n)
        {
            int cap = Math.Max(8, n);
            _pts[unit] = pts = new Vector2[cap];
            _suffix[unit] = new float[cap];
        }
        if (res.Count == 0) pts[0] = goal; // failed (boxed in): steer straight
        else Array.Copy(res.Points, pts, n);
        var suf = _suffix[unit];
        suf[n - 1] = 0;
        for (int k = n - 2; k >= 0; k--) suf[k] = suf[k + 1] + Vector2.Distance(pts[k], pts[k + 1]);
        _count[unit] = n;
        if (Vector2.DistanceSquared(mv.PathGoal, goal) > 1f) mv.StuckRepaths = 0;
        mv.PathId = unit;
        mv.PathIndex = 0;
        mv.PathGoal = goal;
        mv.PathPartial = res.Partial;
        mv.PathToNearest = res.ToNearest;
        mv.PathNavVersion = res.NavVersion;
        mv.PathRequestTick = 0;
        mv.RepathRequested = false;
        mv.PathNoShare = false;
    }

    /// <summary>
    /// Re-checks the remaining path after structures changed: line of sight from the unit to its waypoint and
    /// along every remaining segment. Thread-safe (read-only).
    /// </summary>
    internal bool RemainingPathClear(int unit, in MoveState mv, Vector2 pos, NavGrid grid, int req)
    {
        var pts = _pts[unit];
        int n = _count[unit];
        int k = Math.Min(mv.PathIndex, n - 1);
        if (!grid.LineOfSight(pos, pts[k], req)) return false;
        for (int j = k; j < n - 1; j++)
            if (!grid.LineOfSight(pts[j], pts[j + 1], req)) return false;
        return true;
    }

    internal int PathCount(int unit) => _count[unit];
    internal Vector2 PathPoint(int unit, int k) => _pts[unit][k];

    /// <summary>New formation group id (CommandSystem, sequential).</summary>
    public int NewGroupId() => ++_nextGroup;
}

/// <summary>Timing/throughput counters (informational only, never feed back into the simulation).</summary>
public sealed class PathStats
{
    public double LastPathMs;
    public double LastMoveMs;
    public double TotalPathMs;
    public double TotalMoveMs;
    public int LastRequests;
    public int LastSolves;
    public int LastSplices;
    public long TotalSolves;
    public long TotalSplices;
    public long TotalExpanded;
    public int Ticks;
}

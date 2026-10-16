using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Pathfinding;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Resolves path requests each tick:
/// 1. apply structure changes to the nav data (sequential);
/// 2. gather (parallel, per unit): units with a goal and no path, a goal that drifted beyond tolerance, a repath
///    request (blocked/stuck), a path invalidated by structures, or nearing the end of a partial path;
/// 3. sort requests by (first-request tick, unit index): oldest first, deterministic;
/// 4. group: requests with the same graph (layer + clearance), start block (<see cref="StartBlock"/> cells) and goal
///    block (<see cref="GoalBlock"/> cells) share one solve: the first is the <b>leader</b> (full A* solve), the
///    others are <b>followers</b> that splice the leader's path with line-of-sight checks (a formation of 500 units
///    costs a handful of A* solves). Units whose splice failed are solved individually (deduped by exact
///    start/goal cell). At most <see cref="PathService.MaxSolvesPerTick"/> A* solves per tick (deterministic cap:
///    the rest wait, steering straight at their goal meanwhile);
/// 5. solve leaders in parallel, then splice followers in parallel (thread-local scratch, read-only nav data);
/// 6. assign results to units sequentially in request order.
/// </summary>
public static class PathSystem
{
    /// <summary>Requests whose start lies in the same block of this many cells may share a leader path.</summary>
    public const int StartBlock = 32;
    /// <summary>Requests whose goal lies in the same block of this many cells may share a leader path.</summary>
    public const int GoalBlock = 48;
    /// <summary>Max followers spliced per tick.</summary>
    public const int MaxSplicesPerTick = 8192;
    /// <summary>Distance to the end of a partial path at which its continuation is requested.</summary>
    public const float PartialRefreshDistance = 14f;

    private struct Request
    {
        public int Unit;
        public int RequestTick;
        public int Graph;
        public int StartCell;
        public int GoalCell;
        public bool NoShare;
        public Vector2 Start;
        public Vector2 Goal;
    }

    private readonly record struct ExactKey(int Graph, int StartCell, int GoalCell);
    private readonly record struct GroupKey(int Graph, int StartBlock, int GoalBlock);

    private sealed class State
    {
        public readonly ChunkedBuffer<Request> Buffer = new();
        public readonly List<Request> Requests = new();
        public readonly Dictionary<ExactKey, int> SolveOfExact = new();
        public readonly Dictionary<GroupKey, int> SolveOfGroup = new();
        /// <summary>Request index of each A* solve.</summary>
        public readonly List<int> SolveRequest = new();
        /// <summary>(request index, leader solve index) of each follower.</summary>
        public readonly List<(int req, int solve)> Followers = new();
        /// <summary>Per request: solve index (&gt;= 0), follower index encoded as -(2 + f), or -1 (waits).</summary>
        public int[] AssignOf = [];
        public PathResult[] Results = [];
        public PathResult[] FollowerResults = [];
        public bool[] FollowerOk = [];
        public readonly bool[] GraphNeeded = new bool[7 * (NavGrid.MaxClearance + 1)];
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SimWorld, State> States = new();

    private const int ChunkSize = 256;

    public static void Run(SimWorld w)
    {
        long sw = Stopwatch.GetTimestamp(); // (no Stopwatch object per tick)
        var svc = w.Paths;
        var st = States.GetValue(w, static _ => new State());
        svc.UpdateNav();
        if ((w.Tick & 63) == 0) svc.TrimFields();

        // ---- gather (parallel; each unit writes only its own MoveState)
        var u = w.Units;
        int chunks = Par.ChunkCount(u.AliveCount, ChunkSize);
        st.Buffer.Reset(chunks);
        Par.For(u.AliveCount, ChunkSize, w, static (w, c, start, end) =>
        {
            var u = w.Units;
            var svc = w.Paths;
            var list = States.GetValue(w, static _ => new State()).Buffer[c];
            for (int k = start; k < end; k++)
            {
                int i = u.AliveList[k];
                var bp = w.BpOf(i);
                var layer = bp.Movement.Layer;
                if (bp.IsStructure || layer is MotionLayer.Air or MotionLayer.None || !u.IsComplete(i)) continue;
                ref var mv = ref u.Move[i];
                if (!mv.HasGoal) { mv.PathRequestTick = 0; continue; }
                var grid = svc.GridFor(layer)!;
                int req = PathService.ClearanceOf(bp);
                var pos = u.Pos[i];
                if (!NeedsPath(svc, i, ref mv, pos, grid, req)) { mv.PathRequestTick = 0; continue; }
                if (mv.PathRequestTick == 0) mv.PathRequestTick = w.Tick;
                list.Add(new Request
                {
                    Unit = i,
                    RequestTick = mv.PathRequestTick,
                    Graph = PathService.GraphIndex(layer, req),
                    StartCell = grid.CellOf(pos),
                    GoalCell = grid.CellOf(mv.Goal),
                    NoShare = mv.PathNoShare,
                    Start = pos,
                    Goal = mv.Goal,
                });
            }
        });
        var reqs = st.Requests;
        reqs.Clear();
        st.Buffer.DrainTo(reqs);
        svc.Stats.LastRequests = reqs.Count;
        svc.Stats.LastSolves = 0;
        if (reqs.Count == 0) { Finish(svc, sw); return; }

        reqs.Sort(static (a, b) => a.RequestTick != b.RequestTick ? a.RequestTick.CompareTo(b.RequestTick) : a.Unit.CompareTo(b.Unit));

        // ---- leaders / followers / exact dedupe, deterministic budget
        st.SolveOfExact.Clear();
        st.SolveOfGroup.Clear();
        st.SolveRequest.Clear();
        st.Followers.Clear();
        Array.Clear(st.GraphNeeded);
        if (st.AssignOf.Length < reqs.Count) st.AssignOf = new int[Math.Max(reqs.Count, st.AssignOf.Length * 2)];
        int cap = Math.Max(1, svc.MaxSolvesPerTick);
        int width = w.Terrain.Width;
        for (int r = 0; r < reqs.Count; r++)
        {
            var q = reqs[r];
            st.AssignOf[r] = -1;
            if (q.NoShare)
            {
                var key = new ExactKey(q.Graph, q.StartCell, q.GoalCell);
                if (st.SolveOfExact.TryGetValue(key, out int s)) { st.AssignOf[r] = s; continue; }
                if (st.SolveRequest.Count >= cap) continue;
                st.SolveOfExact[key] = st.AssignOf[r] = AddSolve(st, r, q.Graph);
            }
            else
            {
                var key = new GroupKey(q.Graph, BlockOf(q.StartCell, width, StartBlock), BlockOf(q.GoalCell, width, GoalBlock));
                if (st.SolveOfGroup.TryGetValue(key, out int s))
                {
                    if (st.Followers.Count >= MaxSplicesPerTick) continue;
                    st.AssignOf[r] = -(2 + st.Followers.Count);
                    st.Followers.Add((r, s));
                    continue;
                }
                if (st.SolveRequest.Count >= cap) continue;
                st.SolveOfGroup[key] = st.AssignOf[r] = AddSolve(st, r, q.Graph);
            }
        }
        svc.EnsureGraphs(st.GraphNeeded);

        // ---- A* solves (parallel)
        int solves = st.SolveRequest.Count;
        Grow(ref st.Results, solves);
        var results = st.Results;
        Par.ForEach(solves, w, static (w, s) =>
        {
            var st = States.GetValue(w, static _ => new State());
            var svc = w.Paths;
            var q = st.Requests[st.SolveRequest[s]];
            PathSolver.ForThread().Solve(svc, svc.GraphByIndex(q.Graph), q.Start, q.Goal, st.Results[s]);
        });

        // ---- follower splices (parallel)
        int followers = st.Followers.Count;
        Grow(ref st.FollowerResults, followers);
        if (st.FollowerOk.Length < followers) st.FollowerOk = new bool[Math.Max(followers, st.FollowerOk.Length * 2)];
        var fres = st.FollowerResults;
        var fok = st.FollowerOk;
        Par.For(followers, 64, w, static (w, _, a, b) =>
        {
            var st = States.GetValue(w, static _ => new State());
            var svc = w.Paths;
            var reqs = st.Requests;
            for (int f = a; f < b; f++)
            {
                var (r, s) = st.Followers[f];
                var q = reqs[r];
                var leaderStart = reqs[st.SolveRequest[s]].Start;
                st.FollowerOk[f] = PathSolver.Splice(svc.GraphByIndex(q.Graph), st.Results[s], leaderStart, q.Start, q.Goal, st.FollowerResults[f]);
            }
        });

        // ---- assign (sequential, request order)
        for (int r = 0; r < reqs.Count; r++)
        {
            int a = st.AssignOf[r];
            if (a == -1) continue;
            var q = reqs[r];
            ref var mv = ref u.Move[q.Unit];
            if (a >= 0) svc.AssignPath(q.Unit, ref mv, results[a], q.Goal);
            else
            {
                int f = -a - 2;
                if (fok[f]) svc.AssignPath(q.Unit, ref mv, fres[f], q.Goal);
                else mv.PathNoShare = true; // stays pending: solved on its own next tick (it is among the oldest)
            }
        }
        long expanded = 0;
        for (int s = 0; s < solves; s++) expanded += results[s].Expanded;
        svc.Stats.LastSolves = solves;
        svc.Stats.LastSplices = followers;
        svc.Stats.TotalSolves += solves;
        svc.Stats.TotalSplices += followers;
        svc.Stats.TotalExpanded += expanded;
        Finish(svc, sw);
    }

    private static int AddSolve(State st, int r, int graph)
    {
        st.SolveRequest.Add(r);
        st.GraphNeeded[graph] = true;
        return st.SolveRequest.Count - 1;
    }

    private static int BlockOf(int cell, int width, int block) =>
        (cell / width / block) * 4096 + (cell % width) / block;

    private static void Grow(ref PathResult[] arr, int n)
    {
        if (arr.Length >= n) return;
        int old = arr.Length;
        Array.Resize(ref arr, Math.Max(n, old * 2));
        for (int k = old; k < arr.Length; k++) arr[k] = new PathResult();
    }

    private static void Finish(PathService svc, long sw)
    {
        svc.Stats.LastPathMs = Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
        svc.Stats.TotalPathMs += svc.Stats.LastPathMs;
    }

    private static bool NeedsPath(PathService svc, int i, ref MoveState mv, Vector2 pos, NavGrid grid, int req)
    {
        if (!svc.HasValidPath(mv, pos)) return true;
        if (mv.RepathRequested) return true;
        if (mv.PathNavVersion != grid.Version)
        {
            if (!svc.RemainingPathClear(i, mv, pos, grid, req)) return true;
            mv.PathNavVersion = grid.Version;
        }
        if (mv.PathPartial)
        {
            var end = svc.PathPoint(i, svc.PathCount(i) - 1);
            if (Vector2.DistanceSquared(pos, end) < PartialRefreshDistance * PartialRefreshDistance) return true;
        }
        return false;
    }
}

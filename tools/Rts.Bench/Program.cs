using System.Diagnostics;
using System.Numerics;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;

// Headless benchmark: N units per side attack-move across the map.
// Usage: Rts.Bench [unitsPerSide] [ticks] [threads (0 = default)] [mapSize = 512] [armies = 2]
//        (armies > 2: each army starts at its start position and attack-moves to the opposite one)
//        Rts.Bench replay <file> [threads] | savebench [minutes] [mapSize] [players] [preset] [seed] [extraUnits] [threads]  (ReplayBench.cs)
//        Rts.Bench parbench [threads] [calls]  (Par.For overhead per call, ParBench.cs)
//        Rts.Bench ai|aipassive [maxMinutes] [mapSize] [seed] [difficulties] [threads] [players] [preset] [factions]  (skirmish AI match, see AiMatch.cs)
if (int.TryParse(Environment.GetEnvironmentVariable("PAR_SPIN"), out int parSpin)) Par.WorkerSpinMicroseconds = parSpin; // pool worker spin (µs) experiments
if (Environment.GetEnvironmentVariable("PAR_TASKS") == "1") Par.UseTaskParallel = true; // A/B: the old Parallel.For path
if (args.Length > 0 && args[0] == "replay") { Environment.ExitCode = ReplayBench.Verify(args[1..]); return; }
if (args.Length > 0 && args[0] == "savebench") { ReplayBench.SaveBench(args[1..]); return; }
if (args.Length > 0 && args[0] == "parbench") { ParBench.Run(args[1..]); return; }
if (args.Length > 0 && args[0] is "ai" or "aipassive")
{
    AiMatch.Run(args[1..], passive: args[0] == "aipassive");
    return;
}
int perSide = args.Length > 0 ? int.Parse(args[0]) : 2000;
int ticks = args.Length > 1 ? int.Parse(args[1]) : 300;
if (args.Length > 2 && int.Parse(args[2]) > 0) Par.MaxThreads = int.Parse(args[2]);
int mapSize = args.Length > 3 ? int.Parse(args[3]) : 512;
int armies = args.Length > 4 ? int.Parse(args[4]) : 2;

var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
var setup = Stopwatch.StartNew();
var sim = GameSetup.CreateSkirmish(db, new SkirmishSettings
{
    MapSize = mapSize, Seed = 3, Players = armies, UnitCapacity = Math.Max(16384, perSide * armies + 1000), AiPlayers = new bool[armies],
});
var w = sim.World;
string[] kinds = ["uef_t1_tank", "uef_t1_artillery", "uef_t1_mobile_aa", "uef_t1_interceptor"];
var sides = Enumerable.Range(0, armies).Select(_ => new List<UnitHandle>()).ToArray();
// 80/432 on the default 512 map (same scenario as before), scaled for other sizes; more armies: their start positions.
var bases = armies == 2
    ? new[] { new Vector2(mapSize * 0.15625f, mapSize * 0.15625f), new Vector2(mapSize * 0.84375f, mapSize * 0.84375f) }
    : w.Terrain.StartPositions.ToArray();
for (int s = 0; s < armies; s++)
{
    int cols = (int)Math.Sqrt(perSide);
    // Blobs grow away from the map center.
    var dir = armies == 2 ? new Vector2(s == 0 ? 1 : -1) : new Vector2(bases[s].X < mapSize / 2 ? -1 : 1, bases[s].Y < mapSize / 2 ? -1 : 1);
    for (int k = 0; k < perSide; k++)
    {
        var off = new Vector2(k % cols, k / cols) * 1.6f * dir;
        var bp = db.IndexOf(kinds[k % kinds.Length]);
        var h = w.SpawnUnit(bp, s, bases[s] + off, 0);
        if (!h.IsNone) sides[s].Add(h);
    }
}
sim.Step(Enumerable.Range(0, armies)
    .Select(s => (Command)new IssueOrderCommand(s, sides[s].ToArray(), Order.AttackMoveTo(bases[armies == 2 ? 1 - s : (s + armies / 2) % armies]), false))
    .ToArray());
Console.WriteLine($"setup {setup.Elapsed.TotalMilliseconds:F0} ms (first tick incl. nav build {sim.LastTickMs:F1} ms: {StageStats.LastTick(sim)})");

var times = new List<double>();
var pathMs = new List<double>();
var moveMs = new List<double>();
var intelMs = new List<double>();
var snapMs = new List<double>();
var snap = new Rts.Sim.Snapshot.SimSnapshot();
var stats = w.Paths.Stats;
long solves0 = stats.TotalSolves, exp0 = stats.TotalExpanded;
var stages = new StageStats();
var total = Stopwatch.StartNew();
stages.Start(sim);
for (int t = 0; t < ticks; t++)
{
    sim.Step();
    stages.Add(sim);
    times.Add(sim.LastTickMs);
    pathMs.Add(stats.LastPathMs);
    moveMs.Add(stats.LastMoveMs);
    intelMs.Add(w.Intel.LastUpdateMs);
    var snapSw = Stopwatch.StartNew();
    sim.WriteSnapshot(snap, 0); // fog-of-war filtered snapshot for army 0 (as the client does)
    snapMs.Add(snapSw.Elapsed.TotalMilliseconds);
    if (Environment.GetEnvironmentVariable("BENCH_VERBOSE") == "1" && sim.LastTickMs > 8)
        Console.WriteLine($"  tick {w.Tick}: {sim.LastTickMs:F1} ms (path {stats.LastPathMs:F1}: {stats.LastRequests} req, {stats.LastSolves} solves, {stats.LastSplices} splices; move {stats.LastMoveMs:F1})");
}

static string Summ(List<double> l)
{
    var c = l.ToList();
    c.Sort();
    return $"avg={c.Average():F2} p50={c[c.Count / 2]:F2} p95={c[(int)(c.Count * 0.95)]:F2} max={c[^1]:F2}";
}

Console.WriteLine($"map={mapSize} units={w.Units.AliveCount} projectiles={w.Projectiles.AliveCount} threads={Par.MaxThreads} ticks={ticks}");
Console.WriteLine($"tick ms: {Summ(times)} (budget 100ms @10Hz)");
Console.WriteLine($"  {stages.Format()}");
Console.WriteLine($"  path ms: {Summ(pathMs)}");
Console.WriteLine($"  move ms: {Summ(moveMs)}");
Console.WriteLine($"  intel ms: {Summ(intelMs)} (fog grid {w.Intel.Cols}x{w.Intel.Rows})");
Console.WriteLine($"  snapshot ms (view army 0, not in tick): {Summ(snapMs)}");
long solves = stats.TotalSolves - solves0;
Console.WriteLine($"  path solves={solves} splices={stats.TotalSplices} avgExpanded={(solves > 0 ? (double)(stats.TotalExpanded - exp0) / solves : 0):F0} goalFields={w.Paths.CachedGoalFields}");
Console.WriteLine($"total {total.Elapsed.TotalSeconds:F1}s, hash={sim.StateHash():X16}");
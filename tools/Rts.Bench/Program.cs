using System.Diagnostics;
using System.Numerics;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;

// Headless benchmark: N units per side attack-move across the map.
// Usage: Rts.Bench [unitsPerSide] [ticks] [threads (0 = default)] [mapSize = 512]
//        Rts.Bench ai|aipassive [maxMinutes] [mapSize] [seed] [difficulties] [threads] [players]  (skirmish AI match, see AiMatch.cs)
if (args.Length > 0 && args[0] is "ai" or "aipassive")
{
    AiMatch.Run(args[1..], passive: args[0] == "aipassive");
    return;
}
int perSide = args.Length > 0 ? int.Parse(args[0]) : 2000;
int ticks = args.Length > 1 ? int.Parse(args[1]) : 300;
if (args.Length > 2 && int.Parse(args[2]) > 0) Par.MaxThreads = int.Parse(args[2]);
int mapSize = args.Length > 3 ? int.Parse(args[3]) : 512;

var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
var setup = Stopwatch.StartNew();
var sim = GameSetup.CreateSkirmish(db, new SkirmishSettings { MapSize = mapSize, Seed = 3, UnitCapacity = Math.Max(16384, perSide * 2 + 1000), AiPlayers = [false, false] });
var w = sim.World;
string[] kinds = ["uef_t1_tank", "uef_t1_artillery", "uef_t1_mobile_aa", "uef_t1_interceptor"];
var sides = new[] { new List<UnitHandle>(), new List<UnitHandle>() };
// 80/432 on the default 512 map (same scenario as before), scaled for other sizes.
var bases = new[] { new Vector2(mapSize * 0.15625f, mapSize * 0.15625f), new Vector2(mapSize * 0.84375f, mapSize * 0.84375f) };
for (int s = 0; s < 2; s++)
{
    int cols = (int)Math.Sqrt(perSide);
    for (int k = 0; k < perSide; k++)
    {
        var off = new Vector2(k % cols, k / cols) * 1.6f * (s == 0 ? 1 : -1);
        var bp = db.IndexOf(kinds[k % kinds.Length]);
        var h = w.SpawnUnit(bp, s, bases[s] + off, 0);
        if (!h.IsNone) sides[s].Add(h);
    }
}
sim.Step([
    new IssueOrderCommand(0, sides[0].ToArray(), Order.AttackMoveTo(bases[1]), false),
    new IssueOrderCommand(1, sides[1].ToArray(), Order.AttackMoveTo(bases[0]), false),
]);
Console.WriteLine($"setup {setup.Elapsed.TotalMilliseconds:F0} ms (first tick incl. nav build {sim.LastTickMs:F1} ms)");

var times = new List<double>();
var pathMs = new List<double>();
var moveMs = new List<double>();
var stats = w.Paths.Stats;
long solves0 = stats.TotalSolves, exp0 = stats.TotalExpanded;
var total = Stopwatch.StartNew();
for (int t = 0; t < ticks; t++)
{
    sim.Step();
    times.Add(sim.LastTickMs);
    pathMs.Add(stats.LastPathMs);
    moveMs.Add(stats.LastMoveMs);
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
Console.WriteLine($"  path ms: {Summ(pathMs)}");
Console.WriteLine($"  move ms: {Summ(moveMs)}");
long solves = stats.TotalSolves - solves0;
Console.WriteLine($"  path solves={solves} splices={stats.TotalSplices} avgExpanded={(solves > 0 ? (double)(stats.TotalExpanded - exp0) / solves : 0):F0} goalFields={w.Paths.CachedGoalFields}");
Console.WriteLine($"total {total.Elapsed.TotalSeconds:F1}s, hash={sim.StateHash():X16}");
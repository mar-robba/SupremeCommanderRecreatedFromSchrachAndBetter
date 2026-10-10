using System.Diagnostics;
using System.Numerics;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;

// Headless benchmark: N units per side attack-move across a 512 map. Usage: Rts.Bench [unitsPerSide] [ticks] [threads]
int perSide = args.Length > 0 ? int.Parse(args[0]) : 2000;
int ticks = args.Length > 1 ? int.Parse(args[1]) : 300;
if (args.Length > 2) Par.MaxThreads = int.Parse(args[2]);

var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
var sim = GameSetup.CreateSkirmish(db, new SkirmishSettings { MapSize = 512, Seed = 3, UnitCapacity = Math.Max(16384, perSide * 2 + 1000) });
var w = sim.World;
string[] kinds = ["uef_t1_tank", "uef_t1_artillery", "uef_t1_mobile_aa", "uef_t1_interceptor"];
var sides = new[] { new List<UnitHandle>(), new List<UnitHandle>() };
var bases = new[] { new Vector2(80, 80), new Vector2(432, 432) };
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

var times = new List<double>();
var total = Stopwatch.StartNew();
for (int t = 0; t < ticks; t++)
{
    sim.Step();
    times.Add(sim.LastTickMs);
}
times.Sort();
Console.WriteLine($"units={w.Units.AliveCount} projectiles={w.Projectiles.AliveCount} threads={Par.MaxThreads} ticks={ticks}");
Console.WriteLine($"tick ms: avg={times.Average():F2} p50={times[times.Count / 2]:F2} p95={times[(int)(times.Count * 0.95)]:F2} max={times[^1]:F2} (budget 100ms @10Hz)");
Console.WriteLine($"total {total.Elapsed.TotalSeconds:F1}s, hash={sim.StateHash():X16}");

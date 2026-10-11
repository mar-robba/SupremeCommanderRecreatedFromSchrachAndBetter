using System.Diagnostics;
using Rts.Sim;
using Rts.Sim.AI;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

/// <summary>
/// Headless skirmish AI match: every army is an AI (or army 0 is passive with "aipassive").
/// Usage: Rts.Bench ai|aipassive [maxMinutes=30] [mapSize=256] [seed=42] [difficulties=normal,normal] [threads=0] [players=2]
/// Prints a status line per game minute, then the winner, duration and per-AI cost (ms per tick).
/// </summary>
public static class AiMatch
{
    public static void Run(string[] args, bool passive)
    {
        float maxMinutes = args.Length > 0 ? float.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 30;
        int mapSize = args.Length > 1 ? int.Parse(args[1]) : 256;
        int seed = args.Length > 2 ? int.Parse(args[2]) : 42;
        string[] diffs = args.Length > 3 ? args[3].Split(',') : ["normal"];
        if (args.Length > 4 && int.Parse(args[4]) > 0) Par.MaxThreads = int.Parse(args[4]);
        int players = args.Length > 5 ? int.Parse(args[5]) : 2;

        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        var ai = Enumerable.Range(0, players).Select(i => !passive || i > 0).ToArray();
        var sim = GameSetup.CreateSkirmish(db, new SkirmishSettings { MapSize = mapSize, Seed = seed, Players = players, AiPlayers = ai });
        var w = sim.World;
        for (int a = 0; a < players; a++)
            w.Armies[a].AiDifficulty = Enum.Parse<AiDifficulty>(diffs[Math.Min(a, diffs.Length - 1)], ignoreCase: true);
        Console.WriteLine($"AI match: map {mapSize}, seed {seed}, players {players} ({string.Join(" vs ", w.Armies.Select(a => a.IsAI ? a.AiDifficulty.ToString() : "passive"))}), threads {Par.MaxThreads}");

        int maxTicks = (int)(maxMinutes * 60 * SimWorld.TicksPerSecond);
        var deaths = new int[players];
        var tickMs = new List<double>(maxTicks);
        var total = Stopwatch.StartNew();
        int firstDeathTick = -1;
        for (int t = 0; t < maxTicks && w.WinningTeam < 0; t++)
        {
            sim.Step();
            tickMs.Add(sim.LastTickMs);
            // Effects are cosmetic and normally drained by the snapshot writer: count deaths, then drop them.
            foreach (var fx in w.Events.Effects)
                if (fx.Kind == EffectKind.UnitDeath && (uint)fx.Army < (uint)players)
                {
                    deaths[fx.Army]++;
                    if (firstDeathTick < 0) firstDeathTick = w.Tick;
                }
            w.Events.Effects.Clear();
            if (w.Tick % 600 == 0) PrintStatus(w, deaths);
        }

        Console.WriteLine();
        if (w.WinningTeam >= 0)
        {
            var winners = w.Armies.Where(a => a.Team == w.WinningTeam).Select(a => $"{a.Name} ({(a.IsAI ? a.AiDifficulty.ToString() : "passive")})");
            Console.WriteLine($"WINNER: {string.Join(", ", winners)} after {Fmt(w.GameTime)} game time");
        }
        else Console.WriteLine($"NO WINNER after {Fmt(w.GameTime)} game time (limit)");
        Console.WriteLine($"first unit death at {(firstDeathTick < 0 ? "never" : Fmt(firstDeathTick * SimWorld.Dt))}; deaths per army: {string.Join(", ", deaths)}");
        var st = AiSystem.StateOf(w);
        for (int a = 0; a < players; a++)
        {
            var b = st?.BrainOrNull(a);
            if (b == null) continue;
            Console.WriteLine($"  {w.Armies[a].Name}: AI cost avg {b.Stats.AvgMs:F3} ms/tick, max {b.Stats.MaxMs:F2} ms, (scan {b.Stats.ScanMs:F0} / eco {b.Stats.EconMs:F0} / mil {b.Stats.MilitaryMs:F0} ms total, sites {b.Stats.SiteSearches}/{b.Stats.SiteFailures} failed/{b.Stats.SiteCandidates} cand), commands {b.Stats.Commands}, waves {b.Military.WavesLaunched}, " +
                              $"deep stall E {Pct(b.Economy.DeepEnergyStallThinks, b.Economy.Thinks)} M {Pct(b.Economy.DeepMassStallThinks, b.Economy.Thinks)}");
        }
        tickMs.Sort();
        Console.WriteLine($"sim tick ms: avg {tickMs.Average():F2} p50 {tickMs[tickMs.Count / 2]:F2} p95 {tickMs[(int)(tickMs.Count * 0.95)]:F2} max {tickMs[^1]:F2}; " +
                          $"wall {total.Elapsed.TotalSeconds:F1}s ({w.Tick / total.Elapsed.TotalSeconds:F0} ticks/s), hash {sim.StateHash():X16}");
    }

    private static string Pct(int a, int b) => b > 0 ? $"{100.0 * a / b:F0}%" : "-";

    private static string Fmt(float seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:D2}";

    private static void PrintStatus(SimWorld w, int[] deaths)
    {
        var u = w.Units;
        var line = new System.Text.StringBuilder($"[{Fmt(w.GameTime)}]");
        for (int a = 0; a < w.Armies.Length; a++)
        {
            var army = w.Armies[a];
            int mex = 0, fac = 0, land = 0, air = 0, eng = 0, pgen = 0, def = 0;
            for (int k = 0; k < u.AliveCount; k++)
            {
                int i = u.AliveList[k];
                if (!u.IsActive(i) || u.Army[i] != a || !u.IsComplete(i)) continue;
                var bp = w.BpOf(i);
                if (bp.Has(UnitCategory.MassExtractor)) mex++;
                else if (bp.Has(UnitCategory.Factory)) fac++;
                else if (bp.Has(UnitCategory.PowerGenerator)) pgen++;
                else if (bp.Has(UnitCategory.Defense)) def++;
                else if (bp.Has(UnitCategory.Engineer) && !bp.Has(UnitCategory.Commander)) eng++;
                else if (bp.IsMobile && bp.Movement.Layer == Rts.Sim.Data.MotionLayer.Air) air++;
                else if (bp.IsMobile && !bp.Has(UnitCategory.Commander)) land++;
            }
            var e = army.Eco;
            var cmd = army.Commander;
            string hp = u.IsActive(cmd) ? $"{u.Health[cmd.Index] / w.BpOf(cmd.Index).MaxHealth * 100:F0}%" : "dead";
            line.Append($" | {army.Name}{(army.Defeated ? " DEFEATED" : "")}: n={army.UnitCount} mex={mex} pg={pgen} fac={fac} def={def} eng={eng} land={land} air={air} " +
                        $"M {e.MassIncome:F0}/{e.MassDemand:F0}/{e.Mass:F0} E {e.EnergyIncome:F0}/{e.EnergyDemand:F0}/{e.Energy:F0} eff {e.MassEfficiency:F2}/{e.EnergyEfficiency:F2} lost={deaths[a]} acu={hp}");
        }
        Console.WriteLine(line.ToString());
    }
}

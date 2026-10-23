using System.Diagnostics;
using Rts.Sim;
using Rts.Sim.AI;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

/// <summary>
/// Headless skirmish AI match: every army is an AI (or army 0 is passive with "aipassive").
/// Usage: Rts.Bench ai|aipassive [maxMinutes=30] [mapSize=256] [seed=42] [difficulties=normal,normal] [threads=0] [players=2] [preset=plains] [factions=uef,uef (or random)]
/// Prints a status line per game minute, then the winner, duration, end-screen statistics and per-AI cost (ms per tick).
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
        var preset = MapPreset.Plains;
        if (args.Length > 6 && !MapPresets.TryParse(args[6], out preset)) throw new ArgumentException("unknown preset " + args[6]);
        // Faction per army (the last one repeats); default = every army UEF.
        string[]? factions = args.Length > 7 ? args[7].Split(',') : null;

        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        if (Environment.GetEnvironmentVariable("BENCH_WARMUP") == "1") SimWarmup.Run(db); // JIT warm-up as the client does
        var ai = Enumerable.Range(0, players).Select(i => !passive || i > 0).ToArray();
        var sim = GameSetup.CreateSkirmish(db, new SkirmishSettings
        {
            MapSize = mapSize, Seed = seed, Players = players, AiPlayers = ai, MapPreset = preset,
            Factions = factions == null ? null : Enumerable.Range(0, players).Select(a => factions[Math.Min(a, factions.Length - 1)]).ToArray(),
            AiDifficulties = Enumerable.Range(0, players).Select(a => Enum.Parse<AiDifficulty>(diffs[Math.Min(a, diffs.Length - 1)], ignoreCase: true)).ToArray(),
        });
        var w = sim.World;
        Console.WriteLine($"AI match: map {mapSize} {MapPresets.Name(preset)}, seed {seed}, players {players} ({string.Join(" vs ", w.Armies.Select(a => (a.IsAI ? a.AiDifficulty.ToString() : "passive") + " " + a.Faction))}), threads {Par.MaxThreads}");

        int maxTicks = (int)(maxMinutes * 60 * SimWorld.TicksPerSecond);
        // Status line interval in ticks (env AI_STATUS_SECONDS, default one game minute).
        int statusEvery = Math.Max(1, (int)(float.Parse(Environment.GetEnvironmentVariable("AI_STATUS_SECONDS") ?? "60", System.Globalization.CultureInfo.InvariantCulture) * SimWorld.TicksPerSecond));
        var deaths = new int[players];
        _spend = new SpendTracker(players);
        _kills = new KillTracker(players);
        // Where units die (diagnostics): per army, [0] own base, [1] enemy base, [2] field; mobile and structures.
        var deathZoneMobile = new int[players, 3];
        var deathZoneStruct = new int[players, 3];
        var mexLost = new int[players];
        var starts = w.Terrain.StartPositions;
        if (Environment.GetEnvironmentVariable("AI_TRACE") == "1")
            AiSystem.Trace = (army, msg) => Console.WriteLine($"  <{Fmt(w.GameTime)}> a{army} {msg}");
        var tickMs = new List<double>(maxTicks);
        var total = Stopwatch.StartNew();
        var stages = new StageStats();
        int firstDeathTick = -1;
        for (int t = 0; t < maxTicks && w.WinningTeam < 0; t++)
        {
            sim.Step();
            if (t == 0) { ApplyCfgOverrides(w, players); Console.WriteLine($"first tick {sim.LastTickMs:F1} ms: {StageStats.LastTick(sim)}"); stages.Start(sim); }
            else stages.Add(sim);
            if (t == 0 && Environment.GetEnvironmentVariable("AI_OMNI") == "1")
                for (int a = 0; a < players; a++)
                    if (AiSystem.StateOf(w)?.BrainOrNull(a) is { } br) br.Perception.RespectIntel = false; // omniscient AIs (comparison)
            tickMs.Add(sim.LastTickMs);
            // Effects are cosmetic and normally drained by the snapshot writer: count deaths, then drop them.
            foreach (var fx in w.Events.Effects)
                if (fx.Kind == EffectKind.UnitDeath && (uint)fx.Army < (uint)players)
                {
                    deaths[fx.Army]++;
                    if (firstDeathTick < 0) firstDeathTick = w.Tick;
                    var p = new System.Numerics.Vector2(fx.Pos.X, fx.Pos.Z);
                    int zone = 2;
                    if (System.Numerics.Vector2.Distance(p, starts[fx.Army % starts.Length]) < 45f) zone = 0;
                    else
                        for (int a = 0; a < players; a++)
                            if (a != fx.Army && System.Numerics.Vector2.Distance(p, starts[a % starts.Length]) < 45f) { zone = 1; break; }
                    var dbp = (uint)fx.BlueprintIndex < (uint)db.Count ? db[fx.BlueprintIndex] : null;
                    if (dbp != null && dbp.Has(UnitCategory.Commander))
                        Console.WriteLine($"COMMANDER DEATH {w.Armies[fx.Army].Name} at {Fmt(w.GameTime)} pos=({p.X:F0},{p.Y:F0}) {System.Numerics.Vector2.Distance(p, starts[fx.Army % starts.Length]):F0} from its start");
                    if (dbp != null && dbp.IsStructure) { deathZoneStruct[fx.Army, zone]++; if (dbp.Has(UnitCategory.MassExtractor)) mexLost[fx.Army]++; }
                    else deathZoneMobile[fx.Army, zone]++;
                }
            w.Events.Effects.Clear();
            if (w.Tick % 5 == 0) _spend.Scan(w);
            _kills!.Scan(w);
            if (w.Tick % statusEvery == 0) PrintStatus(w, deaths);
            if (Environment.GetEnvironmentVariable("AI_DUMP") is { } dump) DumpGroup(w, dump);
            if (Environment.GetEnvironmentVariable("AI_BASE") is { } baseSpec) DumpBase(w, baseSpec);
            if (Environment.GetEnvironmentVariable("AI_DEPOSITS") is { } depMin && w.Tick == (int)(float.Parse(depMin, System.Globalization.CultureInfo.InvariantCulture) * 600))
                for (int a = 0; a < players; a++)
                    if (AiSystem.StateOf(w)?.BrainOrNull(a) is { } br) Console.Write($"  deposits seen by {w.Armies[a].Name}:\n{br.Economy.DescribeDeposits()}");
        }

        Console.WriteLine();
        if (w.WinningTeam >= 0)
        {
            var winners = w.Armies.Where(a => a.Team == w.WinningTeam).Select(a => $"{a.Name} ({(a.IsAI ? a.AiDifficulty.ToString() : "passive")})");
            Console.WriteLine($"WINNER: {string.Join(", ", winners)} after {Fmt(w.GameTime)} game time");
        }
        else Console.WriteLine($"NO WINNER after {Fmt(w.GameTime)} game time (limit)");
        Console.WriteLine($"first unit death at {(firstDeathTick < 0 ? "never" : Fmt(firstDeathTick * SimWorld.Dt))}; deaths per army: {string.Join(", ", deaths)}");
        for (int a = 0; a < players; a++)
            Console.WriteLine($"  {w.Armies[a].Name} deaths by zone (own base / enemy base / field): mobile {deathZoneMobile[a, 0]}/{deathZoneMobile[a, 1]}/{deathZoneMobile[a, 2]}, " +
                              $"structures {deathZoneStruct[a, 0]}/{deathZoneStruct[a, 1]}/{deathZoneStruct[a, 2]} (mex {mexLost[a]})");
        for (int a = 0; a < players; a++) Console.WriteLine($"  {w.Armies[a].Name} kills by: {_kills!.Format(a)}");
        foreach (var army in w.Armies)
        {
            var s = army.Stats;
            Console.WriteLine($"  {army.Name} stats: built {s.UnitsBuilt} units / {s.StructuresBuilt} structures, lost {s.UnitsLost}, killed {s.UnitsKilled}, " +
                              $"mass {s.MassCollected:F0} collected / {s.MassSpent:F0} spent");
        }
        var st = AiSystem.StateOf(w);
        for (int a = 0; a < players; a++)
        {
            var b = st?.BrainOrNull(a);
            if (b == null) continue;
            Console.WriteLine($"  {w.Armies[a].Name}: AI cost avg {b.Stats.AvgMs:F3} ms/tick, max {b.Stats.MaxMs:F2} ms, (scan {b.Stats.ScanMs:F0} / eco {b.Stats.EconMs:F0} / mil {b.Stats.MilitaryMs:F0} ms total, sites {b.Stats.SiteSearches}/{b.Stats.SiteFailures} failed/{b.Stats.SiteCandidates} cand), commands {b.Stats.Commands}, waves {b.Military.WavesLaunched}, " +
                              $"deep stall E {Pct(b.Economy.DeepEnergyStallThinks, b.Economy.Thinks)} M {Pct(b.Economy.DeepMassStallThinks, b.Economy.Thinks)}");
            var ts = b.Stats;
            Console.WriteLine($"  {w.Armies[a].Name} tech: tier={ts.MaxTier} upgrades={ts.Upgrades} enh={ts.Enhancements} exp={ts.Experimentals} " +
                              $"nukes={ts.Nukes} tml={ts.Tml} navy={ts.Naval} drops={ts.Drops} captures={ts.Captures}");
        }
        tickMs.Sort();
        Console.WriteLine($"sim tick ms: avg {tickMs.Average():F2} p50 {tickMs[tickMs.Count / 2]:F2} p95 {tickMs[(int)(tickMs.Count * 0.95)]:F2} max {tickMs[^1]:F2}; " +
                          $"wall {total.Elapsed.TotalSeconds:F1}s ({w.Tick / total.Elapsed.TotalSeconds:F0} ticks/s), hash {sim.StateHash():X16}");
        Console.WriteLine($"  {stages.Format()}");
    }

    /// <summary>AI_CFG="Field=value,..." (all AIs), AI_CFG0/AI_CFG1/... (one army), AI_CFG_Hard/_Normal/_Easy (one difficulty): override AiConfig fields for
    /// parameter sweeps and head-to-head A/B games (bench only; the sim never reads the environment).</summary>
    private static void ApplyCfgOverrides(SimWorld w, int players)
    {
        for (int a = 0; a < players; a++)
        {
            if (AiSystem.StateOf(w)?.BrainOrNull(a) is not { } br) continue;
            foreach (var spec in new[] { Environment.GetEnvironmentVariable("AI_CFG"), Environment.GetEnvironmentVariable("AI_CFG" + a),
                                       Environment.GetEnvironmentVariable("AI_CFG_" + br.Cfg.Difficulty) })
            {
                if (string.IsNullOrEmpty(spec)) continue;
                foreach (var kv in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = kv.Split('=');
                    var f = typeof(Rts.Sim.AI.AiConfig).GetField(parts[0].Trim()) ?? throw new ArgumentException("AI_CFG: no field " + parts[0]);
                    f.SetValue(br.Cfg, Convert.ChangeType(parts[1].Trim(), f.FieldType, System.Globalization.CultureInfo.InvariantCulture));
                    Console.WriteLine($"  cfg a{a}: {f.Name}={f.GetValue(br.Cfg)}");
                }
            }
        }
    }

    /// <summary>AI_BASE="seconds:army[,...]": the army's armed units and structures within 50 of its start (type,
    /// position, health) and the enemy units within 100 of it by type (count, how many stand still).</summary>
    private static void DumpBase(SimWorld w, string spec)
    {
        foreach (var part in spec.Split(','))
        {
            var f = part.Split(':');
            if (f.Length < 2 || w.Tick != (int)(float.Parse(f[0], System.Globalization.CultureInfo.InvariantCulture) * 10)) continue;
            int army = int.Parse(f[1]);
            var c = w.Terrain.StartPositions[army % w.Terrain.StartPositions.Length];
            var u = w.Units;
            var mine = new SortedDictionary<string, string>();
            var foes = new SortedDictionary<string, (int n, int still, float dmin)>();
            for (int k = 0; k < u.AliveCount; k++)
            {
                int i = u.AliveList[k];
                if (!u.IsActive(i) || !u.IsComplete(i)) continue;
                var bp = w.BpOf(i);
                float d = System.Numerics.Vector2.Distance(u.Pos[i], c);
                if (u.Army[i] == army && d < 50f && (bp.Weapons.Length > 0 || bp.Has(UnitCategory.Factory) || bp.Has(UnitCategory.Shield)))
                {
                    string key = bp.Id;
                    string v = $"({u.Pos[i].X:F0},{u.Pos[i].Y:F0}){u.Health[i] / bp.MaxHealth * 100:F0}%";
                    mine[key] = mine.TryGetValue(key, out var old) ? old + " " + v : v;
                }
                else if (u.Army[i] != army && d < 100f && bp.IsMobile)
                {
                    var e = foes.TryGetValue(bp.Id, out var x) ? x : (n: 0, still: 0, dmin: 999f);
                    e.n++;
                    if (!u.Move[i].HasGoal) e.still++;
                    e.dmin = MathF.Min(e.dmin, d);
                    foes[bp.Id] = e;
                }
            }
            Console.WriteLine($"  base a{army} at ({c.X:F0},{c.Y:F0}) t={Fmt(w.GameTime)}");
            foreach (var kv in mine) Console.WriteLine($"    own {kv.Key}: {kv.Value}");
            foreach (var kv in foes) Console.WriteLine($"    foe {kv.Key}: n={kv.Value.n} still={kv.Value.still} nearest={kv.Value.dmin:F0}");
        }
    }

    /// <summary>AI_DUMP="seconds:army:group[,seconds...]": prints the members of an AI group (orders, movement, path).</summary>
    private static void DumpGroup(SimWorld w, string spec)
    {
        foreach (var part in spec.Split(','))
        {
            var f = part.Split(':');
            if (f.Length < 3 || w.Tick != (int)(float.Parse(f[0], System.Globalization.CultureInfo.InvariantCulture) * 10)) continue;
            int army = int.Parse(f[1]), gid = int.Parse(f[2]);
            var brain = AiSystem.StateOf(w)?.BrainOrNull(army);
            var g = brain?.Military.Groups.FirstOrDefault(x => x.Id == gid);
            if (g == null) { Console.WriteLine($"  dump: no group {gid} for army {army}"); continue; }
            var u = w.Units;
            Console.WriteLine($"  dump a{army} g{gid} {g.State} target=({g.Target.X:F1},{g.Target.Y:F1}) members={g.Members.Count}");
            foreach (int i in g.Members)
            {
                ref var mv = ref u.Move[i];
                var q = u.Orders[i];
                string ord = q.IsEmpty ? "none" : $"{q.Current.Type}@({q.Current.Pos.X:F1},{q.Current.Pos.Y:F1})";
                var p = u.Pos[i];
                var path = w.Paths.PathOf(i);
                string tgt = "";
                for (int k = 0; k < w.BpOf(i).Weapons.Length; k++)
                {
                    var t = u.Weapons[i * UnitStore.MaxWeapons + k].Target;
                    if (u.IsActive(t))
                    {
                        // Terrain line of fire (muzzle -> target center), max penetration below the surface.
                        var a3 = new System.Numerics.Vector3(p.X, u.Elevation[i] + w.BpOf(i).Visual.Size.Y * 0.6f, p.Y);
                        var tp = u.Pos[t.Index];
                        var b3 = new System.Numerics.Vector3(tp.X, u.Elevation[t.Index] + w.BpOf(t.Index).Visual.Size.Y * 0.5f, tp.Y);
                        float worst = 0;
                        for (int sIdx = 1; sIdx < 64; sIdx++)
                        {
                            var q3 = System.Numerics.Vector3.Lerp(a3, b3, sIdx / 64f);
                            worst = MathF.Max(worst, w.Terrain.GetSurfaceHeight(new System.Numerics.Vector2(q3.X, q3.Z)) - q3.Y);
                        }
                        tgt += $" tgt={w.BpOf(t.Index).Id}@({tp.X:F0},{tp.Y:F0}) h={u.Elevation[i]:F1}->{u.Elevation[t.Index]:F1} block={worst:F1}";
                    }
                }
                Console.WriteLine($"    #{i} {w.BpOf(i).Id} pos=({p.X:F1},{p.Y:F1}) pass={w.Terrain.PassAt((int)p.X, (int)p.Y)} {ord} goal=({mv.Goal.X:F1},{mv.Goal.Y:F1}) hasGoal={mv.HasGoal} spd={mv.Speed:F2} " +
                                  $"path={mv.PathId}[{mv.PathIndex}/{path.Length}] end={(path.Length > 0 ? $"({path[^1].X:F0},{path[^1].Y:F0})" : "-")} partial={mv.PathPartial} nearest={mv.PathToNearest} " +
                                  $"stuck={mv.StuckTime:F1} blocked={mv.BlockedTime:F1} repaths={mv.StuckRepaths} grp={mv.GroupId}{tgt}");
            }
        }
    }

    private static string Pct(int a, int b) => b > 0 ? $"{100.0 * a / b:F0}%" : "-";

    private static string Fmt(float seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:D2}";

    private static SpendTracker? _spend;
    private static KillTracker? _kills;

    private static void PrintStatus(SimWorld w, int[] deaths)
    {
        var u = w.Units;
        var line = new System.Text.StringBuilder($"[{Fmt(w.GameTime)}]");
        for (int a = 0; a < w.Armies.Length; a++)
        {
            var army = w.Armies[a];
            int mex = 0, fac = 0, land = 0, air = 0, eng = 0, pgen = 0, def = 0;
            float armyM = 0, defM = 0, ecoM = 0, landM = 0; // mass value of complete army units / defenses / other structures / land army
            for (int k = 0; k < u.AliveCount; k++)
            {
                int i = u.AliveList[k];
                if (!u.IsActive(i) || u.Army[i] != a || !u.IsComplete(i)) continue;
                var bp = w.BpOf(i);
                float m = bp.Economy.BuildCostMass;
                if (bp.IsStructure) { if (bp.Has(UnitCategory.Defense) || bp.Has(UnitCategory.Shield)) defM += m; else ecoM += m; }
                else if (!bp.Has(UnitCategory.Engineer) && !bp.Has(UnitCategory.Commander)) armyM += m;
                if (bp.Has(UnitCategory.MassExtractor)) mex++;
                else if (bp.Has(UnitCategory.Factory)) fac++;
                else if (bp.Has(UnitCategory.PowerGenerator)) pgen++;
                else if (bp.Has(UnitCategory.Defense)) def++;
                else if (bp.Has(UnitCategory.Engineer) && !bp.Has(UnitCategory.Commander)) eng++;
                else if (bp.IsMobile && bp.Movement.Layer == Rts.Sim.Data.MotionLayer.Air) air++;
                else if (bp.IsMobile && !bp.Has(UnitCategory.Commander)) land++;
                if (bp.IsMobile && bp.Movement.Layer == Rts.Sim.Data.MotionLayer.Land && !bp.Has(UnitCategory.Engineer) && !bp.Has(UnitCategory.Commander)) landM += m;
            }
            var e = army.Eco;
            var cmd = army.Commander;
            string hp = u.IsActive(cmd) ? $"{u.Health[cmd.Index] / w.BpOf(cmd.Index).MaxHealth * 100:F0}%" : "dead";
            string mil = "";
            var brain = AiSystem.StateOf(w)?.BrainOrNull(a);
            if (brain != null)
            {
                int pool = 0, defend = 0, attack = 0;
                for (int k = 0; k < u.AliveCount; k++)
                {
                    int i = u.AliveList[k];
                    if (!u.IsActive(i) || u.Army[i] != a) continue;
                    var role = brain.Info(i).Role;
                    if (role == AiRole.Pool) pool++; else if (role == AiRole.Defend) defend++; else if (role == AiRole.Attack) attack++;
                }
                mil = $" pool={pool} defend={defend} attack={attack}/{brain.Military.Groups.Count}g waves={brain.Military.WavesLaunched} bud={brain.Economy.FactorySpend:F0}/{brain.Economy.EcoSpend:F0}/{brain.Economy.FactoryBudget:F1}/{brain.Economy.UpgradeSpend:F0}";
                if (Environment.GetEnvironmentVariable("AI_GROUPS") == "1")
                    foreach (var g in brain.Military.Groups)
                    {
                        var c = System.Numerics.Vector2.Zero;
                        int idle = 0;
                        foreach (int i in g.Members) { c += u.Pos[i]; if (u.Orders[i].IsEmpty) idle++; }
                        c /= Math.Max(1, g.Members.Count);
                        mil += $"\n      a{a} g{g.Id} {g.State}{(g.ReturnWhenDone ? " clear" : "")} n={g.Members.Count} idle={idle} at=({c.X:F0},{c.Y:F0}) target=({g.Target.X:F0},{g.Target.Y:F0}) age={(w.Tick - g.OrderTick) / 10}s";
                    }
            }
            line.Append($" | {army.Name}{(army.Defeated ? " DEFEATED" : "")}: n={army.UnitCount} mex={mex} pg={pgen} fac={fac} def={def} eng={eng} land={land} air={air} " +
                        $"M {e.MassIncome:F0}/{e.MassDemand:F0}/{e.Mass:F0} E {e.EnergyIncome:F0}/{e.EnergyDemand:F0}/{e.Energy:F0} eff {e.MassEfficiency:F2}/{e.EnergyEfficiency:F2} lost={deaths[a]} acu={hp}{mil} val={armyM / 1000f:F1}k/{defM / 1000f:F1}k/{ecoM / 1000f:F1}k lv={landM / 1000f:F1}k{_spend?.Format(a)}");
        }
        Console.WriteLine(line.ToString());
    }
}

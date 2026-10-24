using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Rts.Sim;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.World;

/// <summary>
/// Headless unit duels for balance work (docs/AI_TUNING.md "Unit balance").
/// <c>duel &lt;unitA&gt; &lt;unitB&gt; [mass=2000] [seed=1] [mapSize=256]</c>: equal mass of two unit types on flat ground,
/// facing each other just beyond the longer weapon range, attack-move at each other (the real sim: accuracy, area
/// damage, turning, veterancy; no fog, no AI). Units = blueprint id or <c>faction:role</c> (FactionRoles).
/// <c>duelmatrix &lt;factionA&gt; [factionB=factionA] [masses=600,2000,6000] [seeds=3]</c>: every pair of land combat roles,
/// one grid of mean shareA per mass. Each world steps single-threaded (Par.MaxThreads = 1); duels run in parallel
/// (env DUEL_THREADS, default = cores). Output is deterministic for the same args.
/// </summary>
public static class Duel
{
    /// <summary>Land combat roles of the matrix, in display order (short labels for the grid columns).</summary>
    public static readonly (string Role, string Label)[] CombatRoles =
    [
        (FactionRoles.TankT1, "tank1"), (FactionRoles.LightAssaultT1, "lgt1"), (FactionRoles.ArtilleryT1, "art1"),
        (FactionRoles.MobileAaT1, "aa1"), (FactionRoles.HeavyTankT2, "hvy2"), (FactionRoles.AmphibiousT2, "amph2"),
        (FactionRoles.MmlT2, "mml2"), (FactionRoles.MobileAaT2, "aa2"), (FactionRoles.AssaultT3, "asl3"),
        (FactionRoles.SiegeT3, "sieg3"), (FactionRoles.SniperT3, "snp3"), (FactionRoles.ArtilleryT3, "art3"),
    ];

    private const int TimeCapTicks = 15 * 60 * SimWorld.TicksPerSecond;
    /// <summary>No health lost by anyone for this long = stalemate (artillery shells fly up to ~40 s).</summary>
    private const int StallTicks = 90 * SimWorld.TicksPerSecond;
    /// <summary>Idle units (attack-move finished) get a new attack-move to the enemy centroid this often.</summary>
    private const int RetargetTicks = 50;

    public readonly record struct Result(int CountA, int CountB, float StartA, float StartB, int SurvA, int SurvB, float MassA,
        float MassB, float ValueA, float ValueB, float Seconds, string End)
    {
        public string Winner => SurvA > 0 && SurvB == 0 ? "A" : SurvB > 0 && SurvA == 0 ? "B" : "draw";
        /// <summary>A's share of the surviving value (mass x hp fraction); 0.5 when nothing survives.</summary>
        public float ShareA => ValueA + ValueB <= 0 ? 0.5f : ValueA / (ValueA + ValueB);
        /// <summary>Fraction of A's starting mass left (hp-weighted) minus B's: -1..1, fair when the counts round unevenly.</summary>
        public float Margin => ValueA / StartA - ValueB / StartB;
        /// <summary>The same duel seen from the other side (B as unit A).</summary>
        public Result Flip() => new(CountB, CountA, StartB, StartA, SurvB, SurvA, MassB, MassA, ValueB, ValueA, Seconds, End);
    }

    private static string F(float v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);
    /// <summary>Signed two decimals; rounds first so a tiny negative prints "+0.00", not "-0.00".</summary>
    private static string S(float v) => (MathF.Round(v, 2) + 0f).ToString("+0.00;-0.00", CultureInfo.InvariantCulture);

    public static void Run(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: duel <unitA> <unitB> [mass=2000] [seed=1] [mapSize=256]"); return; }
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        int a, b;
        try { a = Resolve(db, args[0]); b = Resolve(db, args[1]); }
        catch (ArgumentException e) { Console.WriteLine(e.Message); Environment.ExitCode = 1; return; }
        float mass = args.Length > 2 ? float.Parse(args[2], CultureInfo.InvariantCulture) : 2000;
        int seed = args.Length > 3 ? int.Parse(args[3]) : 1;
        int mapSize = args.Length > 4 ? int.Parse(args[4]) : 256;
        var sw = Stopwatch.StartNew();
        var r = Fight(db, a, b, mass, seed, mapSize);
        Console.WriteLine($"duel {db[a].Id} x{r.CountA} vs {db[b].Id} x{r.CountB} (mass {F(mass, "0")}, seed {seed}): " +
                          $"winner {r.Winner} ({r.End}), survivors A {r.SurvA} ({F(r.MassA, "0")} mass) B {r.SurvB} ({F(r.MassB, "0")} mass), " +
                          $"shareA {F(r.ShareA, "0.00")}, margin {S(r.Margin)}, {F(r.Seconds, "0.0")} s sim, {sw.Elapsed.TotalSeconds:F1} s real");
    }

    /// <summary>Blueprint id or "faction:role"; exits with a message when unknown.</summary>
    private static int Resolve(BlueprintDb db, string s)
    {
        int i = s.Contains(':') ? db.Factions.RoleBlueprint(s[..s.IndexOf(':')], s[(s.IndexOf(':') + 1)..]) : db.IndexOf(s);
        if (i < 0) throw new ArgumentException($"unknown unit '{s}' (blueprint id or faction:role)");
        return i;
    }

    private static float Cost(UnitBlueprint bp) => MathF.Max(1f, bp.Economy.BuildCostMass);

    private static LayerMask LayerOf(UnitBlueprint bp) => bp.Movement.Layer == MotionLayer.Air ? LayerMask.Air : LayerMask.Land;

    /// <summary>Weapons that can hit units on the target's layer (flat dry map: land, or air).</summary>
    private static IEnumerable<WeaponBlueprint> WeaponsVs(UnitBlueprint bp, LayerMask target) =>
        bp.Weapons.Where(wb => (wb.TargetMask & target) != 0 && wb.Damage > 0);

    private static float RangeVs(UnitBlueprint bp, LayerMask target) => WeaponsVs(bp, target).Select(wb => wb.Range).DefaultIfEmpty(0).Max();

    private static float DpsVs(UnitBlueprint bp, LayerMask target) =>
        WeaponsVs(bp, target).Sum(wb => wb.Damage * wb.ProjectilesPerShot * wb.RateOfFire);

    /// <summary>One duel: equal mass of A (army 0, west) and B (army 1, east) on flat ground.</summary>
    public static Result Fight(BlueprintDb db, int a, int b, float mass, int seed, int mapSize)
    {
        var bps = new[] { db[a], db[b] };
        var counts = bps.Select(bp => Math.Max(1, (int)MathF.Round(mass / Cost(bp)))).ToArray();
        bool aHits = WeaponsVs(bps[0], LayerOf(bps[1])).Any(), bHits = WeaponsVs(bps[1], LayerOf(bps[0])).Any();
        float startA = counts[0] * Cost(bps[0]), startB = counts[1] * Cost(bps[1]);
        if (!aHits && !bHits) return new Result(counts[0], counts[1], startA, startB, counts[0], counts[1], startA, startB, startA, startB, 0, "no weapons");

        float range = MathF.Max(RangeVs(bps[0], LayerOf(bps[1])), RangeVs(bps[1], LayerOf(bps[0])));
        float gap = range + 4f; // front rows start just out of range
        float c = mapSize * 0.5f;
        var heights = new float[(mapSize + 1) * (mapSize + 1)];
        Array.Fill(heights, 10f);
        var terrain = new Terrain(mapSize, mapSize, heights, 0f, [], [new Vector2(c - gap / 2, c), new Vector2(c + gap / 2, c)], seed);
        var armies = new Army[2];
        for (int s = 0; s < 2; s++)
            armies[s] = new Army { Index = s, Team = s, Name = s == 0 ? "A" : "B", Faction = bps[s].Faction is { Length: > 0 } f ? f : "uef" };
        int cap = counts.Sum() + 256;
        var w = new SimWorld(db, terrain, armies, (ulong)seed, unitCapacity: Math.Max(1024, cap), projectileCapacity: 16384);
        w.Intel.Enabled = false; // no fog: everyone sees everything (stealth/cloak have no effect)
        var sim = new Simulation(w);

        // Compact formations: rows across the line (z), deeper rows away from the enemy; small per-seed jitter.
        var units = new List<UnitHandle>[2];
        var starts = new Vector2[2];
        for (int s = 0; s < 2; s++)
        {
            var bp = bps[s];
            int n = counts[s];
            float dir = s == 0 ? -1f : 1f; // rows grow away from the center
            float spacing = MathF.Max(2f * bp.Radius + 1f, 1.6f);
            int cols = Math.Min(n, Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(n * 3f))));
            var rng = DetRandom.For((ulong)seed, 0, s, 0x5D);
            units[s] = new List<UnitHandle>(n);
            float front = c + dir * gap / 2;
            for (int k = 0; k < n; k++)
            {
                int row = k / cols, col = k % cols;
                int inRow = Math.Min(cols, n - row * cols);
                float x = front + dir * row * spacing + rng.Range(-0.25f, 0.25f) * spacing;
                float z = c + (col - (inRow - 1) * 0.5f) * spacing + rng.Range(-0.25f, 0.25f) * spacing;
                var h = w.SpawnUnit(bp.Index, s, new Vector2(x, z), s == 0 ? 0f : MathF.PI, complete: true);
                if (!h.IsNone) units[s].Add(h);
            }
            starts[s] = new Vector2(front + dir * (n / cols) * spacing * 0.5f, c);
        }
        sim.Step([
            new IssueOrderCommand(0, units[0].ToArray(), Order.AttackMoveTo(starts[1]), false),
            new IssueOrderCommand(1, units[1].ToArray(), Order.AttackMoveTo(starts[0]), false),
        ]);

        var u = w.Units;
        int lastHit = 0, tick = 1;
        float lastHp = float.MaxValue;
        string end = "time cap";
        var alive = new int[2];
        var cmds = new List<Command>();
        var idle = new List<UnitHandle>();
        for (; tick < TimeCapTicks; tick++)
        {
            float hp = 0;
            for (int s = 0; s < 2; s++)
            {
                alive[s] = 0;
                foreach (var h in units[s])
                    if (u.IsActive(h)) { alive[s]++; hp += u.Health[h.Index]; }
            }
            if (alive[0] == 0 || alive[1] == 0) { end = "wipe"; break; }
            if (hp < lastHp - 0.01f) lastHit = tick;
            lastHp = hp;
            if (tick - lastHit > StallTicks) { end = "stalemate"; break; }

            cmds.Clear();
            if (tick % RetargetTicks == 0)
                for (int s = 0; s < 2; s++)
                {
                    idle.Clear();
                    foreach (var h in units[s])
                        if (u.IsActive(h) && u.Orders[h.Index].IsEmpty) idle.Add(h);
                    if (idle.Count == 0) continue;
                    Vector2 sum = Vector2.Zero;
                    int m = 0;
                    foreach (var h in units[1 - s])
                        if (u.IsActive(h)) { sum += u.Pos[h.Index]; m++; }
                    cmds.Add(new IssueOrderCommand(s, idle.ToArray(), Order.AttackMoveTo(sum / m), false));
                }
            if (cmds.Count > 0) sim.Step(cmds.ToArray()); else sim.Step();
        }

        var surv = new int[2];
        var survMass = new float[2];
        var value = new float[2];
        for (int s = 0; s < 2; s++)
            foreach (var h in units[s])
            {
                if (!u.IsActive(h)) continue;
                surv[s]++;
                float m = Cost(bps[s]);
                survMass[s] += m;
                value[s] += m * MathF.Min(1f, u.Health[h.Index] / Veterancy.MaxHealth(w, h.Index));
            }
        return new Result(counts[0], counts[1], startA, startB, surv[0], surv[1], survMass[0], survMass[1], value[0], value[1], tick * SimWorld.Dt, end);
    }

    public static void Matrix(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("usage: duelmatrix <factionA> [factionB=factionA] [masses=600,2000,6000] [seeds=3]"); return; }
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        string fa = args[0], fb = args.Length > 1 ? args[1] : fa;
        float[] masses = (args.Length > 2 ? args[2] : "600,2000,6000").Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        int seeds = args.Length > 3 ? int.Parse(args[3]) : 3;
        const int mapSize = 256;
        int threads = int.TryParse(Environment.GetEnvironmentVariable("DUEL_THREADS"), out int t) && t > 0 ? t : Environment.ProcessorCount;
        Par.MaxThreads = 1; // each world steps on one thread; worlds run side by side (no shared mutable sim state)

        // Roles the faction has, whose unit can hurt land units (mobile AA without a ground weapon only parks in the enemy).
        bool Fights(string f, string role) => db.Factions.RoleBlueprint(f, role) is int bi && bi >= 0 && WeaponsVs(db[bi], LayerMask.Land).Any();
        var rolesA = CombatRoles.Where(r => Fights(fa, r.Role)).ToArray();
        var rolesB = CombatRoles.Where(r => Fights(fb, r.Role)).ToArray();
        var skipped = CombatRoles.Where(r => db.Factions.RoleBlueprint(fa, r.Role) >= 0 && !Fights(fa, r.Role)
                                             || fb != fa && db.Factions.RoleBlueprint(fb, r.Role) >= 0 && !Fights(fb, r.Role)).Select(r => r.Label).ToArray();
        if (rolesA.Length == 0 || rolesB.Length == 0) { Console.WriteLine($"no combat roles for '{fa}' / '{fb}'"); return; }
        int na = rolesA.Length, nb = rolesB.Length;
        int samples = seeds * 2; // each seed twice: A west (army 0) and A east (army 1), so no side/index bias
        int perMass = na * nb * samples;
        var results = new Result[masses.Length * perMass];
        // Biggest mass first (longest duels) for load balance; results land by index, so the output order is fixed.
        var order = Enumerable.Range(0, results.Length).OrderByDescending(j => masses[j / perMass]).ThenBy(j => j).ToArray();
        int next = -1;
        var sw = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, Math.Min(threads, results.Length)).Select(_ => new Thread(() =>
        {
            int k;
            while ((k = Interlocked.Increment(ref next)) < order.Length)
            {
                int j = order[k], mi = j / perMass, rest = j % perMass;
                int i = rest / (nb * samples), jj = rest / samples % nb, s = rest % samples / 2;
                int ua = db.Factions.RoleBlueprint(fa, rolesA[i].Role), ub = db.Factions.RoleBlueprint(fb, rolesB[jj].Role);
                results[j] = rest % 2 == 0 ? Fight(db, ua, ub, masses[mi], s + 1, mapSize) : Fight(db, ub, ua, masses[mi], s + 1, mapSize).Flip();
            }
        }, 64 << 20)).ToList();
        workers.ForEach(x => x.Start());
        workers.ForEach(x => x.Join());

        var sb = new StringBuilder();
        sb.AppendLine($"duelmatrix {fa} vs {fb}: masses {string.Join(",", masses.Select(m => F(m, "0")))}, seeds {seeds} x 2 sides, map {mapSize}, " +
                      $"{results.Length} duels, {threads} threads, {sw.Elapsed.TotalSeconds:F1} s");
        if (skipped.Length > 0) sb.AppendLine($"skipped (no weapon vs land): {string.Join(", ", skipped)}");
        sb.AppendLine("units (dps/range vs land; aoe = largest damage radius; eff = hp*dps/mass^2):");
        sb.AppendLine($"  {"label",-6} {"id",-28} {"mass",5} {"hp",6} {"dps",6} {"range",5} {"aoe",4} {"speed",5} {"eff",5}");
        var listed = new HashSet<int>();
        foreach (var (f, roles) in new[] { (fa, rolesA), (fb, rolesB) })
            foreach (var r in roles)
            {
                int bi = db.Factions.RoleBlueprint(f, r.Role);
                if (!listed.Add(bi)) continue;
                var bp = db[bi];
                float dps = DpsVs(bp, LayerMask.Land), m = Cost(bp);
                float aoe = WeaponsVs(bp, LayerMask.Land).Select(wb => wb.DamageRadius).DefaultIfEmpty(0).Max();
                sb.AppendLine($"  {r.Label,-6} {bp.Id,-28} {F(m, "0"),5} {F(bp.MaxHealth, "0"),6} {F(dps, "0.0"),6} " +
                              $"{F(RangeVs(bp, LayerMask.Land), "0"),5} {F(aoe, "0.#"),4} {F(bp.Movement.MaxSpeed, "0.0"),5} {F(bp.MaxHealth * dps / (m * m), "0.00"),5}");
            }
        for (int mi = 0; mi < masses.Length; mi++)
        {
            float target = masses[mi];
            var cell = results.AsSpan(mi * perMass, perMass);
            int stale = 0, timeCap = 0;
            foreach (var r in cell) { if (r.End == "stalemate") stale++; else if (r.End == "time cap") timeCap++; }
            sb.AppendLine($"mass {F(target, "0")}: row = {fa} unit A, column = {fb} unit B; * = a side's mass is >20% off (count rounding)" +
                          (stale + timeCap > 0 ? $"; {stale} stalemates, {timeCap} time caps" : ""));
            foreach (bool margin in new[] { false, true })
            {
                sb.Append(margin ? $"  {"margin",-6}" : $"  {"shareA",-6}");
                foreach (var r in rolesB) sb.Append($" {r.Label,5} ");
                sb.AppendLine(margin ? "  avg   (A's surviving fraction minus B's, -1..+1)" : "  avg   (mean share of surviving mass x hp, 1.00 = A wins)");
                var colSum = new float[nb];
                for (int i = 0; i < na; i++)
                {
                    sb.Append($"  {rolesA[i].Label,-6}");
                    float rowSum = 0;
                    for (int jj = 0; jj < nb; jj++)
                    {
                        float sum = 0;
                        var first = cell[(i * nb + jj) * samples];
                        for (int s = 0; s < samples; s++) { var r = cell[(i * nb + jj) * samples + s]; sum += margin ? r.Margin : r.ShareA; }
                        float mean = sum / samples;
                        rowSum += mean;
                        colSum[jj] += mean;
                        bool off = MathF.Abs(first.StartA / target - 1) > 0.2f || MathF.Abs(first.StartB / target - 1) > 0.2f;
                        sb.Append($" {(margin ? S(mean) : F(mean, "0.00")),5}{(off ? "*" : " ")}");
                    }
                    sb.AppendLine($"  {(margin ? S(rowSum / nb) : F(rowSum / nb, "0.00"))}");
                }
                sb.Append($"  {"B avg",-6}");
                foreach (float cs in colSum) sb.Append($" {(margin ? S(-cs / na) : F(1f - cs / na, "0.00")),5} ");
                sb.AppendLine("   (B's mean vs all A units)");
            }
        }
        Console.Write(sb.ToString());
    }
}

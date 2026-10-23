using System.Numerics;
using Rts.Client.Menus;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.Transport;

namespace Rts.Client;

/// <summary>
/// <c>--scene cybran[:land|:air|:naval|:structures1|:structures2|:structures3|:experimental]</c>: every Cybran blueprint
/// (<c>data/blueprints/units/cybran_*.json</c>, base ids only) laid out once for army 0, grouped (see
/// <see cref="Roster"/>): land units in rows by tech, aircraft parked on two air stagings (the ones that do not fit
/// fly: VTOL transports hover, the rest orbit their spawn point), ships side by side on open deep water next to the
/// water structures, structures in one block per tech (rows by size), experimentals (the Soul Ripper orbits above them). Three army 1 land
/// units next to the land group show the other team colour; every unit holds fire, so nothing fights.
/// Archipelago preset (map 512 unless given), 2 players, no AI, fog off; army 0 has infinite resources (sandbox
/// state). The suffix only picks the default camera (no suffix = the land group). Every group's position and a
/// <c>--camera x,z,dist</c> for it are printed to the console.
/// </summary>
public static class CybranScene
{
    public const string Name = "cybran";
    private static readonly string[] Variants = ["", "land", "air", "naval", "structures1", "structures2", "structures3", "experimental"];

    /// <summary>Layout groups of <see cref="Roster"/>.</summary>
    public enum Group { Land, Air, Naval, NavalStructure, Structure, Experimental }

    /// <summary>Extra air stagings in the air group (one of them is the roster's own): 4 pads each.</summary>
    public const int Stagings = 2;

    /// <summary>Scene defaults for options not given on the command line (call after parsing).</summary>
    public static void ApplyDefaults(ClientOptions o)
    {
        if (!Variants.Contains(Variant(o.Scene!)))
            throw new ArgumentException("unknown scene " + o.Scene + " (cybran[:land|:air|:naval|:structures1|:structures2|:structures3|:experimental])");
        if (!o.PresetGiven) o.Preset = MapPreset.Archipelago;
        if (!o.MapSizeGiven) o.MapSize = 512;
        o.Players = 2;
        o.NoFog = true;
    }

    private static string Variant(string scene) => scene.Contains(':') ? scene[(scene.IndexOf(':') + 1)..] : "";

    /// <summary>
    /// Every Cybran base blueprint (id prefix <c>cybran_</c>, no enhancement variants) with its layout group, sorted
    /// by (tech, id). Experimental first, then: air staging (a structure that docks aircraft) → Air; water
    /// structures → NavalStructure; other structures → Structure; air / water / sub movers → Air / Naval; rest → Land.
    /// </summary>
    public static List<(Group Group, UnitBlueprint Bp)> Roster(BlueprintDb db)
    {
        var list = new List<(Group, UnitBlueprint)>();
        foreach (var bp in db.All)
        {
            if (!bp.Id.StartsWith("cybran_", StringComparison.Ordinal) || bp.Id.Contains('+') || bp.IsVariant) continue;
            Group g;
            if (bp.HasAny(UnitCategory.Experimental)) g = Group.Experimental;
            else if (bp.IsStructure && bp.Cargo != null) g = Group.Air;
            else if (bp.IsStructure) g = bp.HasAny(UnitCategory.Naval) ? Group.NavalStructure : Group.Structure;
            else if (bp.Movement.Layer == MotionLayer.Air) g = Group.Air;
            else if (bp.Movement.Layer is MotionLayer.Water or MotionLayer.Sub) g = Group.Naval;
            else g = Group.Land;
            list.Add((g, bp));
        }
        list.Sort((a, b) => a.Item2.Tech != b.Item2.Tech ? a.Item2.Tech.CompareTo(b.Item2.Tech) : string.CompareOrdinal(a.Item2.Id, b.Item2.Id));
        return list;
    }

    private readonly record struct Item(UnitBlueprint Bp, Vector2 Off, float Heading, int Row);

    /// <summary>Packs items left to right (x) in rows (z), a new row when the width runs out or the tech changes.</summary>
    private static List<Item> Pack(IEnumerable<UnitBlueprint> bps, float maxWidth, float gap, float heading, out Vector2 half,
                                   out List<(int Tech, Vector2 Center)> rows)
    {
        var items = new List<Item>();
        rows = [];
        var rowDepths = new List<float>();
        var rowWidths = new List<float>();
        int tech = -1;
        foreach (var bp in bps)
        {
            float s = MathF.Max(bp.Visual.Size.X, bp.Visual.Size.Z) + gap;
            int r = rows.Count - 1;
            if (r < 0 || bp.Tech != tech || rowWidths[r] + s > maxWidth)
            {
                tech = bp.Tech;
                rows.Add((tech, Vector2.Zero));
                rowDepths.Add(0);
                rowWidths.Add(0);
                r++;
            }
            items.Add(new Item(bp, new Vector2(rowWidths[r] + s * 0.5f, 0), heading, r));
            rowWidths[r] += s;
            rowDepths[r] = MathF.Max(rowDepths[r], s);
        }
        // Rows centred in x, item centres at their row's mid-depth; the whole block centred on (0, 0).
        float total = rowDepths.Sum(), width = rowWidths.Count > 0 ? rowWidths.Max() : 0;
        var rowZ = new float[rows.Count];
        float zAcc = -total * 0.5f;
        for (int r = 0; r < rows.Count; r++) { rowZ[r] = zAcc + rowDepths[r] * 0.5f; zAcc += rowDepths[r]; }
        for (int k = 0; k < items.Count; k++)
        {
            var it = items[k];
            items[k] = it with { Off = new Vector2(it.Off.X - rowWidths[it.Row] * 0.5f, rowZ[it.Row]) };
        }
        for (int r = 0; r < rows.Count; r++) rows[r] = (rows[r].Tech, new Vector2(0, rowZ[r]));
        half = new Vector2(width * 0.5f, total * 0.5f);
        return items;
    }

    /// <summary>Spawns the scene into a freshly created skirmish (before the runner starts) and sets
    /// <see cref="GameConfig.SceneCamera"/>.</summary>
    public static void Setup(Simulation sim, GameConfig cfg)
    {
        var w = sim.World;
        var t = w.Terrain;
        var db = w.Blueprints;
        string variant = Variant(cfg.Scene!);
        foreach (var a in w.Armies) a.IsAI = false;
        SandboxSystem.Enable(w).InfiniteResources[0] = true;
        var roster = Roster(db);
        IEnumerable<UnitBlueprint> Of(Group g) => roster.Where(r => r.Group == g).Select(r => r.Bp);
        float face = MathF.PI * 0.5f + 0.55f; // towards the default camera (+z), turned for a 3/4 view
        var spawned = new List<UnitHandle>[2] { [], [] };
        var cmds = new List<Command>();
        UnitHandle Spawn(UnitBlueprint bp, int army, Vector2 p, float heading)
        {
            var h = w.SpawnUnit(bp.Index, army, t.Clamp(p, 4f), heading);
            if (!h.IsNone) spawned[army].Add(h);
            return h;
        }
        var taken = new List<(Vector2 Min, Vector2 Max)>();
        var cams = new List<(string Name, Vector2 At, float Dist)>();
        float Dist(Vector2 half) => Math.Clamp(1.5f * MathF.Max(half.X, half.Y * 1.4f) + 10f, 18f, 150f);

        // 1) Naval: ships side by side (heading east, slightly turned to the camera) on open deep water nearest the
        //    map centre, the water structures packed in a block next to them.
        var mid = new Vector2(t.Width * 0.5f, t.Height * 0.5f);
        var ships = Of(Group.Naval).OrderByDescending(b => b.Visual.Size.Z).ToList();
        float shipLen = ships.Count > 0 ? ships.Max(b => b.Visual.Size.Z) : 0, shipsDepth = ships.Sum(b => b.Visual.Size.X + 4f);
        var shipHalf = new Vector2(shipLen * 0.5f + 3f, shipsDepth * 0.5f + 3f);
        var navS = Pack(Of(Group.NavalStructure), 34f, 4f, 0f, out var navSHalf, out _);
        var navHalf = new Vector2(shipHalf.X + navSHalf.X + 2f, MathF.Max(shipHalf.Y, navSHalf.Y + 2f));
        var navalAt = FindRect(t, navHalf, mid, taken, Deep);
        if (navalAt is Vector2 nc)
        {
            float zs = nc.Y - shipHalf.Y + 3f, xs = nc.X - navHalf.X + shipHalf.X;
            foreach (var bp in ships)
            {
                zs += bp.Visual.Size.X * 0.5f + 2f;
                var h = Spawn(bp, 0, new Vector2(xs, zs), 0.3f);
                if (!h.IsNone && bp.Movement.Layer == MotionLayer.Sub) cmds.Add(new SetSubmergedCommand(0, [h], false));
                zs += bp.Visual.Size.X * 0.5f + 2f;
            }
            var sc = new Vector2(nc.X + navHalf.X - navSHalf.X - 1f, nc.Y);
            foreach (var it in navS) Spawn(it.Bp, 0, Snap(sc + it.Off), it.Heading);
            cams.Add(("naval", nc, Dist(navHalf)));
        }
        else Console.WriteLine("cybran scene: no open deep water for the naval group (try --preset archipelago)");
        var anchor = navalAt ?? mid;

        // 2) Land groups, each on the nearest free dry land rectangle to the naval group.
        void LandGroup(string name, Vector2 half, Action<Vector2> spawn)
        {
            var at = FindRect(t, half + new Vector2(2, 2), anchor, taken, Land) ?? anchor;
            spawn(at);
            cams.Add((name, at, Dist(half)));
        }

        var land = Pack(Of(Group.Land), 30f, 2.6f, face, out var landHalf, out _);
        var enemyIds = new[] { "cybran_t1_light_bot", "cybran_t2_heavy_tank", "cybran_t3_assault_bot" };
        LandGroup("land", landHalf + new Vector2(0, 3f), c =>
        {
            foreach (var it in land) Spawn(it.Bp, 0, c + it.Off - new Vector2(0, 3f), it.Heading);
            for (int k = 0; k < enemyIds.Length; k++)
                if (db.TryGet(enemyIds[k], out var e)) Spawn(e, 1, c + new Vector2((k - 1) * 4f, landHalf.Y + 1.5f), face);
        });

        var exps = Of(Group.Experimental).ToList();
        float expW = exps.Sum(b => MathF.Max(b.Visual.Size.X, b.Visual.Size.Z) + 6f);
        var expHalf = new Vector2(expW * 0.5f, exps.Count > 0 ? exps.Max(b => b.Visual.Size.Z) * 0.5f + 3f : 4f);
        LandGroup("experimental", expHalf, c =>
        {
            float x = c.X - expHalf.X;
            foreach (var bp in exps)
            {
                float s = MathF.Max(bp.Visual.Size.X, bp.Visual.Size.Z) + 6f;
                Spawn(bp, 0, new Vector2(x + s * 0.5f, c.Y), face);
                x += s;
            }
        });

        // Air: aircraft docked on the air stagings (no fuel: they stay to refuel), the rest flies in front of them.
        var air = Of(Group.Air).ToList();
        var staging = air.FirstOrDefault(b => b.IsStructure);
        var planes = air.Where(b => !b.IsStructure).ToList();
        var board = new List<(UnitHandle Carrier, UnitHandle Plane)>();
        var flyers = new List<UnitBlueprint>();
        var airHalf = new Vector2(Stagings * 5.5f + 4f, 12f);
        LandGroup("air", airHalf, c =>
        {
            var pads = new List<UnitHandle>();
            if (staging != null)
                for (int k = 0; k < Stagings; k++)
                {
                    var pad = Spawn(staging, 0, Snap(c + new Vector2((k - (Stagings - 1) * 0.5f) * 11f, -5f)), 0);
                    if (!pad.IsNone) pads.Add(pad);
                }
            int padSlot = 0;
            foreach (var bp in planes)
            {
                // Big aircraft (transports, strategic bomber) fly; small ones dock while pads are left.
                bool fits = !bp.IsVtol && padSlot < pads.Count * 4 && MathF.Max(bp.Visual.Size.X, bp.Visual.Size.Z) <= 3f;
                if (!fits) { flyers.Add(bp); continue; }
                var carrier = pads[padSlot / 4];
                board.Add((carrier, Spawn(bp, 0, w.Units.Pos[carrier.Index], 0)));
                padSlot++;
            }
            float x = c.X - (flyers.Count - 1) * 4.5f;
            foreach (var bp in flyers) { Spawn(bp, 0, new Vector2(x, c.Y + 6f), face); x += 9f; }
        });

        // Structures: one block per tech, rows by size.
        foreach (var tech in Of(Group.Structure).Select(b => b.Tech).Distinct().ToList())
        {
            var st = Pack(Of(Group.Structure).Where(b => b.Tech == tech).OrderBy(b => b.Visual.Size.X), 34f, 2.5f, 0f, out var stHalf, out _);
            LandGroup($"structures{tech}", stHalf, c => { foreach (var it in st) Spawn(it.Bp, 0, Snap(c + it.Off), it.Heading); });
        }

        // One tick so every unit is alive, then dock the aircraft; everybody holds fire (no fights between the groups).
        sim.Step();
        foreach (var (carrier, plane) in board)
        {
            if (carrier.IsNone || plane.IsNone || !TransportSystem.CanCarry(w, carrier.Index, plane.Index)) continue;
            TransportSystem.Board(w, carrier.Index, plane.Index);
            w.Cargo.Fuel[plane.Index] = 0;
            w.Units.Health[plane.Index] = w.BpOf(plane.Index).MaxHealth * 0.2f; // repairs for ~16 s: stays parked on the pad
        }
        for (int a = 0; a < 2 && a < w.Armies.Length; a++)
            if (spawned[a].Count > 0) cmds.Add(new SetFireStateCommand(a, spawned[a].ToArray(), FireState.HoldFire));
        sim.Step(cmds);
        for (int k = 0; k < 20; k++) sim.Step();

        // Default camera: the group named by the suffix; no suffix = the land group (the groups are spread over the
        // islands, an overview of all of them is too far away to see the models).
        var cam = new Vector3(mid.X, mid.Y + 8f, 80f);
        string want = variant == "" ? "land" : variant;
        foreach (var (name, at, dist) in cams)
        {
            Console.WriteLine($"cybran scene: {name} at {at.X:0},{at.Y:0} (--camera {at.X:0},{at.Y + 2:0},{dist:0})");
            if (name == want) cam = new Vector3(at.X, at.Y + 2f, dist);
        }
        if (flyers.Count > 0) Console.WriteLine($"cybran scene: flying (not docked): {string.Join(", ", flyers.Select(b => b.Id))}");
        cfg.SceneCamera = cam;
        Console.WriteLine($"cybran scene '{(variant == "" ? "default" : variant)}': {roster.Count} blueprints; default camera {cam.X:0},{cam.Y:0},{cam.Z:0}");
    }

    private static Vector2 Snap(Vector2 q) => new(MathF.Round(q.X), MathF.Round(q.Y));

    private static bool Land(Terrain t, Vector2 p) =>
        t.GetHeight(p) > t.WaterLevel + 0.5f && t.CanTraverse(MotionLayer.Land, (int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));

    private static bool Deep(Terrain t, Vector2 p) => t.CanTraverse(MotionLayer.Water, (int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));

    /// <summary>
    /// Centre of a rectangle (half extents <paramref name="half"/>) whose samples (step 3) all pass
    /// <paramref name="ok"/> (else the best-scoring one), not overlapping <paramref name="taken"/> (6 margin), nearest
    /// to <paramref name="near"/>; grid scan step 4. Adds the rectangle to <paramref name="taken"/>. Null if no
    /// candidate has 90% of its samples passing.
    /// </summary>
    private static Vector2? FindRect(Terrain t, Vector2 half, Vector2 near, List<(Vector2 Min, Vector2 Max)> taken, Func<Terrain, Vector2, bool> ok)
    {
        var samples = new List<Vector2>();
        for (float x = -half.X; x <= half.X + 0.01f; x += 3) for (float z = -half.Y; z <= half.Y + 0.01f; z += 3) samples.Add(new Vector2(x, z));
        Vector2? best = null;
        int bestMiss = int.MaxValue;
        float bestD = float.MaxValue;
        int allowed = samples.Count / 10;
        for (float cz = half.Y + 6; cz < t.Height - half.Y - 6; cz += 4)
        for (float cx = half.X + 6; cx < t.Width - half.X - 6; cx += 4)
        {
            var c = new Vector2(cx, cz);
            bool overlap = false;
            foreach (var (mn, mx) in taken)
                if (c.X + half.X + 6 > mn.X && c.X - half.X - 6 < mx.X && c.Y + half.Y + 6 > mn.Y && c.Y - half.Y - 6 < mx.Y) { overlap = true; break; }
            if (overlap) continue;
            int miss = 0;
            int limit = Math.Min(allowed, bestMiss);
            foreach (var s in samples)
                if (!ok(t, c + s) && ++miss > limit) break;
            if (miss > limit) continue;
            float d = Vector2.DistanceSquared(c, near);
            if (miss < bestMiss || (miss == bestMiss && d < bestD)) { best = c; bestMiss = miss; bestD = d; }
        }
        if (best is Vector2 b) taken.Add((b - half, b + half));
        return best;
    }
}

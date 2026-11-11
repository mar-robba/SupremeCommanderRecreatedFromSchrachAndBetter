using System.Numerics;
using Rts.Client.Menus;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.Transport;
using Rts.Sim.World;

namespace Rts.Client;

/// <summary>
/// <c>--scene units8a[:land|:walls|:naval|:salem]</c>: the units added by wave (8a), laid out for screenshots.
/// <list type="bullet">
/// <item><b>land</b>: UEF mech marines (army 0 and three of army 1 for the other colour) and a T2 stealth field
/// generator with a few UEF tanks inside its field.</item>
/// <item><b>walls</b>: a UEF and a Cybran wall line (army 0, 1x1 sections side by side) with army 0 tanks behind them
/// (hold fire) and army 1 tanks on the far side attack-moving through the line: the walls stop them and their direct
/// shots.</item>
/// <item><b>naval</b>: the Cybran T3 aircraft carrier with Cybran aircraft docked on its deck, the UEF and Cybran
/// strategic missile subs (surfaced), a Salem at sea and the UEF and Cybran T3 sonars.</item>
/// <item><b>salem</b>: a Salem (Cybran T2 destroyer, walks ashore) patrolling on dry land.</item>
/// </list>
/// Archipelago preset (map 512 unless given), 2 players, no AI, fog off; army 0 has infinite resources. Everything
/// but the wall attackers holds fire. The suffix picks the default camera (no suffix = land); every group's position
/// and a <c>--camera x,z,dist</c> for it are printed to the console.
/// </summary>
public static class Units8aScene
{
    public const string Name = "units8a";
    private static readonly string[] Variants = ["", "land", "walls", "naval", "salem"];

    /// <summary>Scene defaults for options not given on the command line (call after parsing).</summary>
    public static void ApplyDefaults(ClientOptions o)
    {
        if (!Variants.Contains(Variant(o.Scene!)))
            throw new ArgumentException("unknown scene " + o.Scene + " (units8a[:land|:walls|:naval|:salem])");
        if (!o.PresetGiven) o.Preset = MapPreset.Archipelago;
        if (!o.MapSizeGiven) o.MapSize = 512;
        o.Players = 2;
        o.NoFog = true;
    }

    private static string Variant(string scene) => scene.Contains(':') ? scene[(scene.IndexOf(':') + 1)..] : "";

    public static void Setup(Simulation sim, GameConfig cfg)
    {
        var w = sim.World;
        var t = w.Terrain;
        var db = w.Blueprints;
        string variant = Variant(cfg.Scene!);
        foreach (var a in w.Armies) a.IsAI = false;
        SandboxSystem.Enable(w).InfiniteResources[0] = true;
        const float face = MathF.PI * 0.5f + 0.55f; // towards the default camera (+z), turned for a 3/4 view
        var hold = new List<UnitHandle>[2] { [], [] };
        var cmds = new List<Command>();
        UnitHandle Spawn(string id, int army, Vector2 p, float heading, bool holdFire = true)
        {
            if (!db.TryGet(id, out var bp)) { Console.WriteLine($"units8a scene: no blueprint {id}"); return UnitHandle.None; }
            if (bp.IsStructure) p = OccupancyGrid.SnapStructure(bp, p);
            var h = w.SpawnUnit(bp.Index, army, t.Clamp(p, 4f), heading);
            if (!h.IsNone && holdFire) hold[army].Add(h);
            return h;
        }
        var taken = new List<(Vector2 Min, Vector2 Max)>();
        var cams = new List<(string Name, Vector2 At, float Dist)>();
        var mid = new Vector2(t.Width * 0.5f, t.Height * 0.5f);

        // Naval: carrier, two missile subs and a Salem side by side (heading ~east), the sonars beside them.
        var board = new List<(UnitHandle Carrier, UnitHandle Plane)>();
        var navalAt = FindRect(t, new Vector2(16f, 15f), mid, taken, Deep);
        if (navalAt is Vector2 nc)
        {
            var carrier = Spawn("cybran_t3_aircraft_carrier", 0, nc + new Vector2(-3f, -9f), 0.3f);
            string[] planes = ["cybran_t1_interceptor", "cybran_t1_interceptor", "cybran_t1_bomber", "cybran_t1_bomber",
                               "cybran_t1_air_scout", "cybran_t2_gunship", "cybran_t2_torpedo_bomber", "cybran_t1_interceptor"];
            if (!carrier.IsNone)
                foreach (var id in planes) board.Add((carrier, Spawn(id, 0, w.Units.Pos[carrier.Index], 0.3f)));
            foreach (var (id, dz) in (ReadOnlySpan<(string, float)>)[("uef_t3_strategic_missile_sub", -1f), ("cybran_t3_strategic_missile_sub", 4.5f)])
            {
                var h = Spawn(id, 0, nc + new Vector2(-3f, dz), 0.3f);
                if (!h.IsNone) cmds.Add(new SetSubmergedCommand(0, [h], false));
            }
            Spawn("cybran_t2_destroyer", 0, nc + new Vector2(-3f, 10f), 0.3f);
            Spawn("uef_t3_sonar", 0, nc + new Vector2(11f, -4f), 0);
            Spawn("cybran_t3_sonar", 0, nc + new Vector2(11f, 4f), 0);
            cams.Add(("naval", nc, 46f));
        }
        else Console.WriteLine("units8a scene: no open deep water for the naval group (try --preset archipelago)");
        var anchor = navalAt ?? mid;
        Vector2 LandRect(Vector2 half) => FindRect(t, half, anchor, taken, Land) ?? anchor;

        // Land: mech marines in rows, the stealth field generator with UEF tanks around it (inside its field).
        var lc = LandRect(new Vector2(14f, 10f));
        for (int k = 0; k < 8; k++) Spawn("uef_t1_mech_marine", 0, lc + new Vector2(-10f + (k % 4) * 1.6f, -2f + (k / 4) * 1.8f), face);
        for (int k = 0; k < 3; k++) Spawn("uef_t1_mech_marine", 1, lc + new Vector2(-9f + k * 1.6f, 4f), face);
        Spawn("uef_t2_stealth_field_generator", 0, lc + new Vector2(5f, -1f), 0);
        foreach (var off in (ReadOnlySpan<Vector2>)[new(1.5f, 3f), new(5f, 4f), new(8.5f, 3f), new(9f, -3f)])
            Spawn("uef_t1_tank", 0, lc + off, face);
        cams.Add(("land", lc, 30f));

        // Walls: UEF line (west half) and Cybran line (east half) along x; defenders on the camera side (+z), attackers
        // beyond, attack-moving through the line.
        var wc = LandRect(new Vector2(20f, 14f));
        int sections = 14;
        for (int k = 0; k < sections; k++)
        {
            Spawn("uef_t1_wall", 0, wc + new Vector2(-sections + 0.5f + k, 0), 0); // one unbroken line: [-14, 0) then [0, 14)
            Spawn("cybran_t1_wall", 0, wc + new Vector2(0.5f + k, 0), 0);
        }
        for (int k = 0; k < 3; k++)
        {
            Spawn("uef_t1_tank", 0, wc + new Vector2(-10f + k * 3f, 5f), -MathF.PI * 0.5f);
            Spawn("cybran_t1_light_bot", 0, wc + new Vector2(5f + k * 3f, 5f), -MathF.PI * 0.5f);
        }
        var attackers = new List<UnitHandle>();
        string[] enemyIds = ["uef_t1_tank", "uef_t2_heavy_tank", "cybran_t2_heavy_tank", "cybran_t1_assault_bot"];
        for (int k = 0; k < 8; k++)
        {
            var h = Spawn(enemyIds[k % enemyIds.Length], 1, wc + new Vector2(-13f + k * 3.6f, -11f), MathF.PI * 0.5f, holdFire: false);
            if (!h.IsNone) attackers.Add(h);
        }
        cams.Add(("walls", wc, 38f));

        // Salem ashore: patrols back and forth on dry land.
        var sc = LandRect(new Vector2(16f, 7f));
        var salem = Spawn("cybran_t2_destroyer", 0, sc - new Vector2(11f, 0), 0);
        cams.Add(("salem", sc, 26f));

        // One tick so every unit is alive, then dock the aircraft, give the orders, set the fire states.
        sim.Step();
        foreach (var (carrier, plane) in board)
            if (!plane.IsNone && TransportSystem.CanCarry(w, carrier.Index, plane.Index)) TransportSystem.Board(w, carrier.Index, plane.Index);
        if (attackers.Count > 0) cmds.Add(new IssueOrderCommand(1, attackers.ToArray(), Order.AttackMoveTo(wc + new Vector2(0, 12f)), false));
        if (!salem.IsNone) cmds.Add(new IssueOrderCommand(0, [salem], Order.PatrolTo(sc + new Vector2(11f, 0)), false));
        for (int a = 0; a < 2 && a < w.Armies.Length; a++)
            if (hold[a].Count > 0) cmds.Add(new SetFireStateCommand(a, hold[a].ToArray(), FireState.HoldFire));
        sim.Step(cmds);
        for (int k = 0; k < 20; k++) sim.Step();

        var cam = new Vector3(lc.X, lc.Y + 2f, 30f);
        string want = variant == "" ? "land" : variant;
        foreach (var (name, at, dist) in cams)
        {
            Console.WriteLine($"units8a scene: {name} at {at.X:0},{at.Y:0} (--camera {at.X:0},{at.Y + 2:0},{dist:0})");
            if (name == want) cam = new Vector3(at.X, at.Y + 2f, dist);
        }
        cfg.SceneCamera = cam;
        Console.WriteLine($"units8a scene '{(variant == "" ? "default" : variant)}': default camera {cam.X:0},{cam.Y:0},{cam.Z:0}");
    }

    // Placement helpers (same rules as CybranScene's).
    private static bool Land(Terrain t, Vector2 p) =>
        t.GetHeight(p) > t.WaterLevel + 0.5f && t.CanTraverse(MotionLayer.Land, (int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));

    private static bool Deep(Terrain t, Vector2 p) => t.CanTraverse(MotionLayer.Water, (int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));

    /// <summary>Centre of a free rectangle (half extents <paramref name="half"/>, 6 margin to <paramref name="taken"/>)
    /// whose samples (step 3) all pass <paramref name="ok"/> (at most 10% may fail), nearest to <paramref name="near"/>;
    /// grid scan step 4. Adds it to <paramref name="taken"/>. Null if none qualifies.</summary>
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
            int miss = 0, limit = Math.Min(allowed, bestMiss);
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

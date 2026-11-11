using System.Numerics;
using Rts.Client.Menus;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Client;

/// <summary>
/// <c>--scene deaths[:air|:naval|:structures|:big|:acu|:warp][:N]</c>: the (9b) death sequences and the ACU warp-in, for
/// screenshots (<see cref="Rendering.DeathAnimator"/>).
/// <list type="bullet">
/// <item><b>air</b>: army 1 aircraft (T1-T3 and a Soul Ripper) patrolling over land.</item>
/// <item><b>naval</b>: army 1 surface ships sailing in deep water.</item>
/// <item><b>structures</b>: army 1 factories, generators and radars.</item>
/// <item><b>big</b>: army 1 land experimentals (Fatboy, Monkeylord).</item>
/// <item><b>acu</b>: a second army 1 commander, far from the rest (its death blast is real).</item>
/// <item><b>warp</b>: no stepping before the start, so both commanders warp in; the camera is on army 0's.</item>
/// </list>
/// Every group except warp is killed together (sandbox Kill command) 1.2 s after the client's first snapshot. The run
/// pauses N ticks after the kill (default per group, 0 = never; warp: N ticks after the start) so a screenshot catches
/// the sequence mid-way. Archipelago preset (map 512 unless given), 2 players, no AI, fog off, victory=sandbox. The
/// groups' positions and a <c>--camera</c> for each are printed; the suffix picks the default camera.
/// </summary>
public static class DeathScene
{
    public const string Name = "deaths";
    private static readonly string[] Variants = ["", "air", "naval", "structures", "big", "acu", "warp"];

    // Client-side scene script (never sim state): the units to kill, at which snapshot tick, when to pause.
    private static UnitHandle[] _victims = [];
    private static int _killAt = -1, _pauseAt = -1;
    private static bool _killed, _paused;

    /// <summary>Scene defaults for options not given on the command line (call after parsing).</summary>
    public static void ApplyDefaults(ClientOptions o)
    {
        var (variant, _) = Parse(o.Scene!);
        if (!Variants.Contains(variant))
            throw new ArgumentException("unknown scene " + o.Scene + " (deaths[:air|:naval|:structures|:big|:acu|:warp][:N])");
        if (!o.PresetGiven) o.Preset = MapPreset.Archipelago;
        if (!o.MapSizeGiven) o.MapSize = 512;
        o.GameOptions ??= "victory=sandbox";
        o.Players = 2;
        o.NoFog = true;
    }

    private static (string Variant, int Pause) Parse(string scene)
    {
        var parts = scene.Split(':');
        string v = parts.Length > 1 && !int.TryParse(parts[1], out _) ? parts[1] : "";
        int pause = -1;
        if (int.TryParse(parts[^1], out int n) && parts.Length > 1) pause = n;
        return (v, pause);
    }

    /// <summary>Ticks after the kill (warp: after the start) at which the run pauses, per group.</summary>
    private static int DefaultPause(string variant) => variant switch
    {
        "air" => 14,
        "naval" => 45,
        "structures" => 13,
        "big" => 24,
        "acu" => 11,
        "warp" => 15,
        _ => 0,
    };

    public static void Setup(Simulation sim, GameConfig cfg)
    {
        var w = sim.World;
        var t = w.Terrain;
        var db = w.Blueprints;
        var (variant, pause) = Parse(cfg.Scene!);
        if (pause < 0) pause = DefaultPause(variant);
        foreach (var a in w.Armies) a.IsAI = false;
        SandboxSystem.Enable(w);
        _victims = [];
        _killed = _paused = false;
        _killAt = _pauseAt = -1;

        if (variant == "warp")
        {
            // Nothing stepped: the first snapshot is at tick 0, so DeathAnimator warps the commanders in.
            var acu = Vector2.Zero;
            foreach (int i in w.Units.AliveList)
                if (w.Units.Army[i] == 0 && w.BpOf(i).Has(UnitCategory.Commander)) { acu = w.Units.Pos[i]; break; }
            cfg.SceneCamera = new Vector3(acu.X, acu.Y + 2f, 22f);
            _pauseAt = pause > 0 ? pause : -1;
            Console.WriteLine($"deaths scene 'warp': army 0 commander at {acu.X:0},{acu.Y:0} (--camera {acu.X:0},{acu.Y + 2:0},22), pause at tick {_pauseAt}");
            return;
        }

        var victims = new List<UnitHandle>();
        var patrols = new List<(UnitHandle H, Vector2 To)>();
        var moves = new List<(UnitHandle H, Vector2 To)>();
        UnitHandle Spawn(string id, Vector2 p, float heading)
        {
            if (!db.TryGet(id, out var bp)) { Console.WriteLine($"deaths scene: no blueprint {id}"); return UnitHandle.None; }
            if (bp.IsStructure) p = OccupancyGrid.SnapStructure(bp, p);
            var h = w.SpawnUnit(bp.Index, 1, t.Clamp(p, 4f), heading);
            if (!h.IsNone) victims.Add(h);
            return h;
        }
        var taken = new List<(Vector2 Min, Vector2 Max)>();
        var cams = new List<(string Name, Vector2 At, float Dist)>();
        var mid = new Vector2(t.Width * 0.5f, t.Height * 0.5f);
        const float face = MathF.PI * 0.5f + 0.55f; // towards the default camera (+z), turned for a 3/4 view

        // Naval: a column of surface ships sailing east in deep water.
        var navalAt = FindRect(t, new Vector2(22f, 12f), mid, taken, Deep);
        if (navalAt is Vector2 nc)
        {
            string[] ships = ["uef_t1_frigate", "cybran_t1_frigate", "uef_t2_destroyer", "cybran_t2_cruiser", "uef_t3_battleship", "cybran_t3_battleship"];
            for (int k = 0; k < ships.Length; k++)
            {
                var at = nc + new Vector2(-14f + (k % 3) * 9f, k < 3 ? -5f : 5f);
                var h = Spawn(ships[k], at, 0);
                if (!h.IsNone) moves.Add((h, at + new Vector2(60f, 0)));
            }
            cams.Add(("naval", nc + new Vector2(8f, 0), 55f));
        }
        else Console.WriteLine("deaths scene: no open deep water for the naval group (try --preset archipelago)");
        var anchor = navalAt ?? mid;
        Vector2 LandRect(Vector2 half) => FindRect(t, half, anchor, taken, Land) ?? anchor;

        // Air: aircraft patrolling back and forth over land, at their flying height by the kill.
        var ac = LandRect(new Vector2(22f, 16f));
        string[] planes = ["uef_t1_interceptor", "uef_t1_bomber", "uef_t2_gunship", "cybran_t2_gunship", "cybran_t1_bomber",
                           "uef_t3_strategic_bomber", "cybran_x_soul_ripper"];
        for (int k = 0; k < planes.Length; k++)
        {
            var at = ac + new Vector2(-16f + (k % 4) * 6f, -8f + (k / 4) * 10f);
            var h = Spawn(planes[k], at, 0);
            if (!h.IsNone) patrols.Add((h, at + new Vector2(18f, 0)));
        }
        cams.Add(("air", ac, 60f));

        // Structures: factories, generators and radars in two rows.
        var sc = LandRect(new Vector2(18f, 12f));
        string[] structures = ["uef_t1_land_factory", "uef_t1_power_generator", "uef_t2_radar", "uef_t3_power_generator",
                               "cybran_t1_air_factory", "cybran_t2_power_generator", "cybran_t1_radar", "cybran_t3_land_factory"];
        for (int k = 0; k < structures.Length; k++)
            Spawn(structures[k], sc + new Vector2(-13.5f + (k % 4) * 9f, k < 4 ? -4.5f : 5f), 0);
        cams.Add(("structures", sc, 45f));

        // Big: two land experimentals.
        var bc = LandRect(new Vector2(18f, 12f));
        Spawn("uef_x_fatboy", bc + new Vector2(-8f, 0), face);
        Spawn("cybran_x_monkeylord", bc + new Vector2(8f, 0), face);
        cams.Add(("big", bc, 45f));

        // ACU: a second commander, well away from the others (its death blast is a small nuke).
        var cc = FindRect(t, new Vector2(8f, 8f), anchor + new Vector2(t.Width * 0.3f, 0), taken, Land) ?? anchor;
        Spawn("cybran_commander", cc, face);
        cams.Add(("acu", cc, 24f));

        // One tick so every unit is alive, then the orders and hold fire; 20 ticks to take off / get under way.
        sim.Step();
        var cmds = new List<Command>();
        foreach (var (h, to) in patrols) cmds.Add(new IssueOrderCommand(1, [h], Order.PatrolTo(to), false));
        foreach (var (h, to) in moves) cmds.Add(new IssueOrderCommand(1, [h], Order.MoveTo(to), false));
        if (victims.Count > 0) cmds.Add(new SetFireStateCommand(1, victims.ToArray(), FireState.HoldFire));
        sim.Step(cmds);
        for (int k = 0; k < 20; k++) sim.Step();

        _victims = victims.ToArray();
        _killAt = (int)w.Tick + 12;
        _pauseAt = pause > 0 ? _killAt + 1 + pause : -1;
        var cam = new Vector3(ac.X, ac.Y + 2f, 60f);
        string want = variant == "" ? "air" : variant;
        foreach (var (name, at, dist) in cams)
        {
            Console.WriteLine($"deaths scene: {name} at {at.X:0},{at.Y:0} (--camera {at.X:0},{at.Y + 2:0},{dist:0})");
            if (name == want) cam = new Vector3(at.X, at.Y + 2f, dist);
        }
        cfg.SceneCamera = cam;
        Console.WriteLine($"deaths scene '{(variant == "" ? "default" : variant)}': {_victims.Length} units killed at tick {_killAt}, pause at tick {_pauseAt}, default camera {cam.X:0},{cam.Y:0},{cam.Z:0}");
    }

    /// <summary>Per frame (EffectsRenderer.Death.cs): sends the kill once the client has seen the units alive, then
    /// pauses the run at the chosen tick. No-op outside the scene.</summary>
    public static void Update(GameContext ctx)
    {
        if (_killAt < 0 && _pauseAt < 0) return;
        int tick = (int)ctx.Snapshots.Current.Tick;
        if (!_killed && _killAt >= 0 && tick >= _killAt)
        {
            _killed = true;
            ctx.Runner.Enqueue(new SandboxUnitActionCommand(1, _victims, SandboxUnitAction.Kill, 0, -1));
        }
        if (!_paused && _pauseAt >= 0 && tick >= _pauseAt)
        {
            _paused = true;
            ctx.Runner.Paused = true;
            Console.WriteLine($"deaths scene: paused at tick {tick}");
        }
        if ((_killed || _killAt < 0) && (_paused || _pauseAt < 0)) _killAt = _pauseAt = -1;
    }

    // Placement helpers (same rules as Units8aScene's).
    private static bool Land(Terrain t, Vector2 p) =>
        t.GetHeight(p) > t.WaterLevel + 0.5f && t.CanTraverse(MotionLayer.Land, (int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));

    private static bool Deep(Terrain t, Vector2 p) => t.CanTraverse(MotionLayer.Water, (int)MathF.Floor(p.X), (int)MathF.Floor(p.Y))
        && t.WaterLevel - t.GetHeight(p) > 2f;

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

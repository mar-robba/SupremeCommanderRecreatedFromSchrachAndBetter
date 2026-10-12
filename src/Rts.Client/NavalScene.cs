using System.Numerics;
using Rts.Client.Menus;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Naval;
using Rts.Sim.Orders;

namespace Rts.Client;

/// <summary>
/// <c>--scene naval</c>: screenshot/test scene for the navy. Archipelago preset (map 256, seed 42 unless given),
/// fog off, both armies scripted (no skirmish AI). Per army: a T1 naval factory on the nearest coastal site with
/// frigates and subs queued, and a fleet (battleship, cruisers, destroyers, frigates, subs, two torpedo bombers)
/// placed on open deep water between the starts, close enough to fight at once (torpedoes in flight after a few
/// seconds). Army 0 also gets a sub near the middle of the battle (close-ups of a submerged sub). The default
/// camera frames the battle; the console prints the positions for <c>--camera x,z,dist</c>
/// (e.g. the sub or the naval factory; select the factory with <c>--select uef_t1_naval_factory</c>).
/// </summary>
public static class NavalScene
{
    public const string Name = "naval";

    /// <summary>Scene defaults for options not given on the command line (call after parsing).</summary>
    public static void ApplyDefaults(ClientOptions o)
    {
        if (o.Scene != Name) throw new ArgumentException("unknown scene " + o.Scene + " (naval)");
        if (!o.PresetGiven) o.Preset = MapPreset.Archipelago;
        if (!o.MapSizeGiven) o.MapSize = 256;
        o.Players = 2;
        o.NoFog = true;
    }

    /// <summary>Before the skirmish is created: both armies are scripted (the land AI would pull the fleets away).</summary>
    public static void Configure(GameConfig cfg)
    {
        foreach (var s in cfg.Slots)
            if (s.Kind == SlotKind.AI) s.Kind = SlotKind.Human;
    }

    /// <summary>Spawns the scene into a freshly created skirmish (before the runner starts) and sets
    /// <see cref="GameConfig.SceneCamera"/>.</summary>
    public static void Spawn(Simulation sim, GameConfig cfg)
    {
        var w = sim.World;
        var t = w.Terrain;
        var db = w.Blueprints;
        var cmds = new List<Command>();
        var s0 = t.StartPositions[0];
        var s1 = t.StartPositions[1 % t.StartPositions.Length];

        // Naval factories on the coast nearest to each start, ships queued.
        int nf = db.IndexOf("uef_t1_naval_factory");
        for (int a = 0; a < Math.Min(2, w.Armies.Length); a++)
        {
            var sp = a == 0 ? s0 : s1;
            if (nf < 0 || !NavalRules.FindSite(w.Occupancy, t, db[nf], sp, 160f, out var site)) continue;
            var h = w.SpawnUnit(nf, a, site, 0);
            if (h.IsNone) continue;
            var f = new[] { h };
            cmds.Add(new FactoryEnqueueCommand(a, f, db.IndexOf("uef_t1_frigate"), 3));
            cmds.Add(new FactoryEnqueueCommand(a, f, db.IndexOf("uef_t1_sub"), 2));
            cmds.Add(new FactoryEnqueueCommand(a, f, db.IndexOf("uef_t1_frigate"), 1));
            Console.WriteLine($"naval scene: army {a} naval factory at {site.X:0},{site.Y:0}");
        }

        // The battle: open deep water near the middle, fleets on either side along the best open axis.
        var mid = (s0 + s1) * 0.5f;
        var center = FindOpenWater(t, mid, 16f) ?? FindOpenWater(t, mid, 10f) ?? mid;
        var axis0 = s1 - s0;
        axis0 = axis0.LengthSquared() > 1 ? Vector2.Normalize(axis0) : Vector2.UnitX;
        var axis = axis0;
        int bestScore = -1;
        for (int r = 0; r < 8; r++)
        {
            var dir = DMath.Dir(DMath.HeadingOf(axis0) + r * MathF.PI / 8f);
            int score = 0;
            for (float d = -34; d <= 34; d += 2)
            for (float s = -10; s <= 10; s += 5)
                if (Deep(t, center + dir * d + new Vector2(-dir.Y, dir.X) * s)) score++;
            if (score > bestScore) { bestScore = score; axis = dir; }
        }
        var perp = new Vector2(-axis.Y, axis.X);
        var placed = new List<(Vector2 p, float r)>();
        const float half = 17f;
        var fleetCenters = new Vector2[2];
        for (int a = 0; a < 2; a++) fleetCenters[a] = center + axis * (a == 0 ? -half : half);

        for (int a = 0; a < Math.Min(2, w.Armies.Length); a++)
        {
            var toward = a == 0 ? axis : -axis; // towards the enemy
            var fc = fleetCenters[a];
            float heading = DMath.HeadingOf(toward);
            var units = new List<UnitHandle>();
            var movers = new List<UnitHandle>();
            void Row(string id, int n, float ahead, float spacing, bool strafe = false)
            {
                int bp = db.IndexOf(id);
                if (bp < 0) return;
                for (int i = 0; i < n; i++)
                {
                    var want = fc + toward * ahead + perp * ((i - (n - 1) * 0.5f) * spacing);
                    if (FindFree(t, want, db[bp], placed) is not Vector2 p) continue;
                    var h = w.SpawnUnit(bp, a, p, heading);
                    if (!h.IsNone) (strafe ? movers : units).Add(h);
                }
            }
            Row("uef_t1_sub", 3, 9f, 7f);
            Row("uef_t1_frigate", 4, 5f, 5f, strafe: true);
            Row("uef_t2_destroyer", 3, -1f, 7f);
            Row("uef_t2_cruiser", 2, -10f, 10f);
            Row("uef_t3_battleship", 1, -20f, 0f, strafe: true);
            // Torpedo bombers come in from behind the fleet.
            int tb = db.IndexOf("uef_t2_torpedo_bomber");
            if (tb >= 0)
                for (int i = 0; i < 2; i++)
                {
                    var h = w.SpawnUnit(tb, a, t.Clamp(fc - toward * 26f + perp * (i * 5f - 2.5f), 4f), heading);
                    if (!h.IsNone) units.Add(h);
                }
            cmds.Add(new IssueOrderCommand(a, units.ToArray(), Order.AttackMoveTo(fleetCenters[1 - a]), false));
            // Frigates and the battleship steam sideways along the front (firing on the move: wakes).
            float side = a == 0 ? 1f : -1f;
            var strafeTo = fc + perp * (side * 30f);
            for (float d = 30f; d > 8f && !Deep(t, strafeTo); d -= 4f) strafeTo = fc + perp * (side * d);
            if (movers.Count > 0) cmds.Add(new IssueOrderCommand(a, movers.ToArray(), Order.MoveTo(strafeTo), false));
        }

        // Army 0: a sub near the middle of the battle (close-up subject), holding its ground.
        int subBp = db.IndexOf("uef_t1_sub");
        if (subBp >= 0 && FindFree(t, center - axis * 5f + perp * 9f, db[subBp], placed) is Vector2 subPos)
        {
            var h = w.SpawnUnit(subBp, 0, subPos, DMath.HeadingOf(perp));
            if (!h.IsNone) cmds.Add(new IssueOrderCommand(0, [h], Order.MoveTo(subPos + perp * 4f), false));
            Console.WriteLine($"naval scene: close-up sub at {subPos.X:0},{subPos.Y:0} (e.g. --camera {subPos.X:0},{subPos.Y + 2:0},16)");
        }
        cfg.SceneCamera = new Vector3(center.X, center.Y + 3f, 58f);
        Console.WriteLine($"naval scene: battle at {center.X:0},{center.Y:0} (default camera {center.X:0},{center.Y + 3:0},58)");
        sim.Step(cmds);
    }

    private static bool Deep(Terrain t, Vector2 p) => t.CanTraverse(MotionLayer.Water, (int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));

    /// <summary>Nearest point (ring scan, step 2) whose surroundings within r are all deep water.</summary>
    private static Vector2? FindOpenWater(Terrain t, Vector2 near, float r)
    {
        for (int ring = 0; ring <= 60; ring++)
        {
            Vector2? best = null;
            float bestD = float.MaxValue;
            for (int dz = -ring; dz <= ring; dz++)
            for (int dx = -ring; dx <= ring; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue;
                var c = near + new Vector2(dx, dz) * 2f;
                if (!Open(t, c, r)) continue;
                float d = Vector2.DistanceSquared(c, near);
                if (d < bestD) { bestD = d; best = c; }
            }
            if (best != null) return best;
        }
        return null;
    }

    private static bool Open(Terrain t, Vector2 c, float r)
    {
        if (!Deep(t, c)) return false;
        for (int k = 0; k < 16; k++)
        {
            var d = DMath.Dir(k * MathF.Tau / 16f);
            if (!Deep(t, c + d * r) || !Deep(t, c + d * r * 0.5f)) return false;
        }
        return true;
    }

    /// <summary>Deep-water spot near `want` not overlapping the ships already placed (spiral search).</summary>
    private static Vector2? FindFree(Terrain t, Vector2 want, UnitBlueprint bp, List<(Vector2 p, float r)> placed)
    {
        float r = MathF.Max(bp.Radius, bp.Visual.Size.X * 0.5f) + 0.4f;
        for (int ring = 0; ring <= 14; ring++)
        for (int k = 0; k < Math.Max(1, ring * 6); k++)
        {
            var p = want + (ring == 0 ? Vector2.Zero : DMath.Dir(k * MathF.Tau / (ring * 6)) * ring);
            if (!Deep(t, p)) continue;
            bool ok = true;
            foreach (var (q, qr) in placed)
                if (Vector2.DistanceSquared(p, q) < (r + qr) * (r + qr)) { ok = false; break; }
            if (!ok) continue;
            placed.Add((p, r));
            return p;
        }
        return null;
    }
}

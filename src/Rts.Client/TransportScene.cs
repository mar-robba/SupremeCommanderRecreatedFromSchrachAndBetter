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
/// <c>--scene transport</c>: screenshot/test scene for transports, ferries, air staging and the Atlantis.
/// Archipelago preset (map 256, seed 42 unless given), fog off, both armies scripted. Army 0's start island gets
/// five vignettes (positions printed to the console for <c>--camera x,z,dist</c>):
/// <list type="number">
/// <item>Boarding: a C-6 with two units already clamped under it, four tanks walking up to board (Load).</item>
/// <item>A full C-14 (12 slots) flying with TransportTo to another island.</item>
/// <item>A ferry route: a C-6 with Ferry(beacon, drop to another island) and tanks ordered to the beacon.</item>
/// <item>An Air Staging Facility with four damaged, empty-tanked fighters docked and two more queued (Dock).</item>
/// <item>An Atlantis on deep water off the island with a deck full of aircraft, building interceptors.</item>
/// </list>
/// The default camera frames the island (boarding, C-14 take-off, ferry beacon, staging).
/// </summary>
public static class TransportScene
{
    public const string Name = "transport";

    /// <summary>Scene defaults for options not given on the command line (call after parsing).</summary>
    public static void ApplyDefaults(ClientOptions o)
    {
        if (o.Scene != Name) throw new ArgumentException("unknown scene " + o.Scene + " (transport)");
        if (!o.PresetGiven) o.Preset = MapPreset.Archipelago;
        if (!o.MapSizeGiven) o.MapSize = 256;
        o.Players = 2;
        o.NoFog = true;
    }

    /// <summary>Spawns the scene into a freshly created skirmish (before the runner starts) and sets
    /// <see cref="GameConfig.SceneCamera"/>.</summary>
    public static void Spawn(Simulation sim, GameConfig cfg)
    {
        var w = sim.World;
        var t = w.Terrain;
        var db = w.Blueprints;
        const int A = 0;
        var s0 = t.StartPositions[0];
        var toCenter = new Vector2(t.Width * 0.5f, t.Height * 0.5f) - s0;
        var d = toCenter.LengthSquared() > 1 ? Vector2.Normalize(toCenter) : Vector2.UnitX;
        var p = new Vector2(-d.Y, d.X);
        var home = Island(t, s0);
        bool OnHome(Vector2 q) => home[(int)q.Y * t.Width + (int)q.X];
        bool Away(Vector2 q) => !home[(int)q.Y * t.Width + (int)q.X];
        w.Armies[A].Eco.Mass = 20000;
        w.Armies[A].Eco.Energy = 200000;

        var cmds = new List<Command>();
        var board = new List<(UnitHandle carrier, UnitHandle unit)>();
        UnitHandle Unit(string id, Vector2 pos, float heading) => w.SpawnUnit(db.IndexOf(id), A, t.Clamp(pos, 4f), heading);
        float hd = DMath.HeadingOf(d);

        // Other islands: the C-14's destination and the ferry drop point.
        var other = Find(t, s0 + d * 60f, q => Away(q) && OpenLand(t, q, 4f)) ?? s0 + d * 60f;
        var ferryDrop = Find(t, other + p * 14f, q => Away(q) && OpenLand(t, q, 4f) && Vector2.Distance(q, other) > 8f) ?? other + p * 10f;

        // 1) Boarding: C-6 hovering over open ground, an engineer + mobile AA already aboard, four tanks walk up.
        var boardAt = Find(t, s0 + p * 14f, q => OnHome(q) && OpenLand(t, q, 7f)) ?? s0 + p * 10f;
        var c6 = Unit("uef_t1_transport", boardAt, hd);
        board.Add((c6, Unit("uef_t1_engineer", boardAt, hd)));
        board.Add((c6, Unit("uef_t1_mobile_aa", boardAt, hd)));
        var walkers = new List<UnitHandle>();
        for (int k = 0; k < 4; k++)
        {
            var off = DMath.Dir(hd + MathF.PI * (0.6f + k * 0.27f)) * (6.5f + k * 0.8f);
            walkers.Add(Unit("uef_t1_tank", boardAt + off, DMath.HeadingOf(-off)));
        }
        cmds.Add(new IssueOrderCommand(A, walkers.ToArray(), TransportOrders.Load(c6), false));

        // 2) A full C-14 (12 slots) taking off from the coast towards the other island.
        var liftAt = Find(t, s0 + d * 16f - p * 4f, q => OnHome(q) && OpenLand(t, q, 3f)) ?? s0 + d * 12f;
        var c14 = Unit("uef_t2_transport", liftAt, DMath.HeadingOf(other - liftAt));
        foreach (var id in (ReadOnlySpan<string>)["uef_t2_heavy_tank", "uef_t2_heavy_tank", "uef_t1_tank", "uef_t1_tank",
                     "uef_t1_tank", "uef_t1_tank", "uef_t1_mobile_aa", "uef_t1_mobile_aa", "uef_t1_engineer", "uef_t1_engineer"])
            board.Add((c14, Unit(id, liftAt, 0)));
        cmds.Add(new IssueOrderCommand(A, [c14], TransportOrders.TransportTo(other), false));

        // 3) Ferry route: beacon on the island, drop on the other island; tanks ordered to the beacon.
        var beacon = Find(t, s0 - p * 14f + d * 2f, q => OnHome(q) && OpenLand(t, q, 6f)) ?? s0 - p * 10f;
        var ferry = Unit("uef_t1_transport", beacon + d * 3f, hd);
        cmds.Add(new IssueOrderCommand(A, [ferry], TransportOrders.Ferry(beacon, ferryDrop), false));
        var riders = new List<UnitHandle>();
        for (int k = 0; k < 4; k++) riders.Add(Unit("uef_t1_tank", beacon - d * (7f + (k / 2) * 2.5f) + p * ((k % 2) * 2.6f - 1.3f), hd));
        cmds.Add(new IssueOrderCommand(A, riders.ToArray(), TransportOrders.Ferry(beacon, ferryDrop), false));

        // 4) Air staging: 4 slots docked (damaged, no fuel) + 2 fighters queued.
        int stagingBp = db.IndexOf("uef_t2_air_staging");
        var stagingAt = Find(t, s0 - d * 9f + p * 3f, q =>
        {
            var c = Snap(q);
            return OnHome(c) && OpenLand(t, c, 5f) && SandboxSystem.CanPlaceIgnoringDeposits(w, db[stagingBp], c);
        });
        var staging = stagingAt is Vector2 sp ? w.SpawnUnit(stagingBp, A, Snap(sp), 0) : UnitHandle.None;
        var queued = new List<UnitHandle>();
        if (!staging.IsNone)
        {
            var at = Snap(stagingAt!.Value);
            foreach (var id in (ReadOnlySpan<string>)["uef_t1_interceptor", "uef_t3_air_superiority", "uef_t1_interceptor"])
                board.Add((staging, Unit(id, at, 0)));
            for (int k = 0; k < 2; k++) queued.Add(Unit("uef_t1_interceptor", at + d * 14f + p * (k * 4f - 2f), hd + MathF.PI));
            cmds.Add(new IssueOrderCommand(A, queued.ToArray(), TransportOrders.Dock(staging), false));
        }

        // 5) Atlantis on open deep water off the island, deck full of aircraft, building interceptors.
        var sea = Find(t, s0 + d * 40f - p * 18f, q => OpenWater(t, q, 12f)) ?? Find(t, s0 + d * 40f, q => OpenWater(t, q, 8f));
        var atlantis = UnitHandle.None;
        if (sea is Vector2 seaAt)
        {
            float best = 0;
            int bestScore = -1;
            for (int r = 0; r < 8; r++)
            {
                float h = r * MathF.PI / 8f;
                int score = 0;
                for (float k = -14; k <= 14; k += 2) if (Deep(t, seaAt + DMath.Dir(h) * k)) score++;
                if (score > bestScore) { bestScore = score; best = h; }
            }
            atlantis = w.SpawnUnit(db.IndexOf("uef_x_atlantis"), A, seaAt, best);
            if (!atlantis.IsNone)
            {
                foreach (var id in (ReadOnlySpan<string>)["uef_t1_interceptor", "uef_t1_interceptor", "uef_t3_air_superiority",
                             "uef_t1_interceptor", "uef_t2_gunship", "uef_t3_air_superiority", "uef_t1_bomber", "uef_t1_interceptor",
                             "uef_t2_gunship", "uef_t1_interceptor", "uef_t1_bomber", "uef_t3_air_superiority", "uef_t1_interceptor"])
                    board.Add((atlantis, Unit(id, seaAt, best)));
                cmds.Add(new FactoryEnqueueCommand(A, [atlantis], db.IndexOf("uef_t1_interceptor"), 3));
            }
        }

        // One tick so every spawned unit is alive, then put the cargo aboard (damaged / dry fighters on the staging).
        sim.Step();
        foreach (var (carrier, unit) in board)
        {
            if (carrier.IsNone || unit.IsNone || !TransportSystem.CanCarry(w, carrier.Index, unit.Index)) continue;
            TransportSystem.Board(w, carrier.Index, unit.Index);
            if (carrier == staging)
            {
                var bp = w.BpOf(unit.Index);
                w.Units.Health[unit.Index] = bp.MaxHealth * 0.2f;
                w.Cargo.Fuel[unit.Index] = 0;
            }
        }
        sim.Step(cmds);

        var cam = s0 + d * 6f;
        cfg.SceneCamera = new Vector3(cam.X, cam.Y, 62f);
        Console.WriteLine($"transport scene: (1) boarding C-6 at {boardAt.X:0},{boardAt.Y:0} (e.g. --camera {boardAt.X:0},{boardAt.Y:0},22)");
        Console.WriteLine($"transport scene: (2) loaded C-14 from {liftAt.X:0},{liftAt.Y:0} to {other.X:0},{other.Y:0}");
        Console.WriteLine($"transport scene: (3) ferry beacon {beacon.X:0},{beacon.Y:0} -> drop {ferryDrop.X:0},{ferryDrop.Y:0}");
        if (stagingAt is Vector2 sa) Console.WriteLine($"transport scene: (4) air staging at {Snap(sa).X:0},{Snap(sa).Y:0} (e.g. --camera {Snap(sa).X:0},{Snap(sa).Y:0},18)");
        if (sea is Vector2 sv) Console.WriteLine($"transport scene: (5) Atlantis at {sv.X:0},{sv.Y:0} (e.g. --camera {sv.X:0},{sv.Y:0},34)");
        Console.WriteLine($"transport scene: default camera {cam.X:0},{cam.Y:0},62");
    }

    private static Vector2 Snap(Vector2 q) => new(MathF.Round(q.X), MathF.Round(q.Y));

    private static bool Land(Terrain t, Vector2 q) => t.CanTraverse(MotionLayer.Land, (int)MathF.Floor(q.X), (int)MathF.Floor(q.Y));
    private static bool Deep(Terrain t, Vector2 q) => t.CanTraverse(MotionLayer.Water, (int)MathF.Floor(q.X), (int)MathF.Floor(q.Y));

    /// <summary>q and 16 points on circles of radius r and r/2 around it all satisfy <paramref name="ok"/>.</summary>
    private static bool Around(Vector2 q, float r, Func<Vector2, bool> ok)
    {
        if (!ok(q)) return false;
        for (int k = 0; k < 16; k++)
        {
            var dir = DMath.Dir(k * MathF.Tau / 16f);
            if (!ok(q + dir * r) || !ok(q + dir * r * 0.5f)) return false;
        }
        return true;
    }

    private static bool OpenLand(Terrain t, Vector2 q, float r) => Around(q, r, x => Land(t, x));
    private static bool OpenWater(Terrain t, Vector2 q, float r) => Around(q, r, x => Deep(t, x));

    /// <summary>Nearest point to <paramref name="near"/> (ring scan, step 2, in bounds) satisfying <paramref name="ok"/>.</summary>
    private static Vector2? Find(Terrain t, Vector2 near, Func<Vector2, bool> ok)
    {
        for (int ring = 0; ring <= 70; ring++)
        {
            Vector2? best = null;
            float bestD = float.MaxValue;
            for (int dz = -ring; dz <= ring; dz++)
            for (int dx = -ring; dx <= ring; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue;
                var c = near + new Vector2(dx, dz) * 2f;
                if (c.X < 8 || c.Y < 8 || c.X > t.Width - 8 || c.Y > t.Height - 8 || !ok(c)) continue;
                float dd = Vector2.DistanceSquared(c, near);
                if (dd < bestD) { bestD = dd; best = c; }
            }
            if (best != null) return best;
        }
        return null;
    }

    /// <summary>Land cells 4-connected to <paramref name="from"/> (the start island).</summary>
    private static bool[] Island(Terrain t, Vector2 from)
    {
        int W = t.Width, H = t.Height;
        var mask = new bool[W * H];
        int sx = Math.Clamp((int)from.X, 0, W - 1), sz = Math.Clamp((int)from.Y, 0, H - 1);
        if (!t.CanTraverse(MotionLayer.Land, sx, sz)) return mask;
        var q = new Queue<int>();
        mask[sz * W + sx] = true;
        q.Enqueue(sz * W + sx);
        while (q.Count > 0)
        {
            int c = q.Dequeue(), x = c % W, z = c / W;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
                if (nx < 0 || nz < 0 || nx >= W || nz >= H || mask[nz * W + nx] || !t.CanTraverse(MotionLayer.Land, nx, nz)) continue;
                mask[nz * W + nx] = true;
                q.Enqueue(nz * W + nx);
            }
        }
        return mask;
    }
}

using System.Numerics;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;
using Rts.Client.Menus;

namespace Rts.Client;

/// <summary>
/// <c>--scene experimental[:mavor|:fog]</c>: visual test bed for experimentals and commanders around the map center
/// (plains, map 512 unless given, 2 players, no AI, fog off unless <c>:fog</c>; army 0 has infinite resources via the
/// sandbox state). Army 0: a Fatboy driving east through a few army 1 T1 tanks (crushed or shot) while building
/// 3 T1 tanks, a Mavor firing at an army 1 base ≈200 away (an omni sensor gives the intel), a Quantum Gateway
/// building an SCU next to an SCU and an enhanced SCU, and a row of commanders (plain + three enhancement
/// variants). Army 1's base also holds an enemy Fatboy: it triggers the "Experimental detected" alert at start
/// (with <c>:fog</c> only once it is seen). <c>:mavor</c> pre-steps until a Mavor shell is about to land and aims
/// the camera there. Positions are printed to the console (use them with <c>--camera x,z,dist</c>).
/// </summary>
public static class ExperimentalScene
{
    public const string Name = "experimental";

    /// <summary>Scene defaults for options not given on the command line (call after parsing).</summary>
    public static void ApplyDefaults(ClientOptions o)
    {
        string variant = Variant(o.Scene!);
        if (variant is not ("" or "mavor" or "fog")) throw new ArgumentException("unknown scene " + o.Scene + " (experimental[:mavor|:fog])");
        if (!o.PresetGiven) o.Preset = MapPreset.Plains;
        if (!o.MapSizeGiven) o.MapSize = 512;
        o.Players = 2;
        if (variant != "fog") o.NoFog = true;
    }

    private static string Variant(string scene) => scene.Contains(':') ? scene[(scene.IndexOf(':') + 1)..] : "";

    /// <summary>Spawns the scene into a freshly created skirmish (before the runner starts) and sets
    /// <see cref="GameConfig.SceneCamera"/>.</summary>
    public static void Setup(Simulation sim, GameConfig cfg)
    {
        var w = sim.World;
        var db = w.Blueprints;
        string variant = Variant(cfg.Scene!);
        foreach (var a in w.Armies) a.IsAI = false;
        // Infinite resources for army 0: the Mavor needs 20000 E per shot, the factories build at full speed.
        SandboxSystem.Enable(w).InfiniteResources[0] = true;
        var c = FindLayoutCenter(w.Terrain);
        var cmds = new List<Command>();
        UnitHandle Spawn(string id, int army, Vector2 p, float heading = 0)
        {
            int bp = db.IndexOf(id);
            if (bp < 0) { Console.WriteLine($"experimental scene: unknown blueprint '{id}' (skipped)"); return UnitHandle.None; }
            return w.SpawnUnit(bp, army, w.Terrain.Clamp(p, 4f), heading);
        }
        void Enqueue(UnitHandle f, string id, int n)
        {
            int bp = db.IndexOf(id);
            if (!f.IsNone && bp >= 0) cmds.Add(new FactoryEnqueueCommand(0, [f], bp, n));
        }

        // 1) Fatboy driving east through enemy T1 tanks, building tanks on the move.
        var F0 = c + new Vector2(-60, 40);
        var F1 = c + new Vector2(60, 40);
        var fatboy = Spawn("uef_x_fatboy", 0, F0);
        Enqueue(fatboy, "uef_t1_tank", 3);
        if (!fatboy.IsNone) cmds.Add(new IssueOrderCommand(0, [fatboy], Order.MoveTo(F1), false));
        for (int k = 0; k < 6; k++) Spawn("uef_t1_tank", 1, F0 + new Vector2(14 + (k / 2) * 5f, -2.5f + (k % 2) * 5f), MathF.PI);

        // 2) Mavor shelling an enemy base ≈200 away; an omni sensor (radar 400) provides the intel.
        var M = c + new Vector2(-90, -70);
        var T = c + new Vector2(110, -70);
        var mavor = Spawn("uef_x_mavor", 0, M, 0);
        var omni = M + new Vector2(100, 0);
        Spawn("uef_t3_omni_sensor", 0, omni);
        var factory = Spawn("uef_t1_land_factory", 1, T);
        for (int k = 0; k < 4; k++) Spawn("uef_t1_power_generator", 1, T + new Vector2(-10 + k * 6.5f, 9));
        Spawn("uef_t1_radar", 1, T + new Vector2(10, -8));
        for (int k = 0; k < 3; k++) Spawn("uef_t1_tank", 1, T + new Vector2(-10, -8 + k * 4f), MathF.PI);
        var enemyFatboy = T + new Vector2(5, 28);
        Spawn("uef_x_fatboy", 1, enemyFatboy, MathF.PI);
        if (!mavor.IsNone && !factory.IsNone) cmds.Add(new IssueOrderCommand(0, [mavor], Order.AttackUnit(factory), false));

        // 3) Quantum Gateway building an SCU, next to an SCU and an enhanced SCU.
        var Q = c + new Vector2(-40, 95);
        var gate = Spawn("uef_t3_quantum_gateway", 0, Q);
        Enqueue(gate, "uef_scu", 1);
        Spawn("uef_scu", 0, Q + new Vector2(10, -2), -MathF.PI / 2);
        Spawn("uef_scu+ResourceAllocation+HighExplosiveOrdnance+BubbleShield", 0, Q + new Vector2(16, -2), -MathF.PI / 2);

        // 4) Commanders side by side: plain and three enhancement variants.
        var C0 = c + new Vector2(5, 92);
        string[] commanders =
        [
            "uef_commander",
            "uef_commander+AdvancedEngineering+HeavyAntimatterCannon+PersonalShield",
            "uef_commander+T3Engineering+ZephyrAmplifier+ShieldGeneratorField",
            "uef_commander+ResourceAllocation+TacticalMissilePack",
        ];
        for (int k = 0; k < commanders.Length; k++) Spawn(commanders[k], 0, C0 + new Vector2(k * 9f, 0), -MathF.PI / 2);

        sim.Step(cmds);
        var camera = new Vector3(c.X - 8, c.Y + 60, 135);
        if (variant == "mavor")
        {
            // Until a Mavor shell is coming down close to the ground.
            for (int t = 0; t < 600; t++)
            {
                sim.Step();
                if (ShellAltitude(sim, out float alt, out float vy, out var at) && vy < 0 && alt < 14)
                {
                    camera = new Vector3(at.X, at.Y, 70);
                    break;
                }
            }
        }
        else for (int t = 0; t < 20; t++) sim.Step();
        cfg.SceneCamera = camera;
        Console.WriteLine($"experimental scene '{(variant == "" ? "default" : variant)}': fatboy {F0.X:0},{F0.Y:0} -> {F1.X:0},{F1.Y:0}; " +
                          $"mavor {M.X:0},{M.Y:0} -> enemy base {T.X:0},{T.Y:0} (enemy fatboy {enemyFatboy.X:0},{enemyFatboy.Y:0}, omni {omni.X:0},{omni.Y:0}); " +
                          $"quantum gateway {Q.X:0},{Q.Y:0}; commanders {C0.X:0},{C0.Y:0} .. {C0.X + 27:0},{C0.Y:0}; " +
                          $"default camera {camera.X:0},{camera.Y:0},{camera.Z:0}");
    }

    /// <summary>
    /// Layout origin near the map center (grid search, ±80 in steps of 8) where the Fatboy's path and the
    /// structure sites are dry, passable land (most passable samples wins, ties go to the nearest).
    /// </summary>
    private static Vector2 FindLayoutCenter(Terrain t)
    {
        var mid = new Vector2(t.Width * 0.5f, t.Height * 0.5f);
        var samples = new List<Vector2>();
        for (float x = -64; x <= 64; x += 4)
        for (float z = -6; z <= 6; z += 6) samples.Add(new Vector2(x, 40 + z));
        void Area(Vector2 o, float r)
        {
            for (float x = -r; x <= r; x += 3)
            for (float z = -r; z <= r; z += 3) samples.Add(o + new Vector2(x, z));
        }
        Area(new Vector2(-90, -70), 7);
        Area(new Vector2(10, -70), 3);
        Area(new Vector2(110, -70), 14);
        Area(new Vector2(115, -42), 6);
        Area(new Vector2(-30, 95), 12);
        Area(new Vector2(18, 92), 16);
        var best = mid;
        int bestScore = -1;
        float bestD = float.MaxValue;
        for (float dz = -80; dz <= 80; dz += 8)
        for (float dx = -80; dx <= 80; dx += 8)
        {
            var o = mid + new Vector2(dx, dz);
            int score = 0;
            foreach (var s in samples)
            {
                var p = o + s;
                if (p.X >= 4 && p.Y >= 4 && p.X < t.Width - 4 && p.Y < t.Height - 4 && t.GetHeight(p) > t.WaterLevel + 0.5f
                    && t.CanTraverse(MotionLayer.Land, (int)p.X, (int)p.Y)) score++;
            }
            float d = dx * dx + dz * dz;
            if (score > bestScore || (score == bestScore && d < bestD)) { best = o; bestScore = score; bestD = d; }
        }
        return best;
    }

    /// <summary>Altitude, vertical speed and ground position of the first Mavor shell in flight.</summary>
    private static bool ShellAltitude(Simulation sim, out float alt, out float vy, out Vector2 at)
    {
        var w = sim.World;
        var P = w.Projectiles;
        int bp = w.Blueprints.IndexOf("uef_x_mavor");
        for (int k = 0; k < P.AliveCount; k++)
        {
            int p = P.AliveList[k];
            if (P.SourceBp[p] != bp) continue;
            var pos = P.Pos[p];
            at = new Vector2(pos.X, pos.Z);
            alt = pos.Y - w.Terrain.GetSurfaceHeight(at);
            vy = P.Vel[p].Y;
            return true;
        }
        alt = vy = 0;
        at = default;
        return false;
    }
}

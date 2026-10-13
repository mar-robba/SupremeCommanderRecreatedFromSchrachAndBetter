using System.Numerics;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Strategic;
using Rts.Sim.World;

namespace Rts.Client;

/// <summary>
/// <c>--scene strategic[:shields|:tml|:nuke]</c>: a visual test bed for shields and strategic weapons around the map
/// center (AI disabled). Army 0: a T2 shield generator + mobile shield under fire from army 1 artillery and tanks,
/// two TMLs firing at an army 1 base protected by a TMD, a nuke silo launching at an army 1 cluster (with a shield,
/// which the nuke knocks down). The variant pre-steps the sim so the screenshot catches the interesting moment:
/// shields = domes being hit, tml = missiles in flight, nuke = a few ticks before the detonation.
/// Each variant frames its action with a default camera (an explicit --camera wins); positions are printed to the
/// console (use them with --camera).
/// </summary>
public static class StrategicScene
{
    /// <summary>Builds the scene; returns the default camera (x, z, distance) framing the variant's action.</summary>
    public static Vector3 Setup(Simulation sim, string scene)
    {
        var w = sim.World;
        var db = w.Blueprints;
        string variant = scene.Contains(':') ? scene[(scene.IndexOf(':') + 1)..] : "shields";
        foreach (var a in w.Armies) a.IsAI = false;
        var c = new Vector2(w.Terrain.Width * 0.5f, w.Terrain.Height * 0.5f);
        var cmds = new List<Command>();
        UnitHandle Spawn(string id, int army, Vector2 p, float heading = 0) =>
            w.SpawnUnit(db.IndexOf(id), army, w.Terrain.Clamp(p, 4f), heading);

        // Power for shield upkeep (both armies).
        for (int a = 0; a < 2; a++)
        {
            var sp = w.Terrain.StartPositions[a % w.Terrain.StartPositions.Length];
            // Silos building missiles draw a lot of energy (a nuke: 1000 E/s): 80 T1 generators.
            for (int k = 0; k < 80; k++) Spawn("uef_t1_power_generator", a, sp + new Vector2(-15 + (k % 10) * 3f, 10 + (k / 10) * 3f));
            Spawn("uef_t1_energy_storage", a, sp + new Vector2(14, 10));
            w.Armies[a].Eco.Energy = 4000;
            w.Armies[a].Eco.Mass = 2000;
        }

        // 1) Shields under fire.
        var S = c + new Vector2(-45, -35);
        var gen = Spawn("uef_t2_shield_generator", 0, S);
        for (int k = 0; k < 3; k++) Spawn("uef_t1_power_generator", 0, S + new Vector2(-6 + k * 6f, 6));
        Spawn("uef_t1_point_defense", 0, S + new Vector2(5, -5));
        var arty = new List<UnitHandle>();
        for (int k = 0; k < 6; k++) arty.Add(Spawn("uef_t1_artillery", 1, S + new Vector2(-10 + k * 4f, 27), -MathF.PI / 2));
        arty.Add(Spawn("uef_t2_artillery_installation", 1, S + new Vector2(-70, 10)));
        cmds.Add(new IssueOrderCommand(1, arty.ToArray(), Order.AttackUnit(gen), false));
        var M = S + new Vector2(28, -8);
        Spawn("uef_t2_mobile_shield", 0, M);
        for (int k = 0; k < 4; k++) Spawn("uef_t1_tank", 0, M + new Vector2(-3 + (k % 2) * 6f, -3 + (k / 2) * 6f));
        var tanks = new List<UnitHandle>();
        for (int k = 0; k < 5; k++) tanks.Add(Spawn("uef_t1_tank", 1, M + new Vector2(24, -6 + k * 3f), MathF.PI));
        cmds.Add(new IssueOrderCommand(1, tanks.ToArray(), Order.AttackMoveTo(M), false));

        // 2) Tactical missiles vs a TMD-defended base.
        var T0 = c + new Vector2(-20, 55);
        var TT = c + new Vector2(55, 45);
        var tml = new[] { Spawn("uef_t2_tactical_missile_launcher", 0, T0), Spawn("uef_t2_tactical_missile_launcher", 0, T0 + new Vector2(0, 8)) };
        for (int k = 0; k < 4; k++) Spawn("uef_t1_power_generator", 1, TT + new Vector2(-4 + (k % 2) * 8f, -4 + (k / 2) * 8f));
        Spawn("uef_t2_tactical_missile_defense", 1, TT + new Vector2(10, 0));

        // 3) Nuke.
        var N0 = c + new Vector2(-70, 70);
        var NT = c + new Vector2(45, -45);
        var silo = Spawn("uef_t3_strategic_missile_launcher", 0, N0);
        Spawn("uef_t2_shield_generator", 1, NT + new Vector2(-6, 4));
        for (int k = 0; k < 6; k++) Spawn("uef_t1_tank", 1, NT + new Vector2(-8 + (k % 3) * 8f, -8 + (k / 3) * 8f));
        for (int k = 0; k < 3; k++) Spawn("uef_t1_power_generator", 1, NT + new Vector2(8, -6 + k * 6f));

        sim.Step(cmds);
        cmds.Clear();
        // Let shields boot (25% of the recharge time) and artillery start firing.
        for (int t = 0; t < 70; t++) sim.Step();
        foreach (var h in tml) SiloSystem.SetStock(w, h.Index, 4);
        SiloSystem.SetStock(w, silo.Index, 1);
        for (int k = 0; k < 2; k++)
        {
            cmds.Add(new IssueOrderCommand(0, [tml[k]], Order.LaunchAt(TT + new Vector2(-3 + 6 * k, 2)), true));
            cmds.Add(new IssueOrderCommand(0, [tml[k]], Order.LaunchAt(TT + new Vector2(3 - 6 * k, -4)), true));
        }
        if (variant == "nuke") cmds.Add(new IssueOrderCommand(0, [silo], Order.LaunchAt(NT), false));
        sim.Step(cmds);

        switch (variant)
        {
            case "tml":
                for (int t = 0; t < 32; t++) sim.Step();
                break;
            case "nuke":
                // Until the warhead is close to the ground on its way down.
                for (int t = 0; t < 400; t++)
                {
                    sim.Step();
                    if (NukeAltitude(sim, out float alt, out float vy) && vy < 0 && alt < 12) break;
                }
                break;
            default:
                for (int t = 0; t < 20; t++) sim.Step();
                break;
        }
        // Default yaw looks towards -z (larger z = nearer the bottom of the screen).
        var cam = variant switch
        {
            "tml" => new Vector3((T0.X + TT.X) * 0.5f + 2, (T0.Y + TT.Y) * 0.5f + 2, 80), // launchers, missiles, TMD base
            "nuke" => new Vector3(NT.X, NT.Y + 6, 95),                                     // the target: the blast fills the view
            _ => new Vector3(S.X + 8, S.Y + 8, 68),                                        // dome, mobile shield, artillery line
        };
        Console.WriteLine($"StrategicScene '{variant}': shields at {S.X:0},{S.Y:0} (mobile {M.X:0},{M.Y:0}); TML {T0.X:0},{T0.Y:0} -> {TT.X:0},{TT.Y:0}; nuke {N0.X:0},{N0.Y:0} -> {NT.X:0},{NT.Y:0}; default camera {cam.X:0},{cam.Y:0},{cam.Z:0}");
        return cam;
    }

    private static bool NukeAltitude(Simulation sim, out float alt, out float vy)
    {
        var w = sim.World;
        var P = w.Projectiles;
        int nukeBp = w.Blueprints.IndexOf("uef_t3_strategic_missile_launcher");
        for (int k = 0; k < P.AliveCount; k++)
        {
            int p = P.AliveList[k];
            if (P.SourceBp[p] != nukeBp) continue;
            var pos = P.Pos[p];
            alt = pos.Y - w.Terrain.GetSurfaceHeight(new Vector2(pos.X, pos.Z));
            vy = P.Vel[p].Y;
            return true;
        }
        alt = vy = 0;
        return false;
    }
}

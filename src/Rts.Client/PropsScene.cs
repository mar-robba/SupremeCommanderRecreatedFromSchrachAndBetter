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
/// <c>--scene props[:fire|:rocks]</c> (8b map props): the densest forest near the middle of the map with a burnt
/// patch, a burning patch, a lane of fallen trees and a T1 tank column driving into the forest (trees fall live),
/// plus the densest rock field. Prints the positions and prop counts; aim with <c>--camera x,z,dist</c>.
/// </summary>
public static class PropsScene
{
    public const string Name = "props";
    private static readonly string[] Variants = ["", "fire", "rocks"];

    public static void ApplyDefaults(ClientOptions o)
    {
        if (!Variants.Contains(Variant(o.Scene!)))
            throw new ArgumentException("unknown scene " + o.Scene + " (props[:fire|:rocks])");
        if (!o.MapSizeGiven) o.MapSize = 512;
        o.Players = 2;
        o.NoFog = true;
    }

    private static string Variant(string scene) => scene.Contains(':') ? scene[(scene.IndexOf(':') + 1)..] : "";

    public static void Setup(Simulation sim, GameConfig cfg)
    {
        var w = sim.World;
        var props = w.Props;
        string variant = Variant(cfg.Scene!);
        foreach (var a in w.Armies) a.IsAI = false;
        int trees = 0, rocks = 0;
        foreach (int i in props.AliveList)
            if (props.Kind[i] == PropKind.Tree) trees++;
            else if (props.Kind[i] == PropKind.Rock) rocks++;
        Console.WriteLine($"props scene: {trees} trees, {rocks} rocks on a {w.Terrain.Width} map");

        var forest = Densest(w, PropKind.Tree, 9f);
        var field = Densest(w, PropKind.Rock, 12f);
        var near = new List<int>();
        void Ignite(Vector2 at, float r)
        {
            near.Clear();
            props.QueryRadius(at, r, near);
            foreach (int p in near) if (props.Kind[p] == PropKind.Tree) PropSystem.Ignite(w, p);
        }
        // Burnt patch (burns out before the game starts), then a burning one and a lane of fallen trees.
        Ignite(forest + new Vector2(9, -3), 3.5f);
        for (int k = 0; k < PropSystem.BurnTicks + PropSystem.BurnJitterTicks + 5; k++) sim.Step();
        Ignite(forest + new Vector2(-7, 4), 3f);
        near.Clear();
        props.QueryRadius(forest + new Vector2(0, -6), 6f, near);
        foreach (int p in near)
            if (props.Kind[p] == PropKind.Tree && MathF.Abs(props.Pos[p].Y - (forest.Y - 6)) < 1.6f) PropSystem.Fell(w, p, 0.4f);

        int tank = w.Blueprints.Factions.RoleBlueprint(w.Armies[0].Faction, FactionRoles.TankT1);
        var column = new List<UnitHandle>();
        if (tank >= 0)
            for (int k = 0; k < 3; k++)
            {
                var at = w.Terrain.Clamp(forest + new Vector2(-16, 1.5f * k - 1.5f), 4f);
                var h = w.SpawnUnit(tank, 0, at, 0f);
                if (!h.IsNone) column.Add(h);
            }
        var cmds = new List<Command>();
        if (column.Count > 0) cmds.Add(new IssueOrderCommand(0, column.ToArray(), Order.MoveTo(forest + new Vector2(14, 0)), false));
        sim.Step(cmds);
        for (int k = 0; k < 20; k++) sim.Step();

        Console.WriteLine($"props scene: forest at {forest.X:0},{forest.Y:0} (--camera {forest.X:0},{forest.Y + 2:0},40)");
        Console.WriteLine($"props scene: rocks at {field.X:0},{field.Y:0} (--camera {field.X:0},{field.Y + 2:0},40)");
        var cam = variant switch
        {
            "fire" => new Vector3(forest.X - 7, forest.Y + 6, 22f),
            "rocks" => new Vector3(field.X, field.Y + 2, 36f),
            _ => new Vector3(forest.X, forest.Y + 2, 46f),
        };
        cfg.SceneCamera = cam;
        Console.WriteLine($"props scene '{(variant == "" ? "default" : variant)}': default camera {cam.X:0},{cam.Y:0},{cam.Z:0}");
    }

    /// <summary>The prop of a kind with the most props of that kind within <paramref name="r"/>, closest to the map
    /// center on ties (the scene wants a forest or a rock field away from the start plateaus).</summary>
    private static Vector2 Densest(SimWorld w, PropKind kind, float r)
    {
        var props = w.Props;
        var mid = new Vector2(w.Terrain.Width * 0.5f, w.Terrain.Height * 0.5f);
        var near = new List<int>();
        Vector2 best = mid;
        float bestScore = -1;
        foreach (int i in props.AliveList)
        {
            if (props.Kind[i] != kind) continue;
            near.Clear();
            props.QueryRadius(props.Pos[i], r, near);
            int n = 0;
            foreach (int p in near) if (props.Kind[p] == kind) n++;
            float score = n - Vector2.Distance(props.Pos[i], mid) * 0.02f;
            if (score > bestScore) { bestScore = score; best = props.Pos[i]; }
        }
        return best;
    }
}

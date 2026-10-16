using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim;

/// <summary>
/// JIT warm-up (wave 5m): the first tick of a game used to cost ~200 ms, mostly the JIT compiling the AI, intel,
/// orders and combat code on first use (the first AI think alone ~130 ms). <see cref="Start"/> runs a throwaway
/// 2-AI skirmish with a small battle on a background thread (the client calls it at startup, while the menu shows),
/// so the real game's first ticks run already-compiled code. The throwaway world is separate (no shared sim state;
/// <see cref="Core.Par"/> calls from this thread run inline while the real sim owns the pool), so it cannot change
/// any game's results.
/// </summary>
public static class SimWarmup
{
    [Persistence.NoSave] private static Task? _task;
    [Persistence.NoSave] private static readonly Lock Gate = new();

    /// <summary>Starts the warm-up once per process on a background thread; later calls return the same task.</summary>
    public static Task Start(BlueprintDb db)
    {
        lock (Gate) return _task ??= Task.Run(() => Run(db));
    }

    /// <summary>Runs the throwaway skirmish on the calling thread (blocking).</summary>
    public static void Run(BlueprintDb db, int ticks = 40)
    {
        var sim = GameSetup.CreateSkirmish(db, new SkirmishSettings
        {
            MapSize = 256, Seed = 1, Players = 2, AiPlayers = [true, true], UnitCapacity = 2048,
        });
        var w = sim.World;
        var center = new Vector2(w.Terrain.Width / 2f, w.Terrain.Height / 2f);
        string[] roles = [FactionRoles.TankT1, FactionRoles.ArtilleryT1, FactionRoles.MobileAaT1, FactionRoles.InterceptorT1, FactionRoles.EngineerT1];
        var cmds = new List<Command>();
        for (int a = 0; a < 2; a++)
        {
            var units = new List<UnitHandle>();
            float side = a == 0 ? -1 : 1;
            for (int k = 0; k < 20; k++)
            {
                int bp = db.Factions.RoleBlueprint(w.Armies[a].Faction, roles[k % roles.Length]);
                if (bp < 0) continue;
                var h = w.SpawnUnit(bp, a, center + new Vector2(side * (10 + k / 5 * 2f), (k % 5) * 2f), 0);
                if (!h.IsNone) units.Add(h);
            }
            cmds.Add(new IssueOrderCommand(a, units.ToArray(), Order.AttackMoveTo(center - new Vector2(side * 20, 0)), false));
        }
        sim.Step(cmds);
        for (int t = 1; t < ticks; t++) sim.Step();
    }
}

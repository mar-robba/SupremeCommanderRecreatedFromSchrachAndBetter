using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Naval;
using Rts.Sim.Orders;
using Rts.Sim.Strategic;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>
/// A rich, fully deterministic scenario for the save/load and replay tests: 4 skirmish AIs (Hard, Normal, Hard, Easy)
/// on a Lakes map in sandbox mode, one of EVERY blueprint spawned at tick 1 (naval ones on water), cheats toggled,
/// tactical/strategic missile launches later on. Commands only depend on the tick and the world state.
/// </summary>
public static class PersistenceTestUtil
{
    public static SkirmishSettings Settings(int seed = 11) => new()
    {
        Seed = seed, MapSize = 256, Players = 4, MapPreset = MapPreset.Lakes,
        AiPlayers = [true, true, true, true],
        AiDifficulties = [AiDifficulty.Hard, AiDifficulty.Normal, AiDifficulty.Hard, AiDifficulty.Easy],
    };

    public static Simulation NewRich(int seed = 11)
    {
        var sim = GameSetup.CreateSkirmish(TestUtil.Db, Settings(seed));
        SandboxSystem.Enable(sim.World);
        return sim;
    }

    /// <summary>Scripted commands of tick <paramref name="tick"/> (the tick about to be stepped).</summary>
    public static List<Command> CommandsFor(Simulation sim, int tick)
    {
        var w = sim.World;
        var db = w.Blueprints;
        var cmds = new List<Command>();
        if (tick == 1)
        {
            cmds.Add(new SandboxToggleCommand(0, 0, SandboxToggle.InfiniteResources, true));
            cmds.Add(new SandboxToggleCommand(0, 0, SandboxToggle.InstantBuild, true));
            cmds.Add(new SandboxToggleCommand(0, 2, SandboxToggle.InfiniteResources, true));
            var center = new Vector2(w.Terrain.Width / 2f, w.Terrain.Height / 2f);
            for (int b = 0; b < db.Count; b++)
            {
                var bp = db[b];
                if (bp.HasAny(UnitCategory.Commander)) continue;
                int army = b % 2 == 0 ? 0 : 2;
                var start = w.Terrain.StartPositions[army];
                var toward = Vector2.Normalize(center - start);
                var pos = start + toward * 22 + new Vector2(toward.Y, -toward.X) * ((b % 9) - 4) * 6;
                if (bp.HasAny(UnitCategory.Naval) || bp.MinWaterDepth > 0)
                {
                    if (!NavalRules.FindSite(w.Occupancy, w.Terrain, bp, start, 200, out pos)) continue;
                }
                cmds.Add(new SandboxSpawnCommand(0, army, b, pos, 1));
            }
        }
        if (tick == 30)
        {
            // Spawns are done and silos stocked (instant build fills them): back to a normal economy (keeps the test fast).
            cmds.Add(new SandboxToggleCommand(0, 0, SandboxToggle.InstantBuild, false));
            cmds.Add(new SandboxToggleCommand(0, 0, SandboxToggle.InfiniteResources, false));
            cmds.Add(new SandboxToggleCommand(0, 2, SandboxToggle.InfiniteResources, false));
        }
        if (tick == 400 || tick == 1300)
        {
            // Every launcher of army 0 fires at the nearest enemy start (TML out of range orders are dropped).
            var target = w.Terrain.StartPositions[1];
            var launchers = new List<UnitHandle>();
            foreach (int i in w.Units.AliveList)
            {
                if (w.Units.Army[i] != 0 || !w.Units.IsActive(i)) continue;
                if (w.BpOf(i).Silo != null) launchers.Add(w.Units.HandleOf(i));
            }
            if (launchers.Count > 0) cmds.Add(new IssueOrderCommand(0, launchers.ToArray(), Order.LaunchAt(target), false));
        }
        if (tick == 1000)
        {
            // In-place upgrades in progress at the save tick (factories, mexes, radars of army 0).
            var up = new List<UnitHandle>();
            foreach (int i in w.Units.AliveList)
                if (w.Units.Army[i] == 0 && w.Units.IsActive(i) && w.Units.IsComplete(i) && w.BpOf(i).UpgradesToIndex >= 0)
                    up.Add(w.Units.HandleOf(i));
            if (up.Count > 0) cmds.Add(new UpgradeCommand(0, up.ToArray()));
        }
        if (tick == 900) cmds.Add(new SandboxNukeCommand(0, w.Terrain.StartPositions[3]));
        if (tick == 1500) cmds.Add(new SandboxSetAiCommand(0, 3, false));
        return cmds;
    }

    /// <summary>Steps from the current tick up to (and including) tick <paramref name="toTick"/>.</summary>
    public static void RunTo(Simulation sim, int toTick, Action<Simulation>? afterTick = null)
    {
        while (sim.World.Tick < toTick)
        {
            sim.Step(CommandsFor(sim, sim.World.Tick + 1));
            afterTick?.Invoke(sim);
        }
    }

    public static T WithThreads<T>(int threads, Func<T> f)
    {
        int old = Par.MaxThreads;
        Par.MaxThreads = threads;
        try { return f(); }
        finally { Par.MaxThreads = old; }
    }
}

using System.Numerics;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Rts.Sim.Tests;

public static class TestUtil
{
    private static BlueprintDb? _db;

    /// <summary>Blueprints from the repository data folder (loaded once).</summary>
    public static BlueprintDb Db => _db ??= BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    public static Simulation NewSkirmish(int seed = 42, int mapSize = 256, int players = 2) =>
        GameSetup.CreateSkirmish(Db, new SkirmishSettings { Seed = seed, MapSize = mapSize, Players = players });

    public static UnitHandle Spawn(Simulation sim, string bp, int army, Vector2 pos, float heading = 0) =>
        sim.World.SpawnUnit(Db.IndexOf(bp), army, pos, heading, complete: true);

    public static void Run(Simulation sim, int ticks, params Command[] firstTickCommands)
    {
        sim.Step(firstTickCommands);
        for (int t = 1; t < ticks; t++) sim.Step();
    }
}

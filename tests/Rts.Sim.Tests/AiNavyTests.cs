using Rts.Sim.AI;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Skirmish AI navy (AiNavy): naval factories and ships on island maps, nothing naval on dry maps.</summary>
public class AiNavyTests
{
    internal static Simulation NewGame(MapPreset preset, int seed, int players = 2)
    {
        var ai = new bool[players];
        Array.Fill(ai, true);
        var sim = GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings { Seed = seed, MapSize = 256, Players = players, AiPlayers = ai, MapPreset = preset });
        for (int a = 0; a < players; a++) sim.World.Armies[a].AiDifficulty = AiDifficulty.Normal;
        return sim;
    }

    internal static void RunTo(Simulation sim, int tick)
    {
        while (sim.World.Tick < tick)
        {
            sim.Step();
            sim.World.Events.Effects.Clear();
        }
    }

    internal static AiBrain Brain(Simulation sim, int army) => AiSystem.StateOf(sim.World)!.Brain(army);

    [Fact]
    public void ArchipelagoAiBuildsNavalFactoryAndShips()
    {
        var sim = NewGame(MapPreset.Archipelago, 1);
        RunTo(sim, 6 * 60 * SimWorld.TicksPerSecond);
        for (int a = 0; a < 2; a++)
        {
            var b = Brain(sim, a);
            Assert.True(b.Navy.Active && b.Navy.Priority, $"army {a}: navy not active");
            Assert.True(b.Navy.FactoriesOrdered > 0, $"army {a}: no naval factory");
            Assert.True(b.Stats.Naval > 0, $"army {a}: no ships queued");
        }
    }

    [Fact]
    public void PlainsAiBuildsNothingNaval()
    {
        var sim = NewGame(MapPreset.Plains, 3);
        RunTo(sim, 5 * 60 * SimWorld.TicksPerSecond);
        for (int a = 0; a < 2; a++)
        {
            var b = Brain(sim, a);
            Assert.False(b.Navy.Active);
            Assert.Equal(0, b.Navy.FactoriesOrdered + b.Stats.Naval);
            Assert.False(b.Transport.Island);
            Assert.Equal(0, b.Transport.TransportsOrdered);
        }
    }
}

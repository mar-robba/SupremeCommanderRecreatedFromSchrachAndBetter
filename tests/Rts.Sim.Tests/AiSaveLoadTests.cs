using Rts.Sim.Core;
using Rts.Sim.Persistence;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Save/load in the middle of an AI-vs-AI fight (armies assembled and attacking, focus fire, raids,
/// reinforcements): the loaded game continues exactly like the uninterrupted one.</summary>
public class AiSaveLoadTests
{
    private static void RunTo(Simulation sim, int tick)
    {
        while (sim.World.Tick < tick)
        {
            sim.Step();
            sim.World.Events.Effects.Clear();
        }
    }

    [Fact]
    public void AiBattleContinuesIdenticallyAfterSaveLoad()
    {
        var a = GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings { Seed = 4, MapSize = 256, Players = 2, AiPlayers = [true, true] });
        a.World.Armies[0].AiDifficulty = AiDifficulty.Hard;
        a.World.Armies[1].AiDifficulty = AiDifficulty.Normal;
        const int saveAt = 5 * 60 * SimWorld.TicksPerSecond, endAt = 7 * 60 * SimWorld.TicksPerSecond;
        RunTo(a, saveAt);
        var saved = WorldState.Capture(a);
        var b = WorldState.Restore(saved, a.World.Blueprints, a.World.Terrain);
        for (int t = saveAt + 300; t <= endAt; t += 300)
        {
            RunTo(a, t);
            RunTo(b, t);
            Assert.True(a.StateHash() == b.StateHash(), $"StateHash differs at tick {t}");
        }
        Assert.True(WorldState.Capture(a).AsSpan().SequenceEqual(WorldState.Capture(b)), "serialized state differs at the end");
    }
}

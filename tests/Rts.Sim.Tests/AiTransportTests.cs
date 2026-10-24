using Rts.Sim.Map;
using Rts.Sim.Persistence;
using Rts.Sim.World;
using static Rts.Sim.Tests.AiNavyTests;

namespace Rts.Sim.Tests;

/// <summary>Skirmish AI drops (AiTransport): on an island map the land army is ferried to the enemy island, and a
/// save in the middle of the drops continues identically.</summary>
public class AiTransportTests
{
    [Fact]
    public void IslandAiDropsLandUnitsOnEnemyIsland()
    {
        var sim = NewGame(MapPreset.Archipelago, 1);
        RunTo(sim, 9 * 60 * SimWorld.TicksPerSecond);
        int drops = 0, landed = 0;
        for (int a = 0; a < 2; a++)
        {
            var b = Brain(sim, a);
            Assert.True(b.Transport.Island, $"army {a}: not in island mode");
            Assert.True(b.Transport.TransportsOrdered > 0, $"army {a}: no transport ordered");
            drops += b.Stats.Drops;
            landed += b.Transport.UnitsLanded;
        }
        Assert.True(drops > 0 && landed > 0, $"no drop (drops {drops}, landed {landed})");
    }

    [Fact]
    public void DropGameContinuesIdenticallyAfterSaveLoad()
    {
        var a = NewGame(MapPreset.Archipelago, 1);
        const int saveAt = 6 * 60 * SimWorld.TicksPerSecond + 300, endAt = 8 * 60 * SimWorld.TicksPerSecond;
        RunTo(a, saveAt);
        var b = WorldState.Restore(WorldState.Capture(a), a.World.Blueprints, a.World.Terrain);
        for (int t = saveAt + 300; t <= endAt; t += 300)
        {
            RunTo(a, t);
            RunTo(b, t);
            Assert.True(a.StateHash() == b.StateHash(), $"StateHash differs at tick {t}");
        }
        Assert.True(WorldState.Capture(a).AsSpan().SequenceEqual(WorldState.Capture(b)), "serialized state differs at the end");
    }
}

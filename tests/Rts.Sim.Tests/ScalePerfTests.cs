using Rts.Sim.Data;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Wave 5m perf changes that must not change results: nav grids built at setup, the JIT warm-up thread.</summary>
public class ScalePerfTests
{
    private static Simulation NewAiGame() => GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings
    {
        MapSize = 256, Seed = 5, Players = 2, AiPlayers = [true, true],
    });

    [Fact]
    public void SkirmishSetupBuildsTheNavGrids()
    {
        var sim = NewAiGame();
        Assert.NotNull(sim.World.Paths.GridFor(MotionLayer.Land));
        Assert.NotNull(sim.World.Paths.GridFor(MotionLayer.Water));
    }

    [Fact]
    public void WarmupOnAnotherThreadDoesNotChangeAGame()
    {
        static ulong Play(Simulation sim)
        {
            for (int t = 0; t < 300; t++) sim.Step();
            return sim.StateHash();
        }
        ulong alone = Play(NewAiGame());
        var game = NewAiGame();
        var warm = Task.Run(() => SimWarmup.Run(TestUtil.Db, 60)); // competes for the Par pool meanwhile
        ulong withWarmup = Play(game);
        warm.Wait();
        Assert.Equal(alone, withWarmup);
    }
}

using Rts.Sim.Persistence;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>(6b, G6) Score history: samples every 300 ticks per army, the score formula, save/load.</summary>
public class ScoreHistoryTests
{
    private static Simulation MakeAi(int seed = 9) => GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings
    {
        Seed = seed, MapSize = 256, Players = 2, AiPlayers = [true, true], Options = new GameOptions(),
    });

    [Fact]
    public void ScoreFormulaIsTheDocumentedSum()
    {
        Assert.Equal(0, ArmyStats.Score(0, 0, 0, 0));
        // 1000 mass / 10 + 5000 energy / 100 + 3 kills * 10 + 4 built * 2 = 100 + 50 + 30 + 8
        Assert.Equal(188, ArmyStats.Score(1000, 5000, 3, 4));
        var st = new ArmyStats { MassCollected = 95, EnergyCollected = 199, UnitsKilled = 1, UnitsBuilt = 2, StructuresBuilt = 1 };
        Assert.Equal((int)(9.5f + 1.99f + 10 + 6), st.Score());
    }

    [Fact]
    public void EveryArmyGetsASampleEveryInterval()
    {
        var sim = MakeAi();
        for (int t = 0; t < ScoreSample.Interval * 4 + 10; t++) sim.Step();
        var w = sim.World;
        foreach (var a in w.Armies)
        {
            var h = a.Stats.History;
            Assert.Equal(4, h.Count);
            for (int i = 0; i < h.Count; i++) Assert.Equal((i + 1) * ScoreSample.Interval, h[i].Tick);
            Assert.True(h[^1].MassCollected > 0, "mass collected grows");
            Assert.True(h[^1].MassCollected >= h[0].MassCollected);
            Assert.True(h[^1].Units > 0);
            Assert.True(h[^1].Score > 0);
            Assert.True(h[^1].MassIncome > 0);
        }
        // The live score (snapshot ArmyView.Score) only grows past the last sample.
        Assert.True(w.Armies[0].Stats.Score() >= w.Armies[0].Stats.History[^1].Score);
    }

    [Fact]
    public void HistorySurvivesSaveLoadAndStaysDeterministic()
    {
        var a = MakeAi(11);
        for (int t = 0; t < 700; t++) a.Step();
        var saved = WorldState.Capture(a);
        var b = WorldState.Restore(saved, a.World.Blueprints, a.World.Terrain);
        for (int k = 0; k < a.World.Armies.Length; k++)
            Assert.Equal(a.World.Armies[k].Stats.History, b.World.Armies[k].Stats.History);
        for (int t = 0; t < 600; t++) { a.Step(); b.Step(); }
        Assert.Equal(a.StateHash(), b.StateHash());
        for (int k = 0; k < a.World.Armies.Length; k++)
        {
            Assert.Equal(4, b.World.Armies[k].Stats.History.Count);
            Assert.Equal(a.World.Armies[k].Stats.History, b.World.Armies[k].Stats.History);
        }
    }
}

using Rts.Client.Menus;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Client.Tests;

/// <summary>(6b, G6) End-screen score graph data: one line per army, history + a live last point, metric values.</summary>
public class ScoreGraphTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    [Fact]
    public void GraphHasOneLinePerArmyWithTheLivePointLast()
    {
        var sim = GameSetup.CreateSkirmish(Db, new SkirmishSettings
        {
            Seed = 3, MapSize = 256, Players = 2, AiPlayers = [true, true], Options = new GameOptions(),
        });
        for (int t = 0; t < 650; t++) sim.Step();
        var g = new ScoreGraph(sim.World, 0);
        Assert.Equal(2, g.Lines.Count);
        Assert.True(g.Lines[0].Local);
        Assert.False(g.Lines[1].Local);
        Assert.True(g.HasData);
        foreach (var l in g.Lines)
        {
            Assert.Equal([300, 600, 650], l.Samples.Select(s => s.Tick));
            foreach (var m in Enum.GetValues<ScoreGraph.Metric>())
                Assert.All(l.Samples, s => Assert.True(ScoreGraph.ValueOf(s, m) <= g.MaxOf(m)));
            Assert.Equal(l.Samples[^1].Units, (int)ScoreGraph.ValueOf(l.Samples[^1], ScoreGraph.Metric.Units));
        }
        Assert.Equal(ScoreGraph.MetricNames.Length, Enum.GetValues<ScoreGraph.Metric>().Length);
    }
}

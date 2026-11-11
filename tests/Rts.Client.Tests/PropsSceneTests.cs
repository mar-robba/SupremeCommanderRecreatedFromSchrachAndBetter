using System.Numerics;
using Rts.Client.Input;
using Rts.Client.Menus;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Client.Tests;

/// <summary>(8b) <c>--scene props</c> and the reclaim labels of map props (headless, no window).</summary>
public class PropsSceneTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    [Fact]
    public void SceneHasForestWithFallenBurningAndBurntTrees()
    {
        var o = ClientOptions.Parse(["--scene", "props"]);
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--scene", "props:bogus"]));
        var cfg = GameConfig.FromOptions(o);
        var sim = GameSession.CreateSimulation(Db, cfg, out _);
        Assert.NotNull(cfg.SceneCamera);
        var props = sim.World.Props;
        int trees = 0, rocks = 0, fallen = 0, burning = 0, burnt = 0;
        foreach (int i in props.AliveList)
        {
            if (props.Kind[i] == PropKind.Rock) rocks++;
            if (props.Kind[i] != PropKind.Tree) continue;
            trees++;
            var f = props.Flags[i];
            if ((f & PropFlags.Fallen) != 0) fallen++;
            if ((f & PropFlags.Burning) != 0) burning++;
            if ((f & PropFlags.Burnt) != 0) burnt++;
        }
        Assert.True(trees > 500 && rocks > 50, $"trees {trees}, rocks {rocks}");
        Assert.True(fallen > 0 && burning > 0 && burnt > 0, $"fallen {fallen}, burning {burning}, burnt {burnt}");
    }

    [Fact]
    public void ReclaimLabelNamesTreesAndRocks()
    {
        var tree = new ContextAction(ContextKind.Reclaim, UnitHandle.None, Vector2.Zero, 1, 0.4f, 0.5f, 10f, PropKind.Tree);
        var rock = new ContextAction(ContextKind.Reclaim, UnitHandle.None, Vector2.Zero, 2, 12f, 1.5f, 0f, PropKind.Rock);
        var wreck = new ContextAction(ContextKind.Reclaim, UnitHandle.None, Vector2.Zero, 3, 42f, 1f);
        Assert.Equal("Reclaim tree  +10 energy", tree.Label);
        Assert.Equal("Reclaim rock  +12 mass", rock.Label);
        Assert.Equal("Reclaim  +42 mass", wreck.Label);
    }
}

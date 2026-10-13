using Rts.Client.Input;
using Rts.Sim.Core;

namespace Rts.Client.Tests;

/// <summary>Sandbox orders on mixed-army selections: one command per army, deterministic order.</summary>
public class ArmyCommandsTests
{
    private static UnitHandle H(int i) => new(i, 1);

    [Fact]
    public void GroupsByArmyAscendingKeepingSelectionOrder()
    {
        var groups = ArmyCommands.GroupByArmy([(H(5), 2), (H(1), 0), (H(9), 2), (H(3), 0), (H(7), 1)]);
        Assert.Equal([0, 1, 2], groups.Select(g => g.Army));
        Assert.Equal([H(1), H(3)], groups[0].Units);
        Assert.Equal([H(7)], groups[1].Units);
        Assert.Equal([H(5), H(9)], groups[2].Units);
    }

    [Fact]
    public void DropsUnknownUnitsAndDuplicates()
    {
        var groups = ArmyCommands.GroupByArmy([(H(1), 0), (H(2), -1), (H(1), 0)]);
        Assert.Single(groups);
        Assert.Equal([H(1)], groups[0].Units);
        Assert.Empty(ArmyCommands.GroupByArmy([]));
    }
}

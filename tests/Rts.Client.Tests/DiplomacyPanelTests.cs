using Rts.Client.UI;
using Rts.Sim.Snapshot;
using Xunit;

namespace Rts.Client.Tests;

/// <summary>(6b) Diplomacy panel rules: which rows are allies and get give buttons.</summary>
public class DiplomacyPanelTests
{
    private static ArmyView Army(int team, bool defeated = false) => new() { Team = team, Defeated = defeated, Faction = "uef", Name = "x" };

    [Fact]
    public void OnlyLivingAlliesGetGiveButtons()
    {
        // 2v2 + a defeated ally: armies 0,1,3 team 1; 2,4 team 2.
        var armies = new[] { Army(1), Army(1), Army(2), Army(1, defeated: true), Army(2) };
        var rows = DiplomacyRules.Rows(armies, 0);
        Assert.Equal(5, rows.Count);
        Assert.True(rows[0].Self);
        Assert.False(rows[0].CanGive);
        Assert.True(rows[1].Ally && rows[1].CanGive);
        Assert.False(rows[2].Ally || rows[2].CanGive);
        Assert.True(rows[3].Ally && rows[3].Defeated);
        Assert.False(rows[3].CanGive);
        Assert.False(rows[4].CanGive);
    }

    [Fact]
    public void FreeForAllAndDefeatedLocalGiveNothing()
    {
        var ffa = new[] { Army(100), Army(101), Army(102) };
        Assert.DoesNotContain(DiplomacyRules.Rows(ffa, 0), r => r.Ally || r.CanGive);
        var lost = new[] { Army(1, defeated: true), Army(1) };
        Assert.False(DiplomacyRules.Rows(lost, 0)[1].CanGive);
        Assert.True(DiplomacyRules.Rows(lost, 0)[1].Ally);
        // Observer / bad local army index: no allies.
        Assert.DoesNotContain(DiplomacyRules.Rows(ffa, -1), r => r.Self || r.Ally);
    }

    [Fact]
    public void ShareIsAQuarterOfTheStoredAmountRoundedDown()
    {
        Assert.Equal(162f, DiplomacyRules.Share(650f));
        Assert.Equal(0f, DiplomacyRules.Share(3f));
        Assert.Equal(0f, DiplomacyRules.Share(-5f));
    }
}

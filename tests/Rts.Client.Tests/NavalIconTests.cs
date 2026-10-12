using Rts.Client.Rendering;
using Rts.Client.UI;
using Rts.Sim.Data;

namespace Rts.Client.Tests;

/// <summary>Naval blueprints get specific short labels and a known strategic icon (not the generic fallbacks).</summary>
public class NavalIconTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    [Theory]
    [InlineData("uef_t1_frigate", "FRIG")]
    [InlineData("uef_t1_sub", "SUB")]
    [InlineData("uef_t2_destroyer", "DEST")]
    [InlineData("uef_t2_cruiser", "CRUIS")]
    [InlineData("uef_t3_battleship", "BSHIP")]
    [InlineData("uef_t1_naval_factory", "NAV FAC")]
    [InlineData("uef_t1_sonar", "SONAR")]
    [InlineData("uef_t2_sonar", "SONAR")]
    [InlineData("uef_t1_torpedo_launcher", "TORP")]
    [InlineData("uef_t2_torpedo_bomber", "T-BOMB")]
    public void NavalUnitsHaveSpecificShortLabels(string id, string label)
    {
        int i = Db.IndexOf(id);
        Assert.True(i >= 0, id);
        Assert.Equal(label, UnitIcons.ShortLabel(Db[i]));
    }

    [Fact]
    public void EveryNavalBlueprintIconIsInTheAtlas()
    {
        int unknown = IconAtlas.KindOf("unknown");
        int n = 0;
        for (int i = 0; i < Db.Count; i++)
        {
            var bp = Db[i];
            if (!bp.Has(UnitCategory.Naval) && !bp.Has(UnitCategory.AntiNaval)) continue;
            Assert.NotEqual(unknown, IconAtlas.KindOf(bp.Visual.Icon));
            n++;
        }
        Assert.True(n >= 10);
    }
}

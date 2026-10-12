using Rts.Client.UI;
using Rts.Sim.Data;

namespace Rts.Client.Tests;

/// <summary>Strategic structures show up in the engineers' build menu (Defense tab, own tier) and the mobile shield in
/// the T2/T3 land factory lists.</summary>
public class StrategicUiTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    [Theory]
    [InlineData("uef_t2_engineer", "uef_t2_shield_generator", 2)]
    [InlineData("uef_t2_engineer", "uef_t2_tactical_missile_launcher", 2)]
    [InlineData("uef_t2_engineer", "uef_t2_tactical_missile_defense", 2)]
    [InlineData("uef_t2_engineer", "uef_t2_artillery_installation", 2)]
    [InlineData("uef_t3_engineer", "uef_t3_heavy_shield_generator", 3)]
    [InlineData("uef_t3_engineer", "uef_t3_strategic_missile_launcher", 3)]
    [InlineData("uef_t3_engineer", "uef_t3_strategic_missile_defense", 3)]
    [InlineData("uef_t3_engineer", "uef_t3_heavy_artillery", 3)]
    public void StrategicStructuresAreInTheEngineerDefenseTab(string engineer, string structure, int tier)
    {
        var s = Db[structure];
        Assert.Contains(Db.IndexOf(structure), Db[engineer].BuildIndices);
        Assert.True(s.IsStructure);
        Assert.Equal(tier, s.Tech);
        Assert.Equal(BuildTab.Defense, UnitIcons.TabOf(s));
    }

    [Theory]
    [InlineData("uef_t2_land_factory")]
    [InlineData("uef_t3_land_factory")]
    public void MobileShieldIsBuiltByLandFactories(string factory) =>
        Assert.Contains(Db.IndexOf("uef_t2_mobile_shield"), Db[factory].BuildIndices);
}

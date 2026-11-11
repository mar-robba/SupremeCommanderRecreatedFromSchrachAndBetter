using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>Wave 8a roster: the SupCom 1 units that were still missing (data only; behaviour tests live next to
/// their systems: MissileSubTests, CybranCarrierTests, SalemTests, WallTests).</summary>
public class MissingUnitsRosterTests
{
    private static BlueprintDb Db => TestUtil.Db;

    [Theory]
    [InlineData("uef", FactionRoles.LightAssaultT1, "uef_t1_mech_marine")]
    [InlineData("uef", FactionRoles.NukeSubT3, "uef_t3_strategic_missile_sub")]
    [InlineData("uef", FactionRoles.SonarT3, "uef_t3_sonar")]
    [InlineData("uef", FactionRoles.StealthFieldT2, "uef_t2_stealth_field_generator")]
    [InlineData("uef", FactionRoles.WallT1, "uef_t1_wall")]
    [InlineData("cybran", FactionRoles.CarrierT3, "cybran_t3_aircraft_carrier")]
    [InlineData("cybran", FactionRoles.NukeSubT3, "cybran_t3_strategic_missile_sub")]
    [InlineData("cybran", FactionRoles.SonarT3, "cybran_t3_sonar")]
    [InlineData("cybran", FactionRoles.WallT1, "cybran_t1_wall")]
    public void NewUnitsFillTheirRoles(string faction, string role, string id)
    {
        Assert.Equal(Db.IndexOf(id), Db.Factions.RoleBlueprint(faction, role));
        Assert.False(FactionRoles.IsRequired(role)); // optional: the AI must cope with -1
        var bp = Db[id];
        Assert.Equal(faction, bp.Faction);
        Assert.True(bp.MaxHealth > 0 && bp.Economy.BuildCostMass > 0 && bp.Economy.BuildTime > 0, id);
    }

    [Theory]
    [InlineData("uef")]
    [InlineData("cybran")]
    public void EveryBlueprintIsReachableFromTheCommander(string faction)
    {
        int start = Db.Factions.RoleBlueprint(faction, FactionRoles.EngineerT1) >= 0 ? Db.IndexOf(faction + "_commander") : -1;
        var seen = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var bp = Db[queue.Dequeue()];
            var next = new List<int>(bp.BuildIndices);
            if (bp.UpgradesToIndex >= 0) next.Add(bp.UpgradesToIndex);
            foreach (int v in bp.EnhancementVariants) next.AddRange(Db[v].BuildIndices);
            foreach (int n in next)
                if (seen.Add(n)) queue.Enqueue(n);
        }
        var missing = Db.All.Where(b => !b.IsVariant && b.Faction == faction && !seen.Contains(b.Index)).Select(b => b.Id).ToList();
        Assert.True(missing.Count == 0, $"not reachable from {faction}_commander: " + string.Join(", ", missing));
    }

    [Fact]
    public void BuildListsAndUpgradeChains()
    {
        foreach (string f in new[] { "uef", "cybran" })
        {
            // Walls: every engineer tier and the commander; 1x1, cheap, Wall category, no weapons.
            var wall = Db[f + "_t1_wall"];
            Assert.True(wall.Has(UnitCategory.Wall | UnitCategory.Structure));
            Assert.Equal((1, 1), (wall.FootprintX, wall.FootprintZ));
            Assert.Empty(wall.Weapons);
            foreach (string b in new[] { "commander", "t1_engineer", "t2_engineer", "t3_engineer" })
                Assert.Contains(wall.Index, Db[$"{f}_{b}"].BuildIndices);

            // T3 sonar: reached only by upgrading the T2 sonar, longer range.
            var s2 = Db[f + "_t2_sonar"]; var s3 = Db[f + "_t3_sonar"];
            Assert.Equal(s3.Index, s2.UpgradesToIndex);
            Assert.Equal(s2.Index, s3.UpgradesFromIndex);
            Assert.True(s3.Intel.SonarRadius > s2.Intel.SonarRadius);
            Assert.True(s3.MinWaterDepth > 0);
            foreach (var b in Db.All) Assert.DoesNotContain(s3.Index, b.BuildIndices);

            // Strategic missile sub: a diving hull with a strategic silo, from the T3 naval factory only.
            var sub = Db[f + "_t3_strategic_missile_sub"];
            Assert.Equal(MotionLayer.Sub, sub.Movement.Layer);
            Assert.NotNull(sub.Silo);
            Assert.Contains(sub.Index, Db[f + "_t3_naval_factory"].BuildIndices);
            Assert.DoesNotContain(sub.Index, Db[f + "_t2_naval_factory"].BuildIndices);
            Assert.Contains(sub.Weapons, w => w.FiresSubmerged); // tactical missiles from under water

            // Stealth field generator for both factions now.
            Assert.True(Db[f + "_t2_stealth_field_generator"].Intel.StealthFieldRadius > 0);
        }
        Assert.Contains(Db.IndexOf("uef_t1_mech_marine"), Db["uef_t1_land_factory"].BuildIndices);
        Assert.Contains(Db.IndexOf("uef_t1_mech_marine"), Db["uef_t3_land_factory"].BuildIndices);

        var carrier = Db["cybran_t3_aircraft_carrier"];
        Assert.True(carrier.Has(UnitCategory.Factory | UnitCategory.Naval));
        Assert.NotNull(carrier.Cargo);
        Assert.Contains(Db.IndexOf("cybran_t3_air_superiority"), carrier.BuildIndices);
        Assert.Contains(carrier.Index, Db["cybran_t3_naval_factory"].BuildIndices);
        Assert.Equal(MotionLayer.Water, carrier.Movement.Layer); // unlike the Atlantis it cannot dive
    }
}

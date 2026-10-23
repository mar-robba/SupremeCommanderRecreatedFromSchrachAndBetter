using Rts.Client.Rendering;
using Rts.Sim.Data;

namespace Rts.Client.Tests;

/// <summary>
/// Every Cybran blueprint gets a real procedural model (no generic fallback block), within the 16-bit vertex limit
/// of a raylib mesh, with turret pivots wherever a weapon aims, and a strategic icon that exists in the atlas.
/// Uses the upload-free <see cref="UnitModels.CybranMeshStats"/> hook, so nothing touches the GPU.
/// </summary>
public class CybranModelTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    private static List<UnitBlueprint> Cybran()
    {
        var list = new List<UnitBlueprint>();
        for (int i = 0; i < Db.Count; i++)
            if (Db[i].Id.StartsWith("cybran_", StringComparison.Ordinal)) list.Add(Db[i]);
        return list;
    }

    /// <summary>A weapon that aims needs a turret. Not: aircraft (they fire along their heading), torpedoes (fixed hull
    /// tubes, like the UEF sub) and the silo / anti-nuke launchers (TurretTurnRate 0, like the UEF ones).</summary>
    private static bool NeedsTurret(UnitBlueprint bp)
        => !bp.Has(UnitCategory.Air) && bp.Weapons.Any(w => w.TurretTurnRate > 0 && w.ProjectileVisual != "torpedo");

    [Fact]
    public void EveryCybranBlueprintUsesACybranShape()
    {
        var all = Cybran();
        Assert.True(all.Count >= 60, $"only {all.Count} cybran_* blueprints");
        foreach (var bp in all) Assert.True(UnitModels.IsCybranShape(bp.Visual.Shape), $"{bp.Id}: shape {bp.Visual.Shape}");
    }

    [Fact]
    public void EveryCybranModelBuildsWithinTheVertexLimit()
    {
        var problems = new List<string>();
        foreach (var bp in Cybran())
        {
            var s = UnitModels.CybranMeshStats(bp);
            if (s.Body <= 0 || s.Body >= 65535) problems.Add($"{bp.Id}: body {s.Body} vertices");
            if (s.Turret >= 65535) problems.Add($"{bp.Id}: turret {s.Turret} vertices");
            if (s.Turret > 0 && s.Pivots == 0) problems.Add($"{bp.Id}: turret mesh without a pivot");
            if (s.Pivots > 0 && s.Turret == 0) problems.Add($"{bp.Id}: pivots without a turret mesh");
            if (NeedsTurret(bp) && s.Pivots == 0) problems.Add($"{bp.Id}: aimed weapon but no turret pivot");
            if (!bp.Has(UnitCategory.Air) && s.MinY > 0.05f * bp.Visual.Size.Y + 0.01f) problems.Add($"{bp.Id}: model floats (min y {s.MinY:0.00})");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void NoCybranBlueprintFallsBackToTheGenericBlock()
    {
        var generic = new List<string>();
        foreach (var bp in Cybran())
        {
            var probe = new UnitBlueprint
            {
                Id = "cybran_probe", CategoryFlags = bp.CategoryFlags, Weapons = bp.Weapons,
                Visual = new VisualBlueprint { Shape = "cy_no_such_shape", Size = bp.Visual.Size },
            };
            if (UnitModels.CybranMeshStats(bp) == UnitModels.CybranMeshStats(probe)) generic.Add($"{bp.Id} ({bp.Visual.Shape})");
        }
        Assert.True(generic.Count == 0, "generic fallback model: " + string.Join(", ", generic));
    }

    [Theory]
    [InlineData("cybran_t2_shield_ed1", "cybran_t2_shield_ed2")]
    [InlineData("cybran_t2_shield_ed2", "cybran_t2_shield_ed3")]
    [InlineData("cybran_t2_shield_ed3", "cybran_t3_shield_ed4")]
    [InlineData("cybran_t3_shield_ed4", "cybran_t3_shield_ed5")]
    public void ShieldModelGrowsAlongTheUpgradeChain(string lower, string upper)
    {
        var a = UnitModels.CybranMeshStats(Db[Db.IndexOf(lower)]);
        var b = UnitModels.CybranMeshStats(Db[Db.IndexOf(upper)]);
        Assert.True(b.Body + b.Turret > a.Body + a.Turret, $"{upper} ({b.Body}+{b.Turret}) not more detailed than {lower} ({a.Body}+{a.Turret})");
    }

    [Fact]
    public void EveryCybranBlueprintIconIsInTheAtlas()
    {
        int unknown = IconAtlas.KindOf("unknown");
        foreach (var bp in Cybran()) Assert.True(IconAtlas.KindOf(bp.Visual.Icon) != unknown, $"{bp.Id}: icon {bp.Visual.Icon}");
    }
}

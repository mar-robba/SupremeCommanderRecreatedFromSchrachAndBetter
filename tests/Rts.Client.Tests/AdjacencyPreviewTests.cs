using System.Numerics;
using Rts.Client.Input;
using Rts.Sim.Data;

namespace Rts.Client.Tests;

/// <summary>Placement preview of adjacency bonuses (pure, see Input/AdjacencyPreview).</summary>
public class AdjacencyPreviewTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
    private static UnitBlueprint Bp(string id) => Db[Db.IndexOf(id)];

    [Fact]
    public void ExtractorNextToMassStorageReceivesMassBonus()
    {
        var mex = Bp("uef_t1_mass_extractor");
        var store = Bp("uef_t1_mass_storage");
        // 2x2 footprints: centers 2 apart on x share a full edge; the far one doesn't touch.
        var r = AdjacencyPreview.Compute(mex, new Vector2(11, 11), [(1, store, new Vector2(13, 11)), (2, store, new Vector2(17, 11))]);
        var link = Assert.Single(r.Links);
        Assert.Equal(1, link.Id);
        Assert.True(r.Received.MassProd > 0.12f && r.Received.MassProd < 0.13f);
        Assert.Equal(0, r.Boosted); // storage gets nothing from a mex
        Assert.Contains("+12.5% mass", AdjacencyPreview.Summary(r)[0]);
    }

    [Fact]
    public void PowerGeneratorGivesEnergyUseReductionToFactory()
    {
        var pgen = Bp("uef_t1_power_generator");
        var factory = Bp("uef_t1_land_factory");
        // Factory 6x6 at (13,13) covers cells 10..15; a 2x2 generator at (17,13) touches its east edge with 2 cells.
        var r = AdjacencyPreview.Compute(pgen, new Vector2(17, 13), [(5, factory, new Vector2(13, 13))]);
        var link = Assert.Single(r.Links);
        Assert.True(link.Gives.EnergyUse > 0);
        Assert.True(link.Receives.IsZero);
        Assert.Equal(1, r.Boosted);
        Assert.Contains(AdjacencyPreview.Summary(r), s => s.Contains("energy use of 1 neighbour"));
    }

    [Fact]
    public void CornersAndNonParticipantsGiveNothing()
    {
        var mex = Bp("uef_t1_mass_extractor");
        var store = Bp("uef_t1_mass_storage");
        var r = AdjacencyPreview.Compute(mex, new Vector2(11, 11), [(1, store, new Vector2(13, 13))]); // corner only
        Assert.True(r.IsEmpty);
        Assert.Empty(AdjacencyPreview.Summary(r));
    }
}

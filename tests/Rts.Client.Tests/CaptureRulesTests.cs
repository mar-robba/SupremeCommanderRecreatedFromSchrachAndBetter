using Rts.Client.Input;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Tests;

/// <summary>Client-side capture rule (ContextOrders.CanCapture, cursor hint / capture mode): mirrors the sim's
/// CaptureSystem.CanCapture as far as a snapshot can tell.</summary>
public class CaptureRulesTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
    private static UnitBlueprint Bp(string id) => Db[Db.IndexOf(id)];

    [Fact]
    public void CompleteVisibleUnitsCanBeCapturedCommandersCannot()
    {
        var tank = Bp("uef_t1_tank");
        Assert.True(ContextOrders.CanCapture(tank, new UnitView()));
        Assert.False(ContextOrders.CanCapture(tank, new UnitView { Flags = UnitFlags.UnderConstruction }));
        Assert.False(ContextOrders.CanCapture(tank, new UnitView { Flags = UnitFlags.RadarOnly }));
        var acu = Db.All.First(b => b.Has(UnitCategory.Commander));
        Assert.False(ContextOrders.CanCapture(acu, new UnitView()));
    }

    [Fact]
    public void OnlyMobileBuildersCapture()
    {
        Assert.True(ContextOrders.IsBuilder(Bp("uef_t1_engineer")));
        Assert.False(ContextOrders.IsBuilder(Bp("uef_t1_tank")));
        Assert.False(ContextOrders.IsBuilder(Bp("uef_t1_land_factory"))); // build power, but not mobile
    }
}

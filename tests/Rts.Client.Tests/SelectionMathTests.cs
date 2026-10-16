using System.Numerics;
using Rts.Client.Input;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Tests;

/// <summary>Pure selection helpers: group camera focus (control group double-tap), idle engineer / factory rules and
/// cycling (idle buttons, "." / ","), fire state cycling (Q).</summary>
public class SelectionMathTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
    private static UnitBlueprint Bp(string id) => Db[Db.IndexOf(id)];

    [Fact]
    public void FocusPointIsTheUnitNearestToTheCentroid()
    {
        Assert.Null(SelectionMath.FocusPoint([]));
        Assert.Equal(new Vector2(5, 5), SelectionMath.FocusPoint([new Vector2(5, 5)]));
        // Two clusters: the centroid (50, 0) is empty ground; the focus is a real unit, the one nearest to it.
        var pts = new List<Vector2> { new(0, 0), new(1, 0), new(2, 0), new(100, 0), new(101, 0) };
        Assert.Equal(new Vector2(2, 0), SelectionMath.FocusPoint(pts));
    }

    [Fact]
    public void NextIndexCyclesAndWraps()
    {
        long[] ids = [10, 20, 30];
        Assert.Equal(-1, SelectionMath.NextIndex([], 10));
        Assert.Equal(0, SelectionMath.NextIndex(ids, -1));
        Assert.Equal(0, SelectionMath.NextIndex(ids, 99)); // last one no longer idle: start over
        Assert.Equal(1, SelectionMath.NextIndex(ids, 10));
        Assert.Equal(2, SelectionMath.NextIndex(ids, 20));
        Assert.Equal(0, SelectionMath.NextIndex(ids, 30));
    }

    private static UnitView Idle() => new() { WorkMode = BuilderMode.None, WorkProgress = -1, UpgradeBp = -1 };

    [Fact]
    public void IdleEngineerRules()
    {
        var eng = Bp("uef_t1_engineer");
        Assert.True(SelectionMath.IsIdleEngineer(eng, Idle()));
        Assert.False(SelectionMath.IsIdleEngineer(eng, Idle() with { OrderCount = 1 }));
        Assert.False(SelectionMath.IsIdleEngineer(eng, Idle() with { WorkMode = BuilderMode.Reclaim }));
        Assert.False(SelectionMath.IsIdleEngineer(eng, Idle() with { Flags = UnitFlags.UnderConstruction }));
        Assert.False(SelectionMath.IsIdleEngineer(Bp("uef_commander"), Idle())); // the commander is not an "idle engineer"
        Assert.False(SelectionMath.IsIdleEngineer(Bp("uef_t1_land_factory"), Idle()));
    }

    [Fact]
    public void IdleFactoryRules()
    {
        var fac = Bp("uef_t1_land_factory");
        Assert.True(SelectionMath.IsIdleFactory(fac, Idle()));
        Assert.False(SelectionMath.IsIdleFactory(fac, Idle() with { QueueCount = 2 }));
        Assert.False(SelectionMath.IsIdleFactory(fac, Idle() with { WorkProgress = 0.3f }));
        Assert.False(SelectionMath.IsIdleFactory(fac, Idle() with { UpgradeBp = 3 }));
        Assert.False(SelectionMath.IsIdleFactory(fac, Idle() with { Flags = UnitFlags.UnderConstruction }));
        Assert.False(SelectionMath.IsIdleFactory(Bp("uef_t1_engineer"), Idle()));
    }

    [Fact]
    public void FireStateCyclesBothWays()
    {
        Assert.Equal(FireState.ReturnFire, UnitToggles.NextFireState(FireState.FireAtWill, 1));
        Assert.Equal(FireState.HoldFire, UnitToggles.NextFireState(FireState.ReturnFire, 1));
        Assert.Equal(FireState.FireAtWill, UnitToggles.NextFireState(FireState.HoldFire, 1));
        Assert.Equal(FireState.HoldFire, UnitToggles.NextFireState(FireState.FireAtWill, -1));
        Assert.Equal(FireState.FireAtWill, UnitToggles.NextFireState(FireState.ReturnFire, -1));
        Assert.Equal("Hold fire", UnitToggles.FireStateName(FireState.HoldFire));
    }
}

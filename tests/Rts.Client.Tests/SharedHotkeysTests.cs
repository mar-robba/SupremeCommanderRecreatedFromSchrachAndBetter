using Rts.Client.Input;
using Rts.Sim.Data;
using Rts.Sim.Naval;

namespace Rts.Client.Tests;

/// <summary>D / E shared between capture / dive (UnitToggles) and transport-to / dock (TransportInput).</summary>
public class SharedHotkeysTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    private static CargoSelection Sel(params string[] ids)
    {
        var s = new CargoSelection();
        foreach (var id in ids) s.Add(Db[id], 0);
        return s;
    }

    private static bool Capturer(params string[] ids) => ids.Any(id => ContextOrders.IsBuilder(Db[id]));

    private static SharedHotkeys.EAction E(params string[] ids) => SharedHotkeys.ResolveE(Capturer(ids), Sel(ids).Transports > 0);

    [Fact]
    public void EIsCaptureForEngineersAndTransportToWhenTransportsAreSelected()
    {
        Assert.Equal(SharedHotkeys.EAction.Capture, E("uef_t1_engineer"));
        Assert.Equal(SharedHotkeys.EAction.Capture, E("uef_commander", "uef_t1_tank"));
        Assert.Equal(SharedHotkeys.EAction.TransportTo, E("uef_t1_transport", "uef_t1_tank"));
        Assert.Equal(SharedHotkeys.EAction.TransportTo, E("uef_t1_engineer", "uef_t2_transport")); // transports win
        Assert.Equal(SharedHotkeys.EAction.None, E("uef_t1_tank"));
    }

    [Fact]
    public void DDivesSubsAndDocksAircraft()
    {
        bool Sub(params string[] ids) => ids.Any(id => Submarines.CanDive(Db[id]));
        bool Air(params string[] ids) => ids.Any(id => TransportInput.IsAirCargo(Db[id]));
        string[] subs = ["uef_t1_sub"], planes = ["uef_t1_interceptor"], both = ["uef_t1_sub", "uef_t1_interceptor"];
        Assert.Equal((true, false), SharedHotkeys.ResolveD(Sub(subs), Air(subs)));
        Assert.Equal((false, true), SharedHotkeys.ResolveD(Sub(planes), Air(planes)));
        Assert.Equal((true, true), SharedHotkeys.ResolveD(Sub(both), Air(both)));
        // The order buttons agree: Dock shows for aircraft only.
        Assert.Equal(TransportButtons.Dock, TransportInput.ButtonsFor(Sel(planes)) & TransportButtons.Dock);
        Assert.Equal(TransportButtons.None, TransportInput.ButtonsFor(Sel(subs)) & TransportButtons.Dock);
    }

    [Fact]
    public void EngineersRightClickingAnOwnTransportBoardIt()
    {
        Assert.Equal(ContextKind.Load, TransportInput.TargetContext(Sel("uef_t1_engineer"), Db["uef_t1_transport"], 0, targetSelected: false));
    }
}

using System.Numerics;
using Rts.Client.Input;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;

namespace Rts.Client.Tests;

/// <summary>Transport UI decisions (TransportInput): context right-click, order buttons, ferry beacon snapping.</summary>
public class TransportUiTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    private static CargoSelection Sel(params (string id, int used)[] units)
    {
        var s = new CargoSelection();
        foreach (var (id, used) in units) s.Add(Db[id], used);
        return s;
    }

    [Fact]
    public void LandUnitsRightClickingATransportWithRoomBoard()
    {
        var tanks = Sel(("uef_t1_tank", 0), ("uef_t1_tank", 0));
        var transport = Db["uef_t1_transport"];
        Assert.Equal(ContextKind.Load, TransportInput.TargetContext(tanks, transport, 0, targetSelected: false));
        Assert.Equal(ContextKind.Load, TransportInput.TargetContext(tanks, transport, 5, targetSelected: true));
        // Full transport: default right-click (guard).
        Assert.Equal(ContextKind.None, TransportInput.TargetContext(tanks, transport, transport.Cargo!.Capacity, false));
        // A T3 unit (4 slots) does not fit a transport with 3 free slots.
        var heavy = Sel(("uef_t3_heavy_bot", 0));
        Assert.Equal(4, Db["uef_t3_heavy_bot"].CargoSize);
        Assert.Equal(ContextKind.None, TransportInput.TargetContext(heavy, transport, transport.Cargo.Capacity - 3, false));
    }

    [Fact]
    public void TransportRightClickingALandUnitPicksItUp()
    {
        var t = Sel(("uef_t1_transport", 0));
        Assert.Equal(ContextKind.PickUp, TransportInput.TargetContext(t, Db["uef_t1_tank"], 0, targetSelected: false));
        Assert.Equal(ContextKind.None, TransportInput.TargetContext(t, Db["uef_t1_tank"], 0, targetSelected: true));
        // Not transportable / no room.
        Assert.Equal(ContextKind.None, TransportInput.TargetContext(t, Db["uef_t1_frigate"], 0, false));
        Assert.Equal(ContextKind.None, TransportInput.TargetContext(Sel(("uef_t1_transport", 6)), Db["uef_t1_tank"], 0, false));
        // An empty transport selected alone and another transport: nothing special.
        Assert.Equal(ContextKind.None, TransportInput.TargetContext(t, Db["uef_t2_transport"], 0, false));
    }

    [Theory]
    [InlineData("uef_t2_air_staging")]
    [InlineData("uef_x_atlantis")]
    public void AircraftRightClickingAnAirCarrierDock(string carrier)
    {
        Assert.Equal(ContextKind.Dock, TransportInput.TargetContext(Sel(("uef_t1_interceptor", 0)), Db[carrier], 0, false));
        Assert.Equal(ContextKind.None, TransportInput.TargetContext(Sel(("uef_t1_tank", 0)), Db[carrier], 0, false));
    }

    [Fact]
    public void LoadedTransportRightClickingTheGroundUnloads()
    {
        Assert.Equal(ContextKind.Unload, TransportInput.GroundContext(Sel(("uef_t1_transport", 2), ("uef_t1_tank", 0))));
        Assert.Equal(ContextKind.Move, TransportInput.GroundContext(Sel(("uef_t1_transport", 0))));
        Assert.Equal(ContextKind.Move, TransportInput.GroundContext(Sel(("uef_x_atlantis", 4)))); // carriers launch via the button
    }

    [Fact]
    public void ContextOrderGoesOnlyToTheUnitsItAppliesTo()
    {
        Assert.True(TransportInput.Applies(ContextKind.Load, Db["uef_t1_tank"], 0));
        Assert.False(TransportInput.Applies(ContextKind.Load, Db["uef_t1_interceptor"], 0));
        Assert.True(TransportInput.Applies(ContextKind.PickUp, Db["uef_t1_transport"], 0));
        Assert.False(TransportInput.Applies(ContextKind.PickUp, Db["uef_t1_tank"], 0));
        Assert.True(TransportInput.Applies(ContextKind.Dock, Db["uef_t1_interceptor"], 0));
        Assert.True(TransportInput.Applies(ContextKind.Unload, Db["uef_t1_transport"], 1));
        Assert.False(TransportInput.Applies(ContextKind.Unload, Db["uef_t1_transport"], 0));
    }

    [Fact]
    public void OrderButtonsMatchTheSelection()
    {
        Assert.Equal(TransportButtons.Ferry, TransportInput.ButtonsFor(Sel(("uef_t1_tank", 0))));
        Assert.Equal(TransportButtons.Dock, TransportInput.ButtonsFor(Sel(("uef_t1_interceptor", 0))));
        Assert.Equal(TransportButtons.TransportTo | TransportButtons.Ferry | TransportButtons.Dock,
            TransportInput.ButtonsFor(Sel(("uef_t1_transport", 0), ("uef_t1_tank", 0))));
        Assert.True((TransportInput.ButtonsFor(Sel(("uef_t2_transport", 3))) & TransportButtons.Unload) != 0);
        Assert.Equal(TransportButtons.Launch, TransportInput.ButtonsFor(Sel(("uef_t2_air_staging", 2))));
        Assert.Equal(TransportButtons.None, TransportInput.ButtonsFor(Sel(("uef_t2_air_staging", 0))));
        Assert.Equal(TransportButtons.None, TransportInput.ButtonsFor(Sel(("uef_t1_land_factory", 0), ("uef_t1_frigate", 0))));
        var atlantis = Sel(("uef_x_atlantis", 4));
        Assert.Equal(TransportButtons.Launch, TransportInput.ButtonsFor(atlantis));
        Assert.Equal(1, atlantis.MobileLoadedAirCarriers);
    }

    [Fact]
    public void FerryClicksSnapToTheNearestOwnBeacon()
    {
        FerryView[] f =
        [
            new() { Army = 0, Pickup = new Vector3(100, 5, 100), Drop = new Vector3(200, 5, 200), Transports = 1 },
            new() { Army = 1, Pickup = new Vector3(103, 5, 100), Drop = new Vector3(150, 5, 150), Transports = 1 },
            new() { Army = 0, Pickup = new Vector3(106, 5, 100), Drop = new Vector3(190, 5, 200), Transports = 2 },
        ];
        Assert.Equal(0, TransportInput.NearestFerryPoint(f, 0, new Vector2(102, 100), 5f, drops: false));
        Assert.Equal(2, TransportInput.NearestFerryPoint(f, 0, new Vector2(105, 101), 5f, drops: false));
        Assert.Equal(-1, TransportInput.NearestFerryPoint(f, 0, new Vector2(120, 100), 5f, drops: false));
        Assert.Equal(1, TransportInput.NearestFerryPoint(f, 1, new Vector2(150, 152), 5f, drops: true));
        Assert.Equal(2, TransportInput.NearestFerryPoint(f, 0, new Vector2(192, 199), 5f, drops: true));
    }
}

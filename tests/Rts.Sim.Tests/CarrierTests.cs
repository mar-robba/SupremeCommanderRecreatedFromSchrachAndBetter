using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Naval;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.Transport;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

/// <summary>
/// Cybran T3 aircraft carrier: the generic Atlantis code (Cargo Air + Factory + Mobile) on a ship that cannot dive.
/// It builds aircraft at sea, docks, repairs and carries them (KeepDocked) and launches them on Unload.
/// </summary>
public class CarrierTests
{
    private const string Carrier = "cybran_t3_aircraft_carrier";

    private static void Rich(SimWorld w)
    {
        foreach (var a in w.Armies) { a.Eco.Mass = a.Eco.MassMax; a.Eco.Energy = a.Eco.EnergyMax; }
    }

    private static bool Carried(SimWorld w, UnitHandle h) => w.Units.IsActive(h) && (w.Units.Flags[h.Index] & UnitFlags.Carried) != 0;

    private static bool RunUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return true;
            Rich(sim.World);
            sim.Step();
        }
        return done();
    }

    /// <summary>Open sea (no strip), the carrier at (80, 64) and storage for the build costs (test only: on the sea).</summary>
    private static (Simulation sim, UnitHandle carrier) Sea()
    {
        var sim = NavalWorld.Basin(strip: false);
        var carrier = NavalWorld.Spawn(sim, Carrier, 0, 80, 64);
        NavalWorld.Spawn(sim, "uef_t1_energy_storage", 0, 8, 120);
        NavalWorld.Spawn(sim, "uef_t1_mass_storage", 0, 14, 120);
        sim.Step();
        return (sim, carrier);
    }

    private static UnitHandle[] Planes(Simulation sim, string bp, int n, float z)
    {
        var r = new UnitHandle[n];
        for (int k = 0; k < n; k++) r[k] = NavalWorld.Spawn(sim, bp, 0, 70 + 3 * k, z);
        sim.Step();
        return r;
    }

    [Fact]
    public void CarrierIsAShipThatCannotDive()
    {
        var bp = TestUtil.Db[Carrier];
        Assert.True(bp.IsMobile);
        Assert.False(Submarines.CanDive(bp));
        Assert.Equal(0, bp.CargoSize); // ships never board anything
        Assert.Equal(CargoLayer.Air, bp.Cargo!.Layer);
        Assert.True(bp.Cargo.KeepDocked);
        Assert.Equal(16, bp.Cargo.Capacity);
        Assert.Equal(Carrier, TestUtil.Db.Factions.RoleBlueprint("cybran", "carrier_t3") is int i and >= 0 ? TestUtil.Db[i].Id : null);
    }

    [Fact]
    public void CarrierBuildsAircraftAtSeaAndTheyTakeOff()
    {
        var (sim, carrier) = Sea();
        var w = sim.World;
        sim.Step([new FactoryEnqueueCommand(0, [carrier], Bp("cybran_t1_interceptor"), 2)]);
        var built = new List<int>();
        Assert.True(RunUntil(sim, () =>
        {
            built = UnitsOf(w, 0, "cybran_t1_interceptor").Where(i => w.Units.IsComplete(i)).ToList();
            return built.Count >= 2;
        }, 800), "the carrier should build two interceptors");
        TestUtil.Run(sim, 30);
        foreach (int i in built)
        {
            Assert.False(Carried(w, w.Units.HandleOf(i)), "a plane built on a carrier takes off (deck open)");
            Assert.True(w.Units.Elevation[i] > w.Terrain.WaterLevel + 3f, "launched into the air");
        }
        Assert.True(w.Terrain.IsUnderwater(w.Units.Pos[carrier.Index]));
    }

    [Fact]
    public void DockedAircraftAreRepairedCarriedAndLaunched()
    {
        var (sim, carrier) = Sea();
        var w = sim.World;
        var planes = Planes(sim, "cybran_t1_interceptor", 3, 96);
        float max = w.BpOf(planes[0].Index).MaxHealth;
        w.Units.Health[planes[0].Index] = max * 0.5f;
        sim.Step([new IssueOrderCommand(0, planes, TransportOrders.Load(carrier), false)]); // Load on an air carrier = Dock
        Assert.True(RunUntil(sim, () => planes.All(p => Carried(w, p)), 400), "the aircraft land on the carrier");

        // The carrier sails off with its air wing; the docked aircraft ride along and get repaired.
        sim.Step([new IssueOrderCommand(0, [carrier], Order.MoveTo(new Vector2(80, 24)), false)]);
        TestUtil.Run(sim, 300);
        Assert.True(w.Units.Pos[carrier.Index].Y < 40f, $"the carrier should have sailed, at {w.Units.Pos[carrier.Index]}");
        foreach (var p in planes)
        {
            Assert.True(Carried(w, p), "KeepDocked: the aircraft stay aboard");
            Assert.Equal(w.Units.Pos[carrier.Index], w.Units.Pos[p.Index]);
        }
        Assert.Equal(max, w.Units.Health[planes[0].Index], 1);

        sim.Step([new IssueOrderCommand(0, [carrier], TransportOrders.Unload(w.Units.Pos[carrier.Index]), false)]);
        sim.Step();
        Assert.All(planes, p => Assert.False(Carried(w, p)));
        TestUtil.Run(sim, 40);
        Assert.All(planes, p => Assert.True(w.Units.Elevation[p.Index] > w.Terrain.WaterLevel + 3f, "launched into the air"));
    }

    [Fact]
    public void DeckHoldsSixteenSlots()
    {
        var (sim, carrier) = Sea();
        var w = sim.World;
        var planes = Planes(sim, "cybran_t3_air_superiority", 10, 100); // T3 aircraft take 2 slots: 8 fit
        Assert.Equal(2, w.BpOf(planes[0].Index).CargoSize);
        sim.Step([new IssueOrderCommand(0, planes, TransportOrders.Load(carrier), false)]);
        RunUntil(sim, () => planes.Count(p => Carried(w, p)) >= 8, 500);
        TestUtil.Run(sim, 100);
        Assert.Equal(8, planes.Count(p => Carried(w, p)));
        Assert.Equal(16, w.Cargo.UsedSlots(w, carrier.Index));
    }

    [Fact]
    public void CarrierDeathKillsItsDockedAircraft()
    {
        var (sim, carrier) = Sea();
        var w = sim.World;
        var planes = Planes(sim, "cybran_t1_interceptor", 2, 96);
        sim.Step([new IssueOrderCommand(0, planes, TransportOrders.Load(carrier), false)]);
        Assert.True(RunUntil(sim, () => planes.All(p => Carried(w, p)), 400));
        w.KillUnit(carrier.Index, UnitHandle.None);
        TestUtil.Run(sim, 2);
        Assert.All(planes, p => Assert.False(w.Units.IsActive(p)));
    }

    [Fact]
    public void NavalFactoryBuildsTheCarrierAndItSailsOut()
    {
        var sim = NavalWorld.Archipelago(seed: 42);
        var w = sim.World;
        var t = w.Terrain;
        var site = NavalWorld.NavalSite(w, t.StartPositions[0], "cybran_t3_naval_factory");
        var fac = w.SpawnUnit(Bp("cybran_t3_naval_factory"), 0, site, DMath.HeadingOf(t.StartPositions[1] - site), complete: true);
        sim.Step([new FactoryEnqueueCommand(0, [fac], Bp(Carrier), 1)]);
        var built = UnitHandle.None;
        Assert.True(RunUntil(sim, () =>
        {
            var c = w.Units.Factory[fac.Index].Current;
            if (!c.IsNone) built = c;
            return !built.IsNone && c.IsNone && w.Units.IsActive(built) && w.Units.IsComplete(built.Index);
        }, 2000), "the T3 naval factory should finish the carrier");
        float half = ConstructionSystem.HalfSize(w.BpOf(fac.Index));
        TestUtil.Run(sim, 200);
        var p = w.Units.Pos[built.Index];
        Assert.True(t.IsUnderwater(p), $"the carrier should be at sea, at {p}");
        Assert.True(MathF.Max(MathF.Abs(p.X - site.X), MathF.Abs(p.Y - site.Y)) > half, $"the carrier should leave the factory, at {p}");
        Assert.Equal(t.WaterLevel, w.Units.Elevation[built.Index], 2);
    }
}

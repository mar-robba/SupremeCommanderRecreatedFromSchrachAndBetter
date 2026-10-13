using System.Numerics;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.Transport;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Cargo mechanic: transports, ferry, air staging, fuel, Atlantis (Rts.Sim/Transport).</summary>
public class TransportTests
{
    private static Vector2 Start(SimWorld w, int army = 0) => w.Units.Pos[w.Armies[army].Commander.Index];

    private static bool RunUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return true;
            sim.Step();
        }
        return done();
    }

    private static bool Carried(SimWorld w, UnitHandle h) => w.Units.IsActive(h) && (w.Units.Flags[h.Index] & UnitFlags.Carried) != 0;

    private static UnitHandle[] SpawnRow(Simulation sim, string bp, int n, Vector2 at, int army = 0)
    {
        var hs = new UnitHandle[n];
        for (int k = 0; k < n; k++) hs[k] = TestUtil.Spawn(sim, bp, army, at + new Vector2(k % 4 * 2.2f, k / 4 * 2.2f));
        return hs;
    }

    [Fact]
    public void CargoSizesFollowTechAndCategories()
    {
        var db = TestUtil.Db;
        int Size(string id) => db[db.IndexOf(id)].CargoSize;
        Assert.Equal(1, Size("uef_t1_tank"));
        Assert.Equal(1, Size("uef_t1_engineer"));
        Assert.Equal(2, Size("uef_t2_heavy_tank"));
        Assert.Equal(4, Size("uef_t3_assault_bot"));
        Assert.Equal(4, Size("uef_commander"));
        Assert.Equal(1, Size("uef_t1_interceptor"));
        Assert.Equal(2, Size("uef_t3_air_superiority"));
        Assert.Equal(0, Size("uef_x_atlantis"));
        Assert.Equal(0, Size("uef_t1_land_factory"));
        Assert.Equal(0, Size("uef_t1_frigate"));
        Assert.True(db[db.IndexOf("uef_t1_transport")].IsVtol);
        Assert.Equal(6, db[db.IndexOf("uef_t1_transport")].Cargo!.Capacity);
        Assert.Equal(12, db[db.IndexOf("uef_t2_transport")].Cargo!.Capacity);
        Assert.True(db[db.IndexOf("uef_t1_interceptor")].FuelSeconds > 0);
        Assert.Equal(0, db[db.IndexOf("uef_t1_tank")].FuelSeconds);
    }

    [Fact]
    public void TanksBoardTransportAndUnloadAtPoint()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var s = Start(w) + new Vector2(8, 8);
        var tr = TestUtil.Spawn(sim, "uef_t1_transport", 0, s);
        var tanks = SpawnRow(sim, "uef_t1_tank", 3, s + new Vector2(10, 0));
        sim.Step([new IssueOrderCommand(0, tanks, TransportOrders.Load(tr), false)]);
        Assert.True(RunUntil(sim, () => tanks.All(h => Carried(w, h)), 400), "tanks should board");
        Assert.Equal(3, w.Cargo.Count(tr.Index));
        Assert.Equal(3, w.Cargo.UsedSlots(w, tr.Index));
        for (int k = 0; k < w.Units.AliveCount; k++)
            Assert.DoesNotContain(w.Units.AliveList[k], tanks.Select(h => h.Index));

        // Carried units ride along.
        var drop = w.Terrain.Clamp(s + new Vector2(-30, 25));
        sim.Step([new IssueOrderCommand(0, [tr], TransportOrders.Unload(drop), false)]);
        Assert.True(RunUntil(sim, () => tanks.All(h => !Carried(w, h)), 600), "tanks should be dropped");
        foreach (var h in tanks)
        {
            Assert.True(w.Units.IsActive(h));
            Assert.True(Vector2.Distance(w.Units.Pos[h.Index], drop) < 10f, $"dropped near the point: {w.Units.Pos[h.Index]}");
            Assert.True(MathF.Abs(w.Units.Elevation[h.Index] - w.Terrain.GetHeight(w.Units.Pos[h.Index])) < 0.5f);
        }
        Assert.Equal(0, w.Cargo.Count(tr.Index));
        sim.Step();
        Assert.Contains(tanks[0].Index, w.Units.AliveList.Take(w.Units.AliveCount));
    }

    [Fact]
    public void TransportPicksUpUnitAndQueuedOrdersResumeAfterDrop()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var s = Start(w) + new Vector2(8, 8);
        var tr = TestUtil.Spawn(sim, "uef_t1_transport", 0, s);
        var eng = TestUtil.Spawn(sim, "uef_t1_engineer", 0, s + new Vector2(12, -6));
        sim.Step([new IssueOrderCommand(0, [tr], TransportOrders.Load(eng), false)]);
        Assert.True(RunUntil(sim, () => Carried(w, eng), 400));
        var drop = w.Terrain.Clamp(s + new Vector2(25, 25));
        var after = w.Terrain.Clamp(drop + new Vector2(6, 0));
        sim.Step([new IssueOrderCommand(0, [tr], TransportOrders.Unload(drop), false)]);
        // A move queued on the carried engineer runs after the drop.
        sim.Step([new IssueOrderCommand(0, [eng], Order.MoveTo(after), true)]);
        Assert.True(RunUntil(sim, () => !Carried(w, eng), 600));
        Assert.True(RunUntil(sim, () => Vector2.Distance(w.Units.Pos[eng.Index], after) < 2f, 300));
    }

    [Fact]
    public void CapacityLimitsLoadingBySlots()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var s = Start(w) + new Vector2(8, 8);
        var tr = TestUtil.Spawn(sim, "uef_t1_transport", 0, s);
        var tanks = SpawnRow(sim, "uef_t1_tank", 4, s + new Vector2(8, 0));
        var heavy = TestUtil.Spawn(sim, "uef_t2_heavy_tank", 0, s + new Vector2(8, -6));
        var bot = TestUtil.Spawn(sim, "uef_t3_assault_bot", 0, s + new Vector2(14, -6));
        var all = tanks.Append(heavy).Append(bot).ToArray();
        sim.Step([new IssueOrderCommand(0, all, TransportOrders.Load(tr), false)]);
        RunUntil(sim, () => w.Cargo.UsedSlots(w, tr.Index) >= 6, 500);
        sim.Step();
        Assert.Equal(6, w.Cargo.UsedSlots(w, tr.Index)); // 4 tanks + heavy tank (2); the T3 bot (4) doesn't fit
        Assert.True(Carried(w, heavy));
        Assert.False(Carried(w, bot));
        Assert.False(TransportSystem.CanCarry(w, tr.Index, bot.Index));
        var extra = TestUtil.Spawn(sim, "uef_t1_tank", 0, s + new Vector2(-6, 0));
        Assert.False(TransportSystem.CanCarry(w, tr.Index, extra.Index));
    }

    [Fact]
    public void DestroyedTransportKillsItsCargo()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var s = Start(w) + new Vector2(8, 8);
        var tr = TestUtil.Spawn(sim, "uef_t1_transport", 0, s);
        var tanks = SpawnRow(sim, "uef_t1_tank", 2, s + new Vector2(6, 0));
        sim.Step([new IssueOrderCommand(0, tanks, TransportOrders.Load(tr), false)]);
        Assert.True(RunUntil(sim, () => tanks.All(h => Carried(w, h)), 400));
        int lost = w.Armies[0].Stats.UnitsLost;
        sim.Step([new SelfDestructCommand(0, [tr])]);
        sim.Step();
        Assert.False(w.Units.IsActive(tr));
        Assert.All(tanks, h => Assert.False(w.Units.IsActive(h)));
        Assert.Equal(lost + 3, w.Armies[0].Stats.UnitsLost);
    }

    [Fact]
    public void CarriedUnitsAreUntargetableAndOffTheGrid()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var s = Start(w) + new Vector2(8, 8);
        var tr = TestUtil.Spawn(sim, "uef_t1_transport", 0, s);
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 0, s + new Vector2(4, 0));
        sim.Step([new IssueOrderCommand(0, [tank], TransportOrders.Load(tr), false)]);
        Assert.True(RunUntil(sim, () => Carried(w, tank), 300));
        // Enemy tanks right next to the (landed or hovering) transport, ordered to attack the carried tank.
        var enemies = SpawnRow(sim, "uef_t1_tank", 4, w.Units.Pos[tr.Index] + new Vector2(5, 0), army: 1);
        sim.Step([new IssueOrderCommand(1, enemies, Order.AttackUnit(tank), false)]);
        float hp = w.Units.Health[tank.Index];
        TestUtil.Run(sim, 80);
        Assert.True(Carried(w, tank));
        Assert.Equal(hp, w.Units.Health[tank.Index]);
        foreach (var e in enemies)
        {
            foreach (var ws in w.Units.WeaponsOf(e.Index)) Assert.NotEqual(tank, ws.Target);
            var q = w.Units.Orders[e.Index];
            Assert.True(q.IsEmpty || q.Current.Target != tank);
        }
        var near = new List<int>();
        w.Grid.QueryRadius(w.Units, w.Units.Pos[tank.Index], 3f, near);
        Assert.DoesNotContain(tank.Index, near);
    }

    [Fact]
    public void TransportToDistributesUnitsAmongTransports()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var s = Start(w) + new Vector2(6, 10);
        var t1 = TestUtil.Spawn(sim, "uef_t1_transport", 0, s);
        var t2 = TestUtil.Spawn(sim, "uef_t1_transport", 0, s + new Vector2(5, 0));
        var tanks = SpawnRow(sim, "uef_t1_tank", 5, s + new Vector2(0, 8));
        var heavy = TestUtil.Spawn(sim, "uef_t2_heavy_tank", 0, s + new Vector2(10, 8));
        var cargo = tanks.Append(heavy).ToArray();
        var dest = w.Terrain.Clamp(s + new Vector2(35, -20));
        sim.Step([new IssueOrderCommand(0, cargo.Append(t1).Append(t2).ToArray(), TransportOrders.TransportTo(dest), false)]);

        int SlotsFor(UnitHandle t) => cargo.Where(c => !w.Units.Orders[c.Index].IsEmpty && w.Units.Orders[c.Index].Current.Target == t)
            .Sum(c => w.BpOf(c.Index).CargoSize);
        Assert.All(cargo, c => Assert.Equal(OrderType.Load, w.Units.Orders[c.Index].Current.Type));
        Assert.Equal(7, SlotsFor(t1) + SlotsFor(t2));
        Assert.InRange(SlotsFor(t1), 3, 4); // balanced: most free slots first
        Assert.InRange(SlotsFor(t2), 3, 4);
        Assert.Equal(OrderType.Unload, w.Units.Orders[t1.Index].AsSpan()[^1].Type);

        Assert.True(RunUntil(sim, () => cargo.All(c => !Carried(w, c) && Vector2.Distance(w.Units.Pos[c.Index], dest) < 16f), 1500),
            "everyone delivered");
        Assert.Equal(0, w.Cargo.Count(t1.Index) + w.Cargo.Count(t2.Index));
    }

    [Fact]
    public void FerryRouteShuttlesUnitsInSeveralTrips()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var s = Start(w) + new Vector2(6, 10);
        var tr = TestUtil.Spawn(sim, "uef_t1_transport", 0, s);
        var beacon = w.Terrain.Clamp(s + new Vector2(10, 0));
        var drop = w.Terrain.Clamp(s + new Vector2(30, 30));
        var tanks = SpawnRow(sim, "uef_t1_tank", 8, s + new Vector2(14, 6));
        sim.Step([
            new IssueOrderCommand(0, [tr], TransportOrders.Ferry(beacon, drop), false),
            new IssueOrderCommand(0, tanks, TransportOrders.Ferry(beacon, beacon), false),
        ]);
        var snap = new SimSnapshot();
        sim.WriteSnapshot(snap);
        Assert.Equal(1, snap.FerryCount);
        Assert.True(RunUntil(sim, () => tanks.All(h => !Carried(w, h) && Vector2.Distance(w.Units.Pos[h.Index], drop) < 14f), 3000),
            "8 tanks need two trips of a 6-slot transport");
        Assert.Equal(OrderType.Ferry, w.Units.Orders[tr.Index].Current.Type); // the route keeps running
    }

    [Fact]
    public void AirStagingRepairsRefuelsAndReleases()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var site = EconomyTestUtil.FindSite(w, "uef_t2_air_staging", Start(w), 8, 40);
        var pad = TestUtil.Spawn(sim, "uef_t2_air_staging", 0, site);
        var fighter = TestUtil.Spawn(sim, "uef_t1_interceptor", 0, site + new Vector2(30, 0));
        var bp = w.BpOf(fighter.Index);
        w.Units.Health[fighter.Index] = bp.MaxHealth * 0.6f;
        w.Cargo.Fuel[fighter.Index] = 0;
        Assert.Equal(TransportSystem.OutOfFuelSpeed, TransportSystem.AirSpeedFactor(w, fighter.Index, bp));
        sim.Step(); // fresh spawns join the alive list (the nearest-carrier search uses it) on the next tick
        sim.Step([new IssueOrderCommand(0, [fighter], TransportOrders.Dock(UnitHandle.None), false)]);
        Assert.Equal(pad, w.Units.Orders[fighter.Index].Current.Target);
        Assert.True(RunUntil(sim, () => Carried(w, fighter), 400), "docks");
        Assert.True(RunUntil(sim, () => !Carried(w, fighter), 400), "undocks when done");
        Assert.Equal(bp.MaxHealth, w.Units.Health[fighter.Index]);
        Assert.Equal(bp.FuelSeconds, w.Cargo.Fuel[fighter.Index], 1);
        Assert.True(w.Units.Orders[fighter.Index].IsEmpty);
        // Fuel burns while flying.
        float f0 = w.Cargo.Fuel[fighter.Index];
        TestUtil.Run(sim, 50);
        Assert.Equal(f0 - 5f, w.Cargo.Fuel[fighter.Index], 1);
    }

    [Fact]
    public void AirStagingQueuesAircraftBeyondCapacityAndAutoReturn()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var site = EconomyTestUtil.FindSite(w, "uef_t2_air_staging", Start(w), 8, 40);
        var pad = TestUtil.Spawn(sim, "uef_t2_air_staging", 0, site);
        var planes = SpawnRow(sim, "uef_t1_interceptor", 6, site + new Vector2(25, 0));
        foreach (var p in planes) w.Units.Health[p.Index] = 20; // long repair: they stay docked a while
        // Idle and badly damaged: they return by themselves (no order given).
        int maxInside = 0;
        for (int t = 0; t < 600; t++)
        {
            sim.Step();
            maxInside = Math.Max(maxInside, w.Cargo.Count(pad.Index));
            Assert.True(w.Cargo.UsedSlots(w, pad.Index) <= 4);
        }
        Assert.Equal(4, maxInside);
        Assert.True(planes.Count(p => w.Units.Health[p.Index] > 100) >= 4, "repaired in turns");
    }

    [Fact]
    public void AtlantisBuildsLaunchesAndCarriesAircraft()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var s = Start(w) + new Vector2(0, 14);
        var atl = TestUtil.Spawn(sim, "uef_x_atlantis", 0, s);
        var abp = w.BpOf(atl.Index);
        Assert.True(abp.Has(UnitCategory.Experimental) && abp.Has(UnitCategory.Factory));
        Assert.Contains(abp.Index, w.Blueprints[w.Blueprints.IndexOf("uef_t3_engineer")].BuildIndices);
        int fighterBp = w.Blueprints.IndexOf("uef_t1_interceptor");
        w.Armies[0].Eco.Mass = w.Armies[0].Eco.MassMax;
        sim.Step([new FactoryEnqueueCommand(0, [atl], fighterBp, 2)]);
        var built = new List<int>();
        RunUntil(sim, () =>
        {
            built = EconomyTestUtil.UnitsOf(w, 0, "uef_t1_interceptor").Where(i => w.Units.IsComplete(i)).ToList();
            return built.Count >= 2;
        }, 1500);
        Assert.True(built.Count >= 2, "Atlantis builds aircraft");
        TestUtil.Run(sim, 20);
        foreach (int i in built) Assert.True(w.Units.Elevation[i] > w.Terrain.GetSurfaceHeight(w.Units.Pos[i]) + 3f, "launched into the air");

        var planes = built.Select(i => w.Units.HandleOf(i)).ToArray();
        sim.Step([new IssueOrderCommand(0, planes, TransportOrders.Load(atl), false)]); // Load on an air carrier = Dock
        Assert.True(RunUntil(sim, () => planes.All(p => Carried(w, p)), 400), "aircraft land on the carrier");
        TestUtil.Run(sim, 100);
        Assert.All(planes, p => Assert.True(Carried(w, p))); // KeepDocked: they stay aboard
        sim.Step([new IssueOrderCommand(0, [atl], TransportOrders.Unload(w.Units.Pos[atl.Index]), false)]);
        sim.Step();
        Assert.All(planes, p => Assert.False(Carried(w, p)));
    }

    [Fact]
    public void DeterministicAcrossThreadCountsWithTransports()
    {
        ulong RunWith(int threads)
        {
            int old = Par.MaxThreads;
            Par.MaxThreads = threads;
            try
            {
                var sim = TestUtil.NewSkirmish(seed: 11);
                var w = sim.World;
                var s = Start(w) + new Vector2(6, 10);
                var trs = SpawnRow(sim, "uef_t1_transport", 3, s);
                var tanks = SpawnRow(sim, "uef_t1_tank", 14, s + new Vector2(0, 10));
                var site = EconomyTestUtil.FindSite(w, "uef_t2_air_staging", Start(w), 10, 50);
                TestUtil.Spawn(sim, "uef_t2_air_staging", 0, site);
                var planes = SpawnRow(sim, "uef_t1_interceptor", 6, site + new Vector2(20, 5));
                foreach (var p in planes) w.Units.Health[p.Index] *= 0.5f;
                sim.Step([
                    new IssueOrderCommand(0, tanks.Concat(trs).ToArray(), TransportOrders.TransportTo(w.Terrain.Clamp(s + new Vector2(40, 30))), false),
                    new IssueOrderCommand(0, planes, TransportOrders.Dock(UnitHandle.None), false),
                ]);
                for (int t = 0; t < 500; t++) sim.Step();
                return sim.StateHash();
            }
            finally { Par.MaxThreads = old; }
        }
        Assert.Equal(RunWith(1), RunWith(8));
    }
}

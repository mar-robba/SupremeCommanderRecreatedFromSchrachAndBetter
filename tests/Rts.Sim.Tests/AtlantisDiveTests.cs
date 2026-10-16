using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Naval;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.Transport;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>The Atlantis can dive (Movement.Layer Sub + StartSurfaced): deck closed, only torpedoes fire.</summary>
public class AtlantisDiveTests
{
    private static bool Carried(SimWorld w, UnitHandle h) => w.Units.IsActive(h) && (w.Units.Flags[h.Index] & UnitFlags.Carried) != 0;

    private static void Rich(SimWorld w)
    {
        foreach (var a in w.Armies) { a.Eco.Mass = a.Eco.MassMax; a.Eco.Energy = a.Eco.EnergyMax; }
    }

    private static UnitHandle DivedAtlantis(Simulation sim, float x = 90, float z = 64)
    {
        var atl = NavalWorld.Spawn(sim, "uef_x_atlantis", 0, x, z);
        TestUtil.Run(sim, 40, new SetSubmergedCommand(0, [atl], true));
        Assert.True(Submarines.IsSubmerged(sim.World, atl.Index));
        Assert.Equal(LayerMask.Sub, OrderSystem.LayerOf(sim.World, atl.Index));
        return atl;
    }

    [Fact]
    public void AtlantisStartsSurfacedDivesAndASwapKeepsItDown()
    {
        var sim = NavalWorld.Basin(strip: false);
        var w = sim.World;
        var atl = NavalWorld.Spawn(sim, "uef_x_atlantis", 0, 90, 64);
        Assert.True(Submarines.CanDive(w.BpOf(atl.Index)));
        TestUtil.Run(sim, 5);
        Assert.True(Submarines.IsSurfaced(w, atl.Index), "the Atlantis spawns surfaced");
        Assert.False(Submarines.IsSubmerged(w, atl.Index));
        Assert.Equal(LayerMask.Water, OrderSystem.LayerOf(w, atl.Index));
        Assert.Equal(w.Terrain.WaterLevel, w.Units.Elevation[atl.Index], 3);

        TestUtil.Run(sim, 40, new SetSubmergedCommand(0, [atl], true));
        Assert.Equal(w.Terrain.WaterLevel - NavalMotion.SubDepth, w.Units.Elevation[atl.Index], 3);
        Assert.Equal(LayerMask.Sub, OrderSystem.LayerOf(w, atl.Index));

        // A blueprint swap re-runs every InitUnit: it must not bring a dived diver back up.
        UnitLifecycle.OnSpawn(w, atl.Index, w.BpOf(atl.Index));
        Assert.False(Submarines.IsSurfaced(w, atl.Index));
        Assert.Equal(w.Terrain.WaterLevel - NavalMotion.SubDepth, w.Units.Elevation[atl.Index], 3);

        // A fresh sub still starts submerged.
        var sub = NavalWorld.Spawn(sim, "uef_t1_sub", 0, 90, 30);
        Assert.False(Submarines.IsSurfaced(w, sub.Index));

        TestUtil.Run(sim, 40, new SetSubmergedCommand(0, [atl], false));
        Assert.Equal(LayerMask.Water, OrderSystem.LayerOf(w, atl.Index));
        Assert.Equal(w.Terrain.WaterLevel, w.Units.Elevation[atl.Index], 3);
    }

    [Fact]
    public void DivedAtlantisHoldsSamFireButKeepsFiringTorpedoes()
    {
        var sim = NavalWorld.Basin(strip: false);
        var w = sim.World;
        var atl = DivedAtlantis(sim);
        var plane = NavalWorld.Spawn(sim, "uef_t1_air_scout", 1, 90, 90); // ~26 away: in SAM range
        var sub = NavalWorld.Spawn(sim, "uef_t1_sub", 1, 110, 64);         // 20 away: in torpedo range
        int atlBp = w.Units.Bp[atl.Index];
        bool torpedo = false, missile = false;
        for (int t = 0; t < 120; t++)
        {
            sim.Step();
            Assert.True(CombatWorld.Weapon(sim, atl, 0).Target.IsNone, "SAMs acquired a target while submerged");
            for (int p = 0; p < w.Projectiles.Capacity; p++)
            {
                if (!w.Projectiles.Alive[p] || w.Projectiles.SourceBp[p] != atlBp) continue;
                if ((w.Projectiles.Flags[p] & ProjectileFlags.Torpedo) != 0) torpedo = true;
                else missile = true;
            }
        }
        Assert.True(torpedo, "torpedoes keep firing while submerged");
        Assert.False(missile, "a SAM was fired while submerged");
        Assert.True(CombatWorld.Alive(sim, plane));

        // Surfaced: the SAMs engage again.
        sim.Step([new SetSubmergedCommand(0, [atl], false)]);
        bool engaged = false;
        for (int t = 0; t < 120 && !engaged; t++)
        {
            sim.Step();
            engaged = CombatWorld.Weapon(sim, atl, 0).Target == plane || !CombatWorld.Alive(sim, plane);
        }
        Assert.True(engaged, "surfaced SAMs should engage the aircraft");
        _ = sub;
    }

    [Fact]
    public void AircraftCannotDockOnADivedAtlantisAndWaitsForIt()
    {
        var sim = NavalWorld.Basin(strip: false);
        var w = sim.World;
        var atl = DivedAtlantis(sim);
        var fighter = NavalWorld.Spawn(sim, "uef_t1_interceptor", 0, 90, 40);
        sim.Step();
        // No auto-dock / nearest-carrier search finds a dived carrier.
        Assert.True(TransportSystem.FindAirCarrier(w, fighter.Index, w.BpOf(fighter.Index), needRoom: false).IsNone);

        sim.Step([new IssueOrderCommand(0, [fighter], TransportOrders.Dock(atl), false)]);
        TestUtil.Run(sim, 200);
        Assert.False(Carried(w, fighter), "docked on a submerged carrier");
        Assert.Equal(OrderType.Dock, w.Units.Orders[fighter.Index].Current.Type);
        Assert.True(Vector2.Distance(w.Units.Pos[fighter.Index], w.Units.Pos[atl.Index]) < 25f, "should wait near the carrier");

        sim.Step([new SetSubmergedCommand(0, [atl], false)]);
        bool docked = false;
        for (int t = 0; t < 200 && !docked; t++) { sim.Step(); docked = Carried(w, fighter); }
        Assert.True(docked, "the fighter docks once the Atlantis surfaced");
    }

    [Fact]
    public void AircraftBuiltWhileDivedGoesAboardAndLaunchesAfterSurfacing()
    {
        var sim = NavalWorld.Basin(strip: true);
        var w = sim.World;
        var u = w.Units;
        NavalWorld.Spawn(sim, "uef_commander", 0, 64, 110); // storage, so Rich() gives the army resources
        var atl = DivedAtlantis(sim);
        int fighterBp = TestUtil.Db.IndexOf("uef_t1_interceptor");
        Rich(w);
        sim.Step([new FactoryEnqueueCommand(0, [atl], fighterBp, 1)]);
        var built = UnitHandle.None;
        for (int t = 0; t < 3000; t++)
        {
            Rich(w);
            sim.Step();
            var c = u.Factory[atl.Index].Current;
            if (!c.IsNone) built = c;
            else if (!built.IsNone) break; // released this tick
        }
        Assert.True(u.IsActive(built) && u.IsComplete(built.Index), "the Atlantis built the fighter");
        Assert.True(Carried(w, built), "a fighter finished under water goes aboard");
        Assert.Equal(atl.Index, w.Cargo.Carrier[built.Index]);
        Assert.Equal(OrderType.Dock, u.Orders[built.Index].Current.Type);
        // An enemy AA ship in range: nothing may lock onto the aircraft while it is aboard.
        NavalWorld.Spawn(sim, "uef_t1_frigate", 1, 90, 84);
        TestUtil.Run(sim, 100);
        Assert.True(Carried(w, built), "the deck stays closed while submerged");
        for (int k = 0; k < u.Capacity; k++)
        {
            if (!u.IsActive(k) || u.Army[k] != 1) continue;
            foreach (var ws in u.WeaponsOf(k)) Assert.NotEqual(built, ws.Target);
        }

        sim.Step([new SetSubmergedCommand(0, [atl], false)]);
        bool launched = false;
        for (int t = 0; t < 100 && !launched; t++) { sim.Step(); launched = !Carried(w, built); }
        Assert.True(launched, "the docked fighter takes off once the Atlantis is up");
        TestUtil.Run(sim, 30);
        Assert.True(u.Elevation[built.Index] > w.Terrain.WaterLevel + 3f, "flying");
    }

    [Fact]
    public void DivedAtlantisWithAFullHoldWaitsToBuild()
    {
        var sim = NavalWorld.Basin(strip: true);
        var w = sim.World;
        var u = w.Units;
        NavalWorld.Spawn(sim, "uef_commander", 0, 64, 110);
        var atl = NavalWorld.Spawn(sim, "uef_x_atlantis", 0, 90, 64);
        int cap = w.BpOf(atl.Index).Cargo!.Capacity;
        var planes = new List<UnitHandle>();
        for (int k = 0; k < cap; k++) planes.Add(NavalWorld.Spawn(sim, "uef_t1_interceptor", 0, 80 + k % 6 * 3, 30 + k / 6 * 3));
        sim.Step();
        sim.Step([new IssueOrderCommand(0, planes.ToArray(), TransportOrders.Dock(atl), false)]);
        for (int t = 0; t < 600 && !planes.All(p => Carried(w, p)); t++) sim.Step();
        Assert.All(planes, p => Assert.True(Carried(w, p)));

        TestUtil.Run(sim, 40, new SetSubmergedCommand(0, [atl], true));
        Rich(w);
        sim.Step([new FactoryEnqueueCommand(0, [atl], TestUtil.Db.IndexOf("uef_t1_interceptor"), 1)]);
        for (int t = 0; t < 400; t++) { Rich(w); sim.Step(); }
        var cur = u.Factory[atl.Index].Current;
        Assert.False(cur.IsNone, "the build was started");
        Assert.True(u.BuildFraction[cur.Index] < 0.01f, "production must wait while the hold is full and the deck closed");
    }
}

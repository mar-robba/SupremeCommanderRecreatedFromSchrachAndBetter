using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>(6b) Team play: give units / resources to allies, allied storage overflow.</summary>
public class TeamPlayTests
{
    /// <summary>3 armies: 0 and 1 allies (team 0), 2 enemy (team 1).</summary>
    private static Simulation ThreeArmies() => GameOptionsTests.Make(new GameOptions(), players: 3, teams: [0, 0, 1]);

    private static UnitHandle Tank(SimWorld w, int army, float dx) =>
        w.SpawnUnit(GameOptionsTests.Role(w, army, FactionRoles.TankT1), army, EconomyTestUtil.CommanderPos(w, army) + new Vector2(dx, 6), 0, complete: true);

    [Fact]
    public void GiveUnitsOnlyOwnToLivingAlliesNeverCommanders()
    {
        var sim = ThreeArmies();
        var w = sim.World;
        var u = w.Units;
        var t1 = Tank(w, 0, 4);
        var t2 = Tank(w, 0, 8);
        var enemyTank = Tank(w, 2, 4);
        var cmd = w.Armies[0].Commander;
        int count0 = w.Armies[0].UnitCount, count1 = w.Armies[1].UnitCount;
        sim.Step([
            new GiveUnitsCommand(0, [t1, cmd, enemyTank], 1), // own tank yes; commander and someone else's unit no
            new GiveUnitsCommand(0, [t2], 2), // not to an enemy
            new GiveUnitsCommand(0, [t2], 0), // not to itself
            new GiveUnitsCommand(0, [t2], 9), // out of range
        ]);
        Assert.Equal(1, u.Army[t1.Index]);
        Assert.Equal(0, u.Army[cmd.Index]);
        Assert.Equal(2, u.Army[enemyTank.Index]);
        Assert.Equal(0, u.Army[t2.Index]);
        Assert.Equal(count0 - 1, w.Armies[0].UnitCount);
        Assert.Equal(count1 + 1, w.Armies[1].UnitCount);
        // Not to a defeated ally.
        w.Armies[1].Defeated = true;
        sim.Step([new GiveUnitsCommand(0, [t2], 1)]);
        Assert.Equal(0, u.Army[t2.Index]);
    }

    [Fact]
    public void GiveUnitsRespectsTheReceiversCap()
    {
        var sim = ThreeArmies();
        var w = sim.World;
        var tanks = new[] { Tank(w, 0, 3), Tank(w, 0, 6), Tank(w, 0, 9) };
        sim.Step();
        w.Armies[1].UnitCap = w.Armies[1].UnitCount + 2;
        sim.Step([new GiveUnitsCommand(0, tanks, 1)]);
        Assert.Equal(1, w.Units.Army[tanks[0].Index]);
        Assert.Equal(1, w.Units.Army[tanks[1].Index]);
        Assert.Equal(0, w.Units.Army[tanks[2].Index]); // beyond the cap: stays
        Assert.Equal(w.Armies[1].UnitCap, w.Armies[1].UnitCount);
    }

    [Fact]
    public void GiveFactoryTakesItsUnfinishedUnitAlongAndCountsItForTheCap()
    {
        var sim = ThreeArmies();
        var w = sim.World;
        var u = w.Units;
        int facBp = GameOptionsTests.Role(w, 0, FactionRoles.LandFactoryT1);
        var fac = w.SpawnUnit(facBp, 0, EconomyTestUtil.FindSite(w, w.Blueprints[facBp].Id, EconomyTestUtil.CommanderPos(w, 0), 12, 40), 0, complete: true);
        sim.Step([new FactoryEnqueueCommand(0, [fac], GameOptionsTests.Role(w, 0, FactionRoles.TankT1), 1)]);
        EconomyTestUtil.Ticks(sim, 3);
        var cur = u.Factory[fac.Index].Current;
        Assert.True(u.IsActive(cur));
        Assert.False(u.IsComplete(cur.Index));
        // The unfinished unit alone can't be given (it goes with its factory).
        sim.Step([new GiveUnitsCommand(0, [cur], 1)]);
        Assert.Equal(0, u.Army[cur.Index]);
        // Room for one unit only: the factory + its unit (2) doesn't fit.
        w.Armies[1].UnitCap = w.Armies[1].UnitCount + 1;
        sim.Step([new GiveUnitsCommand(0, [fac], 1)]);
        Assert.Equal(0, u.Army[fac.Index]);
        w.Armies[1].UnitCap = 1000;
        int count1 = w.Armies[1].UnitCount;
        sim.Step([new GiveUnitsCommand(0, [fac], 1)]);
        Assert.Equal(1, u.Army[fac.Index]);
        Assert.Equal(1, u.Army[cur.Index]);
        Assert.Equal(count1 + 2, w.Armies[1].UnitCount);
    }

    [Fact]
    public void GiveResourcesClampsToStoredAndReceiverRoom()
    {
        var w = ThreeArmies().World;
        var a0 = w.Armies[0].Eco;
        var a1 = w.Armies[1].Eco;
        a0.Mass = 300; a0.Energy = 1000;
        a1.Mass = a1.MassMax - 100; a1.Energy = 0;
        DiplomacySystem.Apply(w, new GiveResourcesCommand(0, 1, 1e9f, 400));
        Assert.Equal(200, a0.Mass, 3); // only 100 mass fit
        Assert.Equal(a1.MassMax, a1.Mass, 3);
        Assert.Equal(600, a0.Energy, 3);
        Assert.Equal(400, a1.Energy, 3);
        Assert.Equal(100, w.Armies[0].Stats.MassGiven, 3);
        Assert.Equal(400, w.Armies[1].Stats.EnergyReceived, 3);
        // More than stored: everything stored.
        a1.Mass = 0;
        DiplomacySystem.Apply(w, new GiveResourcesCommand(0, 1, 5000, 0));
        Assert.Equal(0, a0.Mass, 3);
        Assert.Equal(200, a1.Mass, 3);
        // NaN / negative / to an enemy / to itself: nothing.
        a0.Mass = 300;
        DiplomacySystem.Apply(w, new GiveResourcesCommand(0, 1, float.NaN, -50));
        DiplomacySystem.Apply(w, new GiveResourcesCommand(0, 2, 100, 100));
        DiplomacySystem.Apply(w, new GiveResourcesCommand(0, 0, 100, 100));
        DiplomacySystem.Apply(w, new GiveResourcesCommand(0, -1, 100, 100));
        Assert.Equal(300, a0.Mass, 3);
        Assert.Equal(600, a0.Energy, 3);
        Assert.Equal(200, a1.Mass, 3);
    }

    [Fact]
    public void GiveResourcesGoesThroughTheCommandStream()
    {
        var sim = ThreeArmies();
        var w = sim.World;
        w.Armies[1].Eco.Mass = 0;
        sim.Step([new GiveResourcesCommand(0, 1, 250, 0)]);
        Assert.Equal(250, w.Armies[1].Stats.MassReceived - OverflowFrom0(w), 1);
        Assert.True(w.Armies[1].Eco.Mass >= 250);

        static float OverflowFrom0(SimWorld w) => w.Armies[0].Stats.MassGiven - 250; // army 0 overflows while full
    }

    [Fact]
    public void OverflowIsSplitAmongAlliesWithRoomAndNeverGoesToEnemies()
    {
        var w = GameOptionsTests.Make(new GameOptions(), players: 4, teams: [0, 0, 0, 1]).World;
        float max = w.Armies[0].Eco.MassMax;
        void Set(float m0, float m1, float m2, float m3)
        {
            w.Armies[0].Eco.Mass = m0; w.Armies[1].Eco.Mass = m1; w.Armies[2].Eco.Mass = m2; w.Armies[3].Eco.Mass = m3;
            foreach (var a in w.Armies) { a.Eco.Energy = a.Eco.EnergyMax; a.Stats.MassReceived = 0; a.Stats.MassGiven = 0; }
        }
        void Overflow(float mass)
        {
            EconomyShare.NoteOverflow(w, 0, mass, 0);
            for (int a = 1; a < 4; a++) EconomyShare.NoteOverflow(w, a, -1, -1);
            EconomyShare.Distribute(w);
        }

        // Equal split, a full ally's share passed on to the one with room.
        Set(max, max - 50, 0, 0);
        Overflow(200);
        Assert.Equal(50, w.Armies[1].Stats.MassReceived, 3);
        Assert.Equal(150, w.Armies[2].Stats.MassReceived, 3);
        Assert.Equal(max, w.Armies[1].Eco.Mass, 3);
        Assert.Equal(150, w.Armies[2].Eco.Mass, 3);
        Assert.Equal(0, w.Armies[3].Eco.Mass); // enemy
        Assert.Equal(200, w.Armies[0].Stats.MassGiven, 3);

        // Plenty of room: half each.
        Set(max, 0, 0, 0);
        Overflow(100);
        Assert.Equal(50, w.Armies[1].Eco.Mass, 3);
        Assert.Equal(50, w.Armies[2].Eco.Mass, 3);

        // A full ally receives nothing; when all are full the rest is lost.
        Set(max, max, 0, 0);
        Overflow(80);
        Assert.Equal(0, w.Armies[1].Stats.MassReceived);
        Assert.Equal(80, w.Armies[2].Eco.Mass, 3);
        Set(max, max, max - 10, 0);
        Overflow(80);
        Assert.Equal(10, w.Armies[0].Stats.MassGiven, 3);
        Assert.Equal(max, w.Armies[2].Eco.Mass, 3);

        // A defeated ally receives nothing.
        Set(max, 0, 0, 0);
        w.Armies[1].Defeated = true;
        Overflow(100);
        Assert.Equal(0, w.Armies[1].Eco.Mass);
        Assert.Equal(100, w.Armies[2].Eco.Mass, 3);
    }

    [Fact]
    public void FullArmyOverflowReachesItsAllyInAGameTick()
    {
        var sim = ThreeArmies();
        var w = sim.World;
        sim.Step();
        // Army 0 is full (start storage) and its commander produces: the overflow goes to ally 1, never to enemy 2.
        w.Armies[1].Eco.Mass = 0;
        w.Armies[2].Eco.Mass = 0;
        EconomyTestUtil.Ticks(sim, 20);
        Assert.True(w.Armies[1].Stats.MassReceived > 0);
        Assert.Equal(0, w.Armies[2].Stats.MassReceived);
        Assert.Equal(w.Armies[0].Stats.MassGiven + w.Armies[1].Stats.MassGiven,
            w.Armies[0].Stats.MassReceived + w.Armies[1].Stats.MassReceived, 2);
        // FFA: nobody shares.
        var ffa = TestUtil.NewSkirmish();
        EconomyTestUtil.Ticks(ffa, 20);
        Assert.All(ffa.World.Armies, a => Assert.Equal(0, a.Stats.MassGiven + a.Stats.EnergyGiven));
    }

    [Fact]
    public void TwoVersusTwoWithOverflowIsDeterministicAcrossThreadCounts()
    {
        (ulong Hash, float Shared) RunWith(int threads) => PersistenceTestUtil.WithThreads(threads, () =>
        {
            var sim = GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings
            {
                Seed = 9, Players = 4, Teams = [0, 1, 0, 1], AiPlayers = [true, true, true, true],
                AiDifficulties = [AiDifficulty.Hard, AiDifficulty.Normal, AiDifficulty.Normal, AiDifficulty.Hard],
                Options = new GameOptions { Resources = StartResources.High },
            });
            for (int t = 0; t < 1500; t++) sim.Step();
            float shared = sim.World.Armies.Sum(a => a.Stats.MassReceived + a.Stats.EnergyReceived);
            return (sim.StateHash(), shared);
        });
        var one = RunWith(1);
        var four = RunWith(4);
        Assert.True(one.Shared > 0, "the game should have shared some overflow");
        Assert.Equal(one.Hash, four.Hash);
        Assert.Equal(one.Shared, four.Shared);
    }
}

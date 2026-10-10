using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

public class ConstructionTests
{
    private static (Simulation sim, UnitHandle factory) WithFactory(int seed = 42)
    {
        var sim = TestUtil.NewSkirmish(seed);
        var w = sim.World;
        var site = FindSite(w, "uef_t1_land_factory", CommanderPos(w), 10);
        var f = TestUtil.Spawn(sim, "uef_t1_land_factory", 0, site);
        sim.Step();
        return (sim, f);
    }

    [Fact]
    public void FactoryProducesTankThatLeavesWithRallyOrder()
    {
        var (sim, f) = WithFactory();
        var w = sim.World;
        // Factories face +X (heading 0): rally a bit beyond the exit.
        var rally = w.Units.Pos[f.Index] + new Vector2(11, 2);
        TestUtil.Run(sim, 10,
            new IssueOrderCommand(0, [f], Order.MoveTo(rally), false),
            new FactoryEnqueueCommand(0, [f], Bp("uef_t1_tank"), 1));

        // While building: the tank sits inside the factory, under construction.
        var cur = w.Units.Factory[f.Index].Current;
        Assert.True(w.Units.IsActive(cur));
        Assert.False(w.Units.IsComplete(cur.Index));
        Assert.True(w.Units.BuildFraction[cur.Index] > 0);
        Assert.Equal(w.Units.Pos[f.Index], w.Units.Pos[cur.Index]);
        Assert.NotEqual(0, (int)(w.Units.Flags[f.Index] & UnitFlags.Building));

        // Tank: BuildTime 280 / factory BuildRate 20 = 14 s.
        Ticks(sim, 145);
        Assert.True(w.Units.IsComplete(cur.Index));
        Assert.True(w.Units.Factory[f.Index].Queue!.Count == 0);
        Assert.True(w.Units.Factory[f.Index].Current.IsNone);
        Assert.Equal(1, w.Units.Orders[f.Index].Count); // the factory keeps its rally orders

        Ticks(sim, 150);
        Assert.True(Vector2.Distance(w.Units.Pos[cur.Index], rally) < 2.5f, $"tank at {w.Units.Pos[cur.Index]}, rally {rally}");
    }

    [Fact]
    public void FactoryWithoutRallySendsUnitOutOfTheExit()
    {
        var (sim, f) = WithFactory();
        var w = sim.World;
        TestUtil.Run(sim, 50, new FactoryEnqueueCommand(0, [f], Bp("uef_t1_scout"), 1)); // 40 / 20 = 2 s
        var scouts = UnitsOf(w, 0, "uef_t1_scout");
        Assert.Single(scouts);
        Assert.True(w.Units.IsComplete(scouts[0]));
        var bp = TestUtil.Db["uef_t1_land_factory"];
        Assert.True(Vector2.Distance(w.Units.Pos[scouts[0]], w.Units.Pos[f.Index]) > bp.FootprintX * 0.5f + 0.4f);
    }

    [Fact]
    public void RepeatQueueKeepsProducing()
    {
        var (sim, f) = WithFactory();
        var w = sim.World;
        TestUtil.Run(sim, 110,
            new FactoryEnqueueCommand(0, [f], Bp("uef_t1_scout"), 1),
            new FactoryRepeatCommand(0, [f], true));
        // Scout: 2 s each => about 5 in 11 s.
        int n = UnitsOf(w, 0, "uef_t1_scout").Count;
        Assert.InRange(n, 4, 6);
        Assert.Single(w.Units.Factory[f.Index].Queue!);
    }

    [Fact]
    public void DequeueCancelsAndRefunds()
    {
        var (sim, f) = WithFactory();
        var w = sim.World;
        w.Armies[0].Eco.Mass = 200; // storage not full, so the refund is not clipped
        TestUtil.Run(sim, 60, new FactoryEnqueueCommand(0, [f], Bp("uef_t1_tank"), 2));
        var cur = w.Units.Factory[f.Index].Current;
        Assert.True(w.Units.IsActive(cur));
        float massBefore = w.Armies[0].Eco.Mass;
        float spent = 56f * w.Units.BuildFraction[cur.Index];
        TestUtil.Run(sim, 1, new FactoryDequeueCommand(0, [f], Bp("uef_t1_tank"), 2));
        Assert.False(w.Units.IsActive(cur));
        Assert.Empty(w.Units.Factory[f.Index].Queue!);
        Assert.True(w.Armies[0].Eco.Mass > massBefore + spent * 0.9f);
        Assert.Equal(0, w.Props.Count);
    }

    [Fact]
    public void EngineerAssistingFactorySpeedsItUp()
    {
        float Progress(bool assist)
        {
            var (sim, f) = WithFactory();
            var w = sim.World;
            var cmds = new List<Command> { new FactoryEnqueueCommand(0, [f], Bp("uef_t1_tank"), 1) };
            if (assist)
            {
                var dir = Vector2.Normalize(w.Units.Pos[f.Index] - CommanderPos(w));
                var eng = TestUtil.Spawn(sim, "uef_t1_engineer", 0, w.Units.Pos[f.Index] + dir * 5f);
                cmds.Add(new IssueOrderCommand(0, [eng], Order.GuardUnit(f), false));
            }
            TestUtil.Run(sim, 80, cmds.ToArray());
            return w.Units.BuildFraction[w.Units.Factory[f.Index].Current.Index];
        }

        float alone = Progress(false);
        float assisted = Progress(true);
        // Factory 20 + engineer 5 build power => 1.25x.
        Assert.InRange(assisted / alone, 1.18f, 1.27f);
    }

    [Fact]
    public void FactoryDeathKillsUnfinishedUnit()
    {
        var (sim, f) = WithFactory();
        var w = sim.World;
        TestUtil.Run(sim, 20, new FactoryEnqueueCommand(0, [f], Bp("uef_t1_tank"), 1));
        var cur = w.Units.Factory[f.Index].Current;
        Assert.True(w.Units.IsActive(cur));
        w.KillUnit(f.Index, UnitHandle.None);
        sim.Step();
        Assert.False(w.Units.IsActive(cur));
        Assert.Equal(1, w.Props.Count); // the factory's wreck only
    }

    [Fact]
    public void SecondEngineerAssistsExistingSite()
    {
        float Progress(int engineers, out int sites)
        {
            var sim = TestUtil.NewSkirmish();
            var w = sim.World;
            var site = FindSite(w, "uef_t1_power_generator", CommanderPos(w), 10);
            var list = new List<UnitHandle>();
            for (int k = 0; k < engineers; k++)
                list.Add(TestUtil.Spawn(sim, "uef_t1_engineer", 0, site + new Vector2(-3.5f, -1f + 2f * k)));
            sim.Step();
            // Same build order to every engineer: the first places the site, the other assists it.
            TestUtil.Run(sim, 60, new IssueOrderCommand(0, list.ToArray(), Order.BuildAt(Bp("uef_t1_power_generator"), site), false));
            var pg = UnitsOf(w, 0, "uef_t1_power_generator");
            sites = pg.Count;
            return w.Units.BuildFraction[pg[0]];
        }

        float one = Progress(1, out int s1);
        float two = Progress(2, out int s2);
        Assert.Equal(1, s1);
        Assert.Equal(1, s2);
        Assert.InRange(two / one, 1.85f, 2.05f);
    }

    [Fact]
    public void InvalidPlacementDropsOrder()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        sim.Step();
        var cmd = w.Armies[0].Commander;
        var site = FindSite(w, "uef_t1_power_generator", CommanderPos(w), 3, 6); // flat, but no deposit
        TestUtil.Run(sim, 30, new IssueOrderCommand(0, [cmd], Order.BuildAt(Bp("uef_t1_mass_extractor"), site), false));
        Assert.Empty(UnitsOf(w, 0, "uef_t1_mass_extractor"));
        Assert.True(w.Units.Orders[cmd.Index].IsEmpty);
    }

    [Fact]
    public void NewStructurePushesOwnUnitsOutOfFootprint()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var site = FindSite(w, "uef_t1_land_factory", CommanderPos(w), 4, 9);
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 0, site + new Vector2(0.5f, 0.3f));
        sim.Step();
        var cmd = w.Armies[0].Commander;
        TestUtil.Run(sim, 40, new IssueOrderCommand(0, [cmd], Order.BuildAt(Bp("uef_t1_land_factory"), site), false));
        var f = UnitsOf(w, 0, "uef_t1_land_factory");
        Assert.Single(f);
        var (x0, z0, x1, z1) = OccupancyGrid.Footprint(TestUtil.Db["uef_t1_land_factory"], w.Units.Pos[f[0]]);
        var p = w.Units.Pos[tank.Index];
        bool inside = p.X > x0 && p.X < x1 + 1 && p.Y > z0 && p.Y < z1 + 1;
        Assert.False(inside, $"tank at {p} inside footprint {x0},{z0}-{x1},{z1}");
    }

    [Fact]
    public void EngineerRepairsDamagedUnit()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var c = CommanderPos(w);
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 0, c + new Vector2(6, 0));
        var eng = TestUtil.Spawn(sim, "uef_t1_engineer", 0, c + new Vector2(6, 3));
        w.Units.Health[tank.Index] = 100;
        sim.Step();
        w.Armies[0].Eco.Mass = 100;
        // Repair 2/3 of 300 HP at 5/280 of max health per second => ~37 s.
        const int ticks = 420;
        TestUtil.Run(sim, ticks, new IssueOrderCommand(0, [eng], Order.RepairUnit(tank), false));
        Assert.Equal(300f, w.Units.Health[tank.Index], 2);
        Assert.True(w.Units.Orders[eng.Index].IsEmpty);
        // Commander income minus repair cost (2/3 of the tank's mass x RepairCostFraction).
        float expected = 100 + ticks * SimWorld.Dt - 56f * 2f / 3f * Systems.ConstructionSystem.RepairCostFraction;
        Assert.InRange(w.Armies[0].Eco.Mass, expected - 0.3f, expected + 0.3f);
    }

    [Fact]
    public void BuildingIsDeterministicAcrossThreadCounts()
    {
        ulong RunWith(int threads)
        {
            int old = Par.MaxThreads;
            Par.MaxThreads = threads;
            try
            {
                var (sim, f) = WithFactory(seed: 9);
                var w = sim.World;
                var cmd = w.Armies[0].Commander;
                var dep = NearestDeposit(w, CommanderPos(w), Map.DepositType.Mass);
                var eng = TestUtil.Spawn(sim, "uef_t1_engineer", 0, w.Units.Pos[f.Index] + new Vector2(0, 5));
                TestUtil.Run(sim, 300,
                    new FactoryEnqueueCommand(0, [f], Bp("uef_t1_tank"), 3),
                    new FactoryRepeatCommand(0, [f], true),
                    new IssueOrderCommand(0, [cmd], Order.BuildAt(Bp("uef_t1_mass_extractor"), dep), false),
                    new IssueOrderCommand(0, [eng], Order.GuardUnit(f), false));
                return sim.StateHash();
            }
            finally { Par.MaxThreads = old; }
        }

        Assert.Equal(RunWith(1), RunWith(8));
    }
}

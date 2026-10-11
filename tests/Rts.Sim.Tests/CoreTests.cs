using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;

namespace Rts.Sim.Tests;

public class CoreTests
{
    [Fact]
    public void BlueprintsLoadAndResolve()
    {
        var db = TestUtil.Db;
        Assert.True(db.Count > 5);
        var cmd = db["uef_commander"];
        Assert.True(cmd.Has(UnitCategory.Commander));
        Assert.NotEmpty(cmd.BuildIndices);
        foreach (var bp in db.All)
            foreach (var w in bp.Weapons)
                if (w.ProjectileVisual != "antitorpedo") Assert.NotEqual(LayerMask.None, w.TargetMask); // torpedo defense targets torpedoes only
    }

    [Fact]
    public void SkirmishSpawnsCommanders()
    {
        var sim = TestUtil.NewSkirmish();
        sim.Step();
        foreach (var a in sim.World.Armies) Assert.True(sim.World.Units.IsActive(a.Commander));
    }

    [Fact]
    public void TankMovesToTarget()
    {
        var sim = TestUtil.NewSkirmish();
        var start = sim.World.Terrain.StartPositions[0] + new Vector2(5, 5);
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 0, start);
        var dest = start + new Vector2(15, 0);
        TestUtil.Run(sim, 150, new IssueOrderCommand(0, [tank], Order.MoveTo(dest), false));
        Assert.True(Vector2.Distance(sim.World.Units.Pos[tank.Index], dest) < 2f,
            $"tank at {sim.World.Units.Pos[tank.Index]}, wanted {dest}");
    }

    [Fact]
    public void SimulationIsDeterministicAcrossThreadCounts()
    {
        ulong RunWith(int threads)
        {
            int old = Par.MaxThreads;
            Par.MaxThreads = threads;
            try
            {
                var sim = TestUtil.NewSkirmish(seed: 7);
                var w = sim.World;
                int tank = TestUtil.Db.IndexOf("uef_t1_tank");
                var a = new List<UnitHandle>();
                var b = new List<UnitHandle>();
                for (int k = 0; k < 300; k++)
                {
                    a.Add(w.SpawnUnit(tank, 0, new Vector2(60 + k % 20 * 1.5f, 60 + k / 20 * 1.5f), 0));
                    b.Add(w.SpawnUnit(tank, 1, new Vector2(190 - k % 20 * 1.5f, 190 - k / 20 * 1.5f), 0));
                }
                sim.Step([
                    new IssueOrderCommand(0, a.ToArray(), Order.AttackMoveTo(new Vector2(190, 190)), false),
                    new IssueOrderCommand(1, b.ToArray(), Order.AttackMoveTo(new Vector2(60, 60)), false),
                ]);
                for (int t = 0; t < 400; t++) sim.Step();
                return sim.StateHash();
            }
            finally { Par.MaxThreads = old; }
        }

        Assert.Equal(RunWith(1), RunWith(8));
    }
}

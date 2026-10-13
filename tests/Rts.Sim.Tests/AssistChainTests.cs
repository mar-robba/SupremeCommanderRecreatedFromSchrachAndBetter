using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Engineer assist chains (Economy/AssistChain.cs + ConstructionSystem guard-assist).</summary>
public class AssistChainTests
{
    private static (Simulation sim, UnitHandle factory) FactoryWithQueue(int count)
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var start = EconomyTestUtil.CommanderPos(w);
        var site = EconomyTestUtil.FindSite(w, "uef_t1_land_factory", start, 10, 40);
        var f = TestUtil.Spawn(sim, "uef_t1_land_factory", 0, site);
        sim.Step([new FactoryEnqueueCommand(0, [f], EconomyTestUtil.Bp("uef_t1_engineer"), count)]);
        return (sim, f);
    }

    [Fact]
    public void AssistingAnAssisterResolvesToTheRootEvenWhileItWalks()
    {
        var (sim, f) = FactoryWithQueue(3);
        var w = sim.World;
        var fp = w.Units.Pos[f.Index];
        var b = TestUtil.Spawn(sim, "uef_t1_engineer", 0, w.Terrain.Clamp(fp + new Vector2(45, 0))); // far: still walking
        var a = TestUtil.Spawn(sim, "uef_t1_engineer", 0, fp + new Vector2(5, 5));
        sim.Step([
            new IssueOrderCommand(0, [b], Order.GuardUnit(f), false),
            new IssueOrderCommand(0, [a], Order.GuardUnit(b), false),
        ]);
        TestUtil.Run(sim, 15);
        var cur = w.Units.Factory[f.Index].Current;
        Assert.False(cur.IsNone);
        Assert.Equal(BuilderMode.Assist, w.Units.Builder[a.Index].Mode);
        Assert.Equal(cur, w.Units.Builder[a.Index].Target);
        Assert.Equal(OrderType.Guard, w.Units.Orders[a.Index].Current.Type); // A still guards B
    }

    [Fact]
    public void GuardCycleDoesNotHang()
    {
        var (sim, f) = FactoryWithQueue(1);
        var w = sim.World;
        var fp = w.Units.Pos[f.Index];
        var a = TestUtil.Spawn(sim, "uef_t1_engineer", 0, fp + new Vector2(5, 5));
        var b = TestUtil.Spawn(sim, "uef_t1_engineer", 0, fp + new Vector2(6, 5));
        sim.Step([
            new IssueOrderCommand(0, [a], Order.GuardUnit(b), false),
            new IssueOrderCommand(0, [b], Order.GuardUnit(a), false),
        ]);
        TestUtil.Run(sim, 10);
        Assert.NotEqual(BuilderMode.Assist, w.Units.Builder[a.Index].Mode);
    }

    [Fact]
    public void EngineersAssistingAFactoryFollowItsWholeQueue()
    {
        var (sim, f) = FactoryWithQueue(3);
        var w = sim.World;
        var fp = w.Units.Pos[f.Index];
        var a = TestUtil.Spawn(sim, "uef_t1_engineer", 0, fp + new Vector2(5, 5));
        sim.Step([new IssueOrderCommand(0, [a], Order.GuardUnit(f), false)]);
        var seen = new HashSet<UnitHandle>();
        for (int t = 0; t < 900 && seen.Count < 3; t++)
        {
            sim.Step();
            var cur = w.Units.Factory[f.Index].Current;
            if (!cur.IsNone && w.Units.Builder[a.Index].Mode == BuilderMode.Assist && w.Units.Builder[a.Index].Target == cur) seen.Add(cur);
        }
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void AssistingEngineersRepairADamagedFactoryFirst()
    {
        var (sim, f) = FactoryWithQueue(5);
        var w = sim.World;
        var fp = w.Units.Pos[f.Index];
        var a = TestUtil.Spawn(sim, "uef_t1_engineer", 0, fp + new Vector2(5, 5));
        var b = TestUtil.Spawn(sim, "uef_t1_engineer", 0, fp + new Vector2(5, 7));
        sim.Step([
            new IssueOrderCommand(0, [b], Order.GuardUnit(f), false),
            new IssueOrderCommand(0, [a], Order.GuardUnit(b), false),
        ]);
        TestUtil.Run(sim, 10);
        float max = w.BpOf(f.Index).MaxHealth;
        w.Units.Health[f.Index] = max * 0.5f;
        TestUtil.Run(sim, 5);
        Assert.Equal(BuilderMode.Repair, w.Units.Builder[a.Index].Mode);
        Assert.Equal(f, w.Units.Builder[a.Index].Target);
        TestUtil.Run(sim, 60);
        Assert.True(w.Units.Health[f.Index] > max * 0.5f + 1f);
    }
}

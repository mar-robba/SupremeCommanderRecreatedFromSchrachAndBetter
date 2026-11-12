using Rts.Client.UI;
using Rts.Sim.Commander;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;

namespace Rts.Client.Tests;

/// <summary>(10b-2) One-click enhancement swap: the plan (remove + chain) and the client-side step queue.</summary>
public class EnhancementChainTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
    private static readonly UnitBlueprint Base = Db[Db.IndexOf("uef_commander")];
    private static int E(string id) => EnhancementCatalog.Find(Base, id);
    private static UnitBlueprint With(string id) => Db[EnhancementCatalog.WithSlot(Db, Base, Base.Enhancements[E(id)].Slot, E(id))];
    private static readonly UnitHandle Acu = new(5, 1);

    [Fact]
    public void PlanInstallsTheMissingPrerequisiteFirst()
    {
        var steps = EnhancementChain.Plan(Base, E("T3Engineering"), out bool remove);
        Assert.False(remove);
        Assert.Equal(new[] { E("AdvancedEngineering"), E("T3Engineering") }, steps);
        Assert.Equal(new[] { E("T3Engineering") }, EnhancementChain.Plan(With("AdvancedEngineering"), E("T3Engineering"), out remove));
        Assert.False(remove);
    }

    [Fact]
    public void PlanRemovesAnOccupiedSlot()
    {
        var steps = EnhancementChain.Plan(With("ResourceAllocation"), E("T3Engineering"), out bool remove);
        Assert.True(remove);
        Assert.Equal(2, steps.Length);
        Assert.Empty(EnhancementChain.Plan(With("T3Engineering"), E("AdvancedEngineering"), out _)); // installed as a prerequisite
        Assert.Single(EnhancementChain.Plan(With("PersonalShield"), E("TacticalMissilePack"), out remove));
        Assert.True(remove);
    }

    [Fact]
    public void SwapSendsRemoveThenFirstStepAndQueuesTheRest()
    {
        var chain = new EnhancementChain();
        var sent = new List<Command>();
        Assert.True(chain.Begin(Acu, With("ResourceAllocation"), E("T3Engineering"), 0, 100, sent.Add));
        Assert.IsType<RemoveEnhancementCommand>(sent[0]);
        Assert.Equal("AdvancedEngineering", Assert.IsType<EnhanceCommand>(sent[1]).Enhancement);
        Assert.Equal(1, chain.Count);

        // Not started yet (command latency): waits. Then building: waits. Then done: sends the last step, forgets.
        var bp = Base; bool busy = false;
        bool Look(UnitHandle h, out UnitBlueprint b, out bool bz) { b = bp; bz = busy; return true; }
        chain.Update(Look, 0, 105, sent.Add);
        busy = true;
        chain.Update(Look, 0, 110, sent.Add);
        Assert.Equal(2, sent.Count);
        busy = false; bp = With("AdvancedEngineering");
        chain.Update(Look, 0, 200, sent.Add);
        Assert.Equal("T3Engineering", Assert.IsType<EnhanceCommand>(sent[2]).Enhancement);
        Assert.Equal(0, chain.Count);
    }

    [Fact]
    public void CancelledTimedOutOrDeadPlansAreDropped()
    {
        var sent = new List<Command>();
        UnitBlueprint bp = Base; bool busy = false, alive = true;
        bool Look(UnitHandle h, out UnitBlueprint b, out bool bz) { b = bp; bz = busy; return alive; }

        var chain = new EnhancementChain(); // never starts
        chain.Begin(Acu, Base, E("T3Engineering"), 0, 100, sent.Add);
        chain.Update(Look, 0, 100 + EnhancementChain.StartTimeoutTicks + 1, sent.Add);
        Assert.Equal(0, chain.Count);

        chain.Begin(Acu, Base, E("T3Engineering"), 0, 100, sent.Add); // started, then cancelled (idle, not installed)
        busy = true; chain.Update(Look, 0, 101, sent.Add);
        busy = false; chain.Update(Look, 0, 102, sent.Add);
        Assert.Equal(0, chain.Count);

        chain.Begin(Acu, Base, E("T3Engineering"), 0, 100, sent.Add); // unit died
        alive = false; chain.Update(Look, 0, 101, sent.Add);
        Assert.Equal(0, chain.Count);
        Assert.Equal(3, sent.Count); // only the three first steps, never a second one
    }
}

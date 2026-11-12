using Rts.Sim.Commander;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>(10b-2) The client's one-click swap sends RemoveEnhancementCommand + EnhanceCommand in the same tick: the
/// removal completes in the command phase, so the install sees the empty slot and starts at once.</summary>
public class EnhancementSwapTests
{
    [Fact]
    public void RemoveAndEnhanceInOneTickSwapTheSlot()
    {
        var sim = TestUtil.NewSkirmish();
        sim.Step();
        var w = sim.World;
        var acu = w.Armies[0].Commander;
        var bp = w.BpOf(acu.Index);
        int ra = EnhancementCatalog.Find(bp, "ResourceAllocation");
        int target = EnhancementCatalog.WithSlot(w.Blueprints, bp, bp.Enhancements[ra].Slot, ra);
        w.Units.Upgrade[acu.Index] = new UpgradeState { Active = true, TargetBp = target };
        UpgradeSystem.Complete(w, acu.Index);
        Assert.True(EnhancementCatalog.HasInstalled(w.BpOf(acu.Index), ra));

        var slot = bp.Enhancements[ra].Slot;
        sim.Step([new RemoveEnhancementCommand(0, [acu], slot), new EnhanceCommand(0, [acu], "AdvancedEngineering")]);
        var now = w.BpOf(acu.Index);
        Assert.False(EnhancementCatalog.HasInstalled(now, ra));
        Assert.True(w.Units.Upgrade[acu.Index].Active);
        Assert.Equal(EnhancementCatalog.Find(now, "AdvancedEngineering"), EnhancementCatalog.Added(w.Blueprints, w.Units.Bp[acu.Index], w.Units.Upgrade[acu.Index].TargetBp));
    }
}

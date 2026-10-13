using Rts.Sim.Persistence;

namespace Rts.Sim.Tests;

/// <summary>
/// Guard for the save system: every field reachable from the saved state must have a codec, and no sim state may
/// live in static fields. If this fails after you added state, read the message: use plain fields / supported
/// collections, or mark caches and diagnostics [NoSave] (rebuild them in IAfterLoad.AfterLoad).
/// </summary>
public class SaveCoverageTests
{
    [Fact]
    public void EverySimStateTypeIsSaveable()
    {
        var problems = SaveCoverage.Problems();
        Assert.True(problems.Count == 0,
            $"{problems.Count} save-system problem(s) (see docs/ARCHITECTURE.md 'Save/load & replays'):\n" + string.Join("\n", problems));
    }

#pragma warning disable CS0649 // fields only inspected by reflection
    private sealed class BadState
    {
        public int Fine;
        public List<int> AlsoFine = new();
        public Func<int>? Lambda;
        public IEnumerable<int>? Interface;
        public System.Text.StringBuilder? FrameworkClass;
        [NoSave] public System.Text.StringBuilder? Ignored;
    }
#pragma warning restore CS0649

    [Fact]
    public void GuardReportsUnsupportedFields()
    {
        var problems = SaveCoverage.ProblemsFor(typeof(BadState));
        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains(".Lambda") && p.Contains("delegates"));
        Assert.Contains(problems, p => p.Contains(".Interface"));
        Assert.Contains(problems, p => p.Contains("StringBuilder") && p.Contains("not supported"));
    }
}

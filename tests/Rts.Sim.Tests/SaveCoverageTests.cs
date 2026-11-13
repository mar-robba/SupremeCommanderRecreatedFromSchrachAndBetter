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

    /// <summary>(11a-2) A saved field that holds a unit blueprint index must be in <see cref="BlueprintIndexFields.Indices"/>
    /// (so a save made with another blueprint list is translated by id) or, if its name only looks like one, in
    /// <see cref="BlueprintIndexFields.NotIndices"/>. Every listed entry must still exist.</summary>
    [Fact]
    public void EveryBlueprintIndexFieldIsRemapped()
    {
        var listed = BlueprintIndexFields.All(out var missing);
        var notIdx = BlueprintIndexFields.Resolve(BlueprintIndexFields.NotIndices, out var missing2);
        missing.AddRange(missing2);
        Assert.True(missing.Count == 0, "BlueprintIndexFields entries that no longer exist:\n" + string.Join("\n", missing));
        var unlisted = BlueprintIndexFields.ReachableFields()
            .Where(x => BlueprintIndexFields.LooksLikeIndex(x.Field) && !listed.Contains(x.Field) && !notIdx.Contains(x.Field))
            .Select(x => $"(\"{TypeNames.Of(x.Field.DeclaringType!)}\", \"{x.Field.Name}\"), // {x.Field.FieldType.Name}")
            .Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.True(unlisted.Count == 0,
            "Saved fields that look like unit blueprint indices: add each to BlueprintIndexFields.Indices (an index into " +
            "BlueprintDb.All) or NotIndices (src/Rts.Sim/Persistence/SaveBlueprintIndices.cs):\n" + string.Join("\n", unlisted));
        foreach (var f in listed)
            Assert.True(BlueprintIndexFields.SupportedShape(f.FieldType), $"{f.DeclaringType}.{f.Name}: unsupported shape {f.FieldType}");
    }

    /// <summary>(11a-2) The blueprint-derived state the loader rebuilds, drops or reorders on a migrated load is named by
    /// strings: every name must still exist in this build, or a rename would silently skip the rebuild.</summary>
    [Fact]
    public void BlueprintDerivedStateNamesStillExist()
    {
        var keys = WorldState.Tables.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var k in BlueprintIndexFields.DerivedTables)
            Assert.True(keys.Contains(k), $"BlueprintIndexFields.DerivedTables: '{k}' is not a per-world state key (WorldState.Tables)");
        foreach (var (name, make) in BlueprintIndexFields.Derived)
        {
            var t = TypeNames.TryResolve(name);
            Assert.True(t != null, $"BlueprintIndexFields.Derived: type '{name}' does not exist");
            Assert.IsType(t!, make(TestUtil.Db));
        }
        BlueprintIndexFields.Resolve(BlueprintIndexFields.PerBlueprint, out var missing);
        Assert.True(missing.Count == 0, "BlueprintIndexFields.PerBlueprint entries that no longer exist:\n" + string.Join("\n", missing));
        foreach (var f in BlueprintIndexFields.Resolve(BlueprintIndexFields.PerBlueprint, out _))
            Assert.True(f.FieldType.IsArray, $"{f.DeclaringType}.{f.Name}: a per-blueprint field must be an array");
    }
}

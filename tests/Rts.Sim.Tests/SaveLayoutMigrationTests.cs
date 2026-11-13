using Rts.Sim.Persistence;

namespace Rts.Sim.Tests;

/// <summary>
/// (11a-2) Tolerant loading, field by field (ARCHITECTURE "Save migration"). Each case writes an object of an "old"
/// test type (…Old) and reads it back as the "new" one (…New) through a temporary <see cref="SaveRenames.Types"/>
/// entry, i.e. exactly what happens when a sim type changes between the build that saved and the build that loads.
/// Each case uses its own types: the name → type resolution is cached for the process.
/// </summary>
public class SaveLayoutMigrationTests
{
    // ---- helpers ----

    private static byte[] Write(object root)
    {
        var o = new ByteWriter();
        var w = new GraphWriter(o);
        w.WriteRef(root);
        w.Flush();
        return o.ToArray();
    }

    private static object? Read(byte[] data, out SaveMigrationReport? report)
    {
        var r = new GraphReader(new ByteReader(data));
        var o = r.ReadRef();
        r.Complete();
        r.RunAfterLoad();
        report = r.MigrationReport;
        return o;
    }

    /// <summary>Writes <paramref name="old"/>, then reads it with the type renames <paramref name="renames"/> (old → new
    /// type) and field renames in place; the entries are removed again afterwards.</summary>
    private static T Migrate<T>(object old, out SaveMigrationReport report, (Type Old, Type New)[]? renames = null,
        (Type Owner, string Old, string New)[]? fields = null, (Type Old, string? New)[]? gone = null)
    {
        var data = Write(old);
        renames ??= [(old.GetType(), typeof(T))];
        try
        {
            foreach (var (o, n) in renames) SaveRenames.Types[TypeNames.Of(o)] = TypeNames.Of(n);
            foreach (var (o, n) in gone ?? []) SaveRenames.Types[TypeNames.Of(o)] = n ?? "Rts.Sim.Tests:Rts.Sim.Tests.NoSuchType" + o.Name;
            foreach (var (t, o, n) in fields ?? []) SaveRenames.Fields[(TypeNames.Of(t), o)] = n;
            var obj = Read(data, out var rep);
            Assert.NotNull(rep);
            report = rep!;
            return Assert.IsType<T>(obj);
        }
        finally
        {
            foreach (var (o, _) in renames) SaveRenames.Types.Remove(TypeNames.Of(o));
            foreach (var (o, _) in gone ?? []) SaveRenames.Types.Remove(TypeNames.Of(o));
            foreach (var (t, o, _) in fields ?? []) SaveRenames.Fields.Remove((TypeNames.Of(t), o));
        }
    }

    private static string Lines(SaveMigrationReport r) => string.Join(" | ", r.Lines);

    // ---- unchanged layout: the fast path ----

    public sealed class Same { public int A = 1; public float B; public List<int> L = []; public string? S; }

    [Fact]
    public void UnchangedLayoutTakesTheFastPath()
    {
        var o = Assert.IsType<Same>(Read(Write(new Same { A = 7, B = 2.5f, L = [1, 2, 3], S = "x" }), out var rep));
        Assert.Null(rep); // no migration context was ever created: every type read through the generated codecs
        Assert.Equal(7, o.A);
        Assert.Equal(2.5f, o.B);
        Assert.Equal([1, 2, 3], o.L);
        Assert.Equal("x", o.S);
    }

    // ---- field added (initializer value, then the hook) / field removed ----

    public sealed class AddOld { public int Keep = 1; public float Gone = 3; public List<int> GoneList = [5, 6]; }

    public sealed class AddNew : ISaveMigrated
    {
        public int Keep = 1;
        public int Added = 42;
        public List<int> AddedList = [9];
        public int FromHook;
        public SaveMigrationInfo? Info;

        public void OnMigrated(SaveMigrationInfo info)
        {
            Info = info;
            if (!info.Has(nameof(FromHook))) FromHook = Keep * 10;
        }
    }

    [Fact]
    public void AddedFieldTakesItsInitializerThenTheHookAndRemovedFieldIsSkipped()
    {
        var n = Migrate<AddNew>(new AddOld { Keep = 5, Gone = 8, GoneList = [1] }, out var rep);
        Assert.Equal(5, n.Keep);
        Assert.Equal(42, n.Added);
        Assert.Equal([9], n.AddedList);
        Assert.Equal(50, n.FromHook);
        Assert.NotNull(n.Info);
        Assert.Contains(nameof(AddNew.Added), n.Info!.Defaulted);
        Assert.Contains(nameof(AddOld.Gone), n.Info.Dropped);
        Assert.Contains(nameof(AddOld.GoneList), n.Info.Dropped);
        Assert.True(rep.Migrated);
        Assert.Contains(rep.Lines, l => l.Contains("Gone: no longer exists"));
        Assert.Contains(rep.Lines, l => l.Contains("Added: new field"));
        Assert.Contains(rep.Lines, l => l.Contains("renamed to") && l.Contains(nameof(AddNew)));
    }

    // ---- field renamed through the rename table ----

    public sealed class RenOld { public int OldName = 3; public int Other = 4; }
    public sealed class RenNew { public int Other; public int NewName; }

    [Fact]
    public void RenamedFieldIsFoundThroughTheRenameTable()
    {
        var n = Migrate<RenNew>(new RenOld { OldName = 11, Other = 12 }, out var rep,
            fields: [(typeof(RenNew), nameof(RenOld.OldName), nameof(RenNew.NewName))]);
        Assert.Equal(11, n.NewName);
        Assert.Equal(12, n.Other);
        Assert.Contains(rep.Lines, l => l.Contains("OldName: renamed to NewName"));
        Assert.DoesNotContain(rep.Lines, l => l.Contains("no longer exists") || l.Contains("new field"));
    }

    // ---- type renamed (same layout): only the name changes ----

    public sealed class TypeOld { public int A; public long B; }
    public sealed class TypeNew { public int A; public long B; }

    [Fact]
    public void RenamedTypeKeepsItsValues()
    {
        var n = Migrate<TypeNew>(new TypeOld { A = -3, B = 1L << 40 }, out var rep);
        Assert.Equal(-3, n.A);
        Assert.Equal(1L << 40, n.B);
        Assert.Contains(rep.Lines, l => l.Contains("renamed to") && l.Contains(nameof(TypeNew)));
    }

    // ---- retyped with a converter / without one ----

    public enum SmallEnum : byte { A, B, C }
    public sealed class WideOld { public int I = 1; public float F = 1; public byte Bt; public short Sh; public SmallEnum E; public byte ToEnum; }
    public sealed class WideNew { public long I; public double F; public int Bt; public int Sh; public int E; public SmallEnum ToEnum; }

    [Fact]
    public void RetypedFieldGoesThroughTheWideningConverters()
    {
        var n = Migrate<WideNew>(new WideOld { I = int.MinValue, F = 0.1f, Bt = 200, Sh = -300, E = SmallEnum.C, ToEnum = 1 }, out var rep);
        Assert.Equal(int.MinValue, n.I);
        Assert.Equal((double)0.1f, n.F);
        Assert.Equal(200, n.Bt);
        Assert.Equal(-300, n.Sh);
        Assert.Equal(2, n.E);
        Assert.Equal(SmallEnum.B, n.ToEnum);
        Assert.Contains(rep.Lines, l => l.Contains("WideNew.I: saved") && l.Contains("converted"));
        Assert.Contains(rep.Lines, l => l.Contains("WideNew.F: saved") && l.Contains("converted"));
    }

    public sealed class NarrowOld { public float Speed = 2; }
    public sealed class NarrowNew { public int Speed; }

    [Fact]
    public void RetypedFieldWithoutAConverterIsRefusedNamingTypeFieldAndBothTypes()
    {
        var data = Write(new NarrowOld());
        SaveRenames.Types[TypeNames.Of(typeof(NarrowOld))] = TypeNames.Of(typeof(NarrowNew));
        try
        {
            var e = Assert.Throws<IncompatibleSaveException>(() => Read(data, out _));
            Assert.Contains("NarrowNew.Speed", e.Message);
            Assert.Contains("saved", e.Message);
            Assert.Contains("Single", e.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Int32", e.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { SaveRenames.Types.Remove(TypeNames.Of(typeof(NarrowOld))); }
    }

    public sealed class RefRetypeOld { public List<int> Items = [1]; }
    public sealed class RefRetypeNew { public List<string> Items = []; }

    [Fact]
    public void RetypedCollectionIsRefused()
    {
        var data = Write(new RefRetypeOld());
        SaveRenames.Types[TypeNames.Of(typeof(RefRetypeOld))] = TypeNames.Of(typeof(RefRetypeNew));
        try
        {
            var e = Assert.Throws<IncompatibleSaveException>(() => Read(data, out _));
            Assert.Contains("Items", e.Message);
        }
        finally { SaveRenames.Types.Remove(TypeNames.Of(typeof(RefRetypeOld))); }
    }

    // ---- a removed type ----

    public sealed class GoneThing { public int X = 5; public List<int> Xs = [1, 2]; public GoneThing? Next; }
    public struct GoneStruct { public int A; public float B; }
    public sealed class HolderOld
    {
        public int Before = 1;
        public GoneThing? Thing = new() { Next = new GoneThing() };
        public List<GoneThing> Things = [new(), new()];
        public GoneStruct[] Structs = [new() { A = 1 }, new() { A = 2 }];
        public Dictionary<int, GoneThing> ByKey = new() { [1] = new() };
        public object? Boxed = new GoneThing();
        public int After = 2;
    }
    public sealed class HolderNew { public int Before; public object? Boxed; public int After; }

    [Fact]
    public void RemovedTypeIsSkippedWithItsFields()
    {
        var n = Migrate<HolderNew>(new HolderOld { Before = 7, After = 9 }, out var rep,
            renames: [(typeof(HolderOld), typeof(HolderNew))],
            gone: [(typeof(GoneThing), null), (typeof(GoneStruct), null)]);
        Assert.Equal(7, n.Before);
        Assert.Equal(9, n.After); // the bytes of every gone object in between were skipped exactly
        Assert.Null(n.Boxed);     // a kept field whose saved object's type is gone reads as null
        Assert.Contains(rep.Lines, l => l.Contains("Things: no longer exists"));
        Assert.Contains(rep.Lines, l => l.Contains("Boxed") && l.Contains("no longer exists"));
    }

    public class BaseItem { public int V; }
    public sealed class GoneItem : BaseItem { public int W; }
    public sealed class KeptHolder { public List<BaseItem> Items = []; }

    [Fact]
    public void RemovedTypeInsideAnUnchangedContainerIsRefusedNamingTheType()
    {
        // The holder and the list keep their layout (fast path): a gone element type cannot be dropped silently there.
        var data = Write(new KeptHolder { Items = [new BaseItem { V = 1 }, new GoneItem { V = 2 }] });
        SaveRenames.Types[TypeNames.Of(typeof(GoneItem))] = "Rts.Sim.Tests:Rts.Sim.Tests.NoSuchGoneItem";
        try
        {
            var e = Assert.Throws<IncompatibleSaveException>(() => Read(data, out _));
            Assert.Contains("GoneItem", e.Message);
            Assert.Contains("no longer exists", e.Message);
        }
        finally { SaveRenames.Types.Remove(TypeNames.Of(typeof(GoneItem))); }
    }

    // ---- a struct inside an array, a dictionary value type ----

    public struct PtOld { public int X; public int Y; }
    public struct PtNew { public int Y; public long X; public int Z; public PtNew() { Z = -1; } }
    public sealed class ArrOld { public PtOld[] Pts = []; public List<PtOld> PtList = []; public Dictionary<int, PtOld> ByKey = new(); public int Tail; }
    public sealed class ArrNew { public PtNew[] Pts = []; public List<PtNew> PtList = []; public Dictionary<int, PtNew> ByKey = new(); public int Tail; }

    [Fact]
    public void StructInsideArraysListsAndDictionariesIsMigratedElementByElement()
    {
        var old = new ArrOld
        {
            Pts = [new() { X = 1, Y = 2 }, new() { X = 3, Y = 4 }, new() { X = int.MaxValue, Y = -5 }],
            PtList = [new() { X = 10, Y = 20 }],
            ByKey = new() { [7] = new() { X = 70, Y = 77 }, [8] = new() { X = 80, Y = 88 } },
            Tail = 123,
        };
        var n = Migrate<ArrNew>(old, out var rep, renames: [(typeof(ArrOld), typeof(ArrNew)), (typeof(PtOld), typeof(PtNew))]);
        Assert.Equal(3, n.Pts.Length);
        Assert.Equal((2, 1L, -1), (n.Pts[0].Y, n.Pts[0].X, n.Pts[0].Z));
        Assert.Equal((4, 3L, -1), (n.Pts[1].Y, n.Pts[1].X, n.Pts[1].Z));
        Assert.Equal((-5, (long)int.MaxValue), (n.Pts[2].Y, n.Pts[2].X));
        Assert.Equal((20, 10L, -1), (n.PtList.Single().Y, n.PtList.Single().X, n.PtList.Single().Z));
        Assert.Equal([7, 8], n.ByKey.Keys.Order());
        Assert.Equal((77, 70L, -1), (n.ByKey[7].Y, n.ByKey[7].X, n.ByKey[7].Z));
        Assert.Equal(88, n.ByKey[8].Y);
        Assert.Equal(123, n.Tail);
        Assert.Contains(rep.Lines, l => l.Contains("PtNew.X: saved") && l.Contains("converted"));
        Assert.Contains(rep.Lines, l => l.Contains("PtNew.Z: new field"));
    }

    // ---- shared references survive a migration ----

    public sealed class NodeOld { public int V; public NodeOld? Peer; public int Dropped = 4; }
    public sealed class NodeNew { public int V; public NodeNew? Peer; }

    [Fact]
    public void ObjectIdentityAndCyclesSurviveAMigration()
    {
        var a = new NodeOld { V = 1 };
        var b = new NodeOld { V = 2, Peer = a };
        a.Peer = b;
        var list = new List<NodeOld> { a, b, a };
        var data = Write(list);
        SaveRenames.Types[TypeNames.Of(typeof(NodeOld))] = TypeNames.Of(typeof(NodeNew));
        try
        {
            var got = Assert.IsType<List<NodeNew>>(Read(data, out var rep));
            Assert.True(rep!.Migrated);
            Assert.Same(got[0], got[2]);
            Assert.Same(got[1], got[0].Peer);
            Assert.Same(got[0], got[1].Peer);
            Assert.Equal((1, 2), (got[0].V, got[1].V));
        }
        finally { SaveRenames.Types.Remove(TypeNames.Of(typeof(NodeOld))); }
    }
}

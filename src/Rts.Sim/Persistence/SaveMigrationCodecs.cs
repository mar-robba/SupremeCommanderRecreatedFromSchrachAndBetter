using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Rts.Sim.Persistence;

/// <summary>Placeholder for an object whose type no longer exists (its bytes were skipped; references to it read as null).</summary>
internal sealed class DroppedObject
{
    public readonly string TypeName;
    public int Length; // arrays: element count
    public DroppedObject(string typeName) => TypeName = typeName;
}

/// <summary>(11a-2) Reads one value written with a saved layout into this build's type (see <see cref="MigrationContext.For"/>).</summary>
internal abstract class Plan
{
    public abstract object? Read(GraphReader r);
}

internal sealed class SamePlan<T> : Plan
{
    public override object? Read(GraphReader r) { T v = default!; Val<T>.Read(r, ref v); return v; }
}

internal sealed class ConvPlan(Plan inner, Type to) : Plan
{
    public override object? Read(GraphReader r) => SaveLayout.Convert(inner.Read(r)!, to);
}

internal sealed class RefPlan(Type? expected, string where) : Plan
{
    public override object? Read(GraphReader r)
    {
        object? o;
        r.AcceptDropped++;
        try { o = r.ReadRef(); }
        finally { r.AcceptDropped--; }
        if (o is DroppedObject d)
        {
            r.Migration.Report.Add($"{where}: its saved {SaveLayout.Short(d.TypeName)} no longer exists (read as null)");
            return null;
        }
        if (o != null && expected != null && !expected.IsInstanceOfType(o))
            throw new IncompatibleSaveException($"{where}: saved {SaveLayout.Short(o.GetType())}, now {SaveLayout.Short(expected)}");
        return o;
    }
}

internal sealed class SkipPlan(SavedNode n) : Plan
{
    private readonly Plan[]? _fields = n.Kind == NodeKind.Struct && n.Sequential ? n.Fields!.Select(f => (Plan)new SkipPlan(f.Node)).ToArray() : null;

    public override object? Read(GraphReader r)
    {
        switch (n.Kind)
        {
            case NodeKind.Prim: r.In.Take(SaveLayout.PrimSize(n.Type)); break;
            case NodeKind.Enum: r.In.Take(SaveLayout.PrimSize(n.Underlying!)); break;
            case NodeKind.Ref: r.Skipping++; try { r.ReadRef(); } finally { r.Skipping--; } break;
            default:
                if (_fields == null) r.In.Take(n.RawSize);
                else foreach (var f in _fields) f.Read(r);
                break;
        }
        return null;
    }
}

internal sealed class StructPlan : Plan
{
    private readonly object _template;
    private readonly (FieldInfo? Field, Plan Plan)[] _fields;

    public StructPlan(object template, (FieldInfo?, Plan)[] fields) { _template = template; _fields = fields; }

    public override object? Read(GraphReader r)
    {
        object box = RuntimeHelpers.GetObjectValue(_template); // a fresh boxed copy
        foreach (var (f, p) in _fields)
        {
            var v = p.Read(r);
            f?.SetValue(box, v);
        }
        return box;
    }
}

/// <summary>A saved blueprint index (or a collection of them) translated to this build's blueprint order.</summary>
internal sealed class RemapPlan(Plan inner, MigrationContext ctx, string where) : Plan
{
    public override object? Read(GraphReader r)
    {
        var v = inner.Read(r);
        return v switch
        {
            int i => ctx.MapBlueprint(i, where),
            short s => (short)ctx.MapBlueprint(s, where),
            null => null,
            _ => ctx.RemapLater(v, where),
        };
    }
}

/// <summary>A per-blueprint array (<see cref="BlueprintIndexFields.PerBlueprint"/>): the owner's field is replaced by the
/// array in this build's blueprint order once the bodies are read (<see cref="MigClassCodec"/>).</summary>
internal sealed class PermutePlan(Plan inner) : Plan
{
    public override object? Read(GraphReader r) => inner.Read(r);

    public void Later(MigrationContext ctx, object owner, FieldInfo f, object? saved)
    {
        f.SetValue(owner, saved);
        if (saved is Array a) ctx.PostFixups.Add(() => f.SetValue(owner, ctx.Permute(a)));
    }
}

/// <summary>(11a-2) Per-load state of a tolerant read: renames, read plans per saved type, the report.</summary>
internal sealed class MigrationContext
{
    public readonly SaveMigrationReport Report = new();
    private readonly Dictionary<string, Type?> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, object> _sigNodes = new();
    private readonly List<(ISaveMigrated Obj, SaveMigrationInfo Info)> _migrated = new();
    private readonly HashSet<object> _remapped = new(ReferenceEqualityComparer.Instance);
    internal readonly List<Action> PostFixups = new();
    private int[]? _bpMap;
    private string[]? _bpSavedIds;

    /// <summary>Saved blueprint index → this build's index (-1 = the id is gone); null = same blueprint list.</summary>
    public void SetBlueprintMap(int[] map, string[] savedIds) { _bpMap = map; _bpSavedIds = savedIds; }

    /// <summary>Set when any blueprint differs: objects of <see cref="BlueprintIndexFields.Derived"/> types are rebuilt from it.</summary>
    public Data.BlueprintDb? RebuildFrom;

    private bool IsRebuilt(Type t) => RebuildFrom != null && BlueprintIndexFields.Derived.ContainsKey(TypeNames.Of(t));

    public bool Forced(Type t) => _bpMap != null && BlueprintIndexFields.Affects(t) || IsRebuilt(t);

    /// <summary>A per-blueprint array (saved index order) in this build's order; other lengths are left as they are.</summary>
    public Array Permute(Array saved)
    {
        if (_bpMap == null || saved.Length != _bpMap.Length) return saved;
        var now = Array.CreateInstance(saved.GetType().GetElementType()!, RebuildFrom!.All.Count);
        for (int i = 0; i < saved.Length; i++)
            if (_bpMap[i] >= 0) now.SetValue(saved.GetValue(i), _bpMap[i]);
        return now;
    }

    public int MapBlueprint(int saved, string where)
    {
        if (_bpMap == null || saved < 0) return saved;
        if (saved >= _bpMap.Length) throw ByteReader.Corrupt($"blueprint index {saved} at {where}");
        int now = _bpMap[saved];
        if (now < 0)
            throw new IncompatibleSaveException($"This game uses the unit blueprint '{_bpSavedIds![saved]}', which does not exist in this version ({where})");
        return now;
    }

    public object RemapLater(object coll, string where)
    {
        if (_remapped.Add(coll)) PostFixups.Add(() => BlueprintIndexFields.RemapCollection(coll, i => MapBlueprint(i, where)));
        return coll;
    }

    public Type? Resolve(string name)
    {
        if (_types.TryGetValue(name, out var t)) return t;
        t = TypeNames.TryResolve(name);
        if (t != null && TypeNames.Of(t) != name) Report.Add($"type {SaveLayout.Short(name)} renamed to {SaveLayout.Short(t)}");
        return _types[name] = t;
    }

    internal void AddMigrated(object o, SaveMigrationInfo info)
    {
        if (o is ISaveMigrated m) _migrated.Add((m, info));
    }

    public void RunMigratedHooks()
    {
        foreach (var (o, info) in _migrated) o.OnMigrated(info);
        _migrated.Clear();
    }

    private static Plan Same(Type t) => (Plan)Activator.CreateInstance(typeof(SamePlan<>).MakeGenericType(t))!;

    private static string Sig(Type t)
    {
        var sb = new System.Text.StringBuilder();
        TypeLayout.AppendValueSig(sb, t, 1);
        return sb.ToString();
    }

    private static IncompatibleSaveException Retype(string where, string saved, Type now) =>
        new($"{where}: saved {SaveLayout.Short(saved)}, now {SaveLayout.Short(now)} (no conversion for this type change: " +
            "add a converter or rename the field, DEV_GUIDE \"How to change a saved type\")");

    /// <summary>The read plan for a value saved as <paramref name="n"/> into a value of type <paramref name="target"/>
    /// (null = this build has no such field: skip it).</summary>
    public Plan For(SavedNode n, Type? target, string where, FieldInfo? field = null)
    {
        if (target == null) return new SkipPlan(n);
        Type? st = n.Kind == NodeKind.Prim ? SaveLayout.PrimType(n.Type) : Resolve(n.Type);
        Plan p;
        if (st == target)
        {
            // A reference always goes through RefPlan: an object of a type that is gone reads as null there.
            if (n.Text == Sig(target) && !Forced(target) && n.Kind != NodeKind.Ref) p = Same(target);
            else if (n.Kind == NodeKind.Struct) p = StructFor(n, target, where);
            else if (n.Kind == NodeKind.Ref) p = new RefPlan(target, where);
            else if (n.Kind == NodeKind.Enum && SaveLayout.CanConvert(SaveLayout.PrimType(n.Underlying!)!, target))
            {
                p = new ConvPlan(Same(SaveLayout.PrimType(n.Underlying!)!), target);
                Report.Add($"{where}: enum underlying type changed (converted)");
            }
            else throw Retype(where, n.Text, target);
        }
        else if (n.Kind is NodeKind.Prim or NodeKind.Enum && (target.IsPrimitive || target.IsEnum))
        {
            var from = st ?? SaveLayout.PrimType(n.Underlying!)!;
            if (!SaveLayout.CanConvert(from, target)) throw Retype(where, n.Type, target);
            p = new ConvPlan(Same(st ?? from), target);
            Report.Add($"{where}: saved {SaveLayout.Short(n.Type)}, now {SaveLayout.Short(target)} (converted)");
        }
        else if (n.Kind == NodeKind.Ref && !target.IsValueType && (st == null || target.IsAssignableFrom(st)))
        {
            if (st == null) Report.Add($"{where}: its saved type {SaveLayout.Short(n.Type)} no longer exists (read as null)");
            p = new RefPlan(target, where);
        }
        else throw Retype(where, n.Type, target);
        if (field != null && _bpMap != null && BlueprintIndexFields.IsIndex(field)) p = new RemapPlan(p, this, where);
        if (field != null && _bpMap != null && BlueprintIndexFields.IsPerBlueprint(field)) p = new PermutePlan(p);
        return p;
    }

    private Plan StructFor(SavedNode n, Type target, string where)
    {
        if (!n.Sequential) throw new IncompatibleSaveException($"{where}: the raw layout of {SaveLayout.Short(target)} changed (fixed buffer / explicit layout): it cannot be migrated");
        var fields = Match(n.Fields!, target, out var defaulted, out _);
        object template = Activator.CreateInstance(target)!;
        foreach (var f in defaulted)
            if (!f.FieldType.IsValueType) f.SetValue(template, null); // no shared instances between elements
        return new StructPlan(template, fields);
    }

    /// <summary>Saved fields → current fields (by name and occurrence, then <see cref="SaveRenames.Fields"/>) with plans.</summary>
    internal (FieldInfo?, Plan)[] Match(List<(string Name, SavedNode Node)> saved, Type t, out List<FieldInfo> defaulted, out List<string> dropped)
    {
        var cur = TypeLayout.SavedFields(t);
        string tn = TypeNames.Of(t), shortName = SaveLayout.Short(t);
        var used = new bool[cur.Length];
        var occ = new Dictionary<string, int>(StringComparer.Ordinal);
        var plans = new (FieldInfo?, Plan)[saved.Count];
        dropped = new List<string>();
        for (int i = 0; i < saved.Count; i++)
        {
            var (name, node) = saved[i];
            occ[name] = occ.GetValueOrDefault(name) + 1;
            string want = SaveRenames.Fields.TryGetValue((tn, name), out var renamed) ? renamed : name;
            int k = occ[name], idx = -1;
            for (int j = 0; j < cur.Length && idx < 0; j++)
                if (!used[j] && cur[j].Name == want && --k == 0) idx = j;
            FieldInfo? f = idx >= 0 ? cur[idx] : null;
            if (idx >= 0) used[idx] = true;
            if (f == null) { dropped.Add(name); Report.Add($"{shortName}.{name}: no longer exists (dropped)"); }
            else if (want != name) Report.Add($"{shortName}.{name}: renamed to {want}");
            plans[i] = (f, For(node, f?.FieldType, $"{shortName}.{want}", f));
        }
        defaulted = new List<FieldInfo>();
        for (int j = 0; j < cur.Length; j++)
            if (!used[j]) { defaulted.Add(cur[j]); Report.Add($"{shortName}.{cur[j].Name}: new field (default value)"); }
        return plans;
    }

    /// <summary>The codec that reads objects saved with descriptor (<paramref name="name"/>, <paramref name="sig"/>).</summary>
    public RefCodec CodecFor(string name, string sig, Type? type)
    {
        if (!sig.StartsWith(name + ":", StringComparison.Ordinal)) throw ByteReader.Corrupt("type descriptor");
        string body = sig[(name.Length + 1)..];
        RefCodec c;
        if (type == null) c = DroppedFor(name, body);
        else if (IsRebuilt(type) && body.StartsWith('{'))
        {
            Report.Add($"{SaveLayout.Short(type)}: rebuilt from the new blueprints");
            var skips = ParseOrCorrupt(() => SaveLayout.ParseFields(body)).Select(f => (Plan)new SkipPlan(f.Item2)).ToArray();
            var make = BlueprintIndexFields.Derived[TypeNames.Of(type)];
            var db = RebuildFrom!;
            c = new DroppedCodec(DroppedKind.Class, skips) { Make = () => make(db) };
        }
        else
        {
            RefCodec cur;
            try { cur = RefCodec.For(type); }
            catch (NotSupportedException e) { throw new IncompatibleSaveException(e.Message); }
            c = cur switch
            {
                ClassCodec => new MigClassCodec(this, type, ParseOrCorrupt(() => SaveLayout.ParseFields(body))),
                PlainCodec or DelegateCodec => cur,
                _ => ContainerFor(type, cur, body),
            };
        }
        c.Type = type ?? typeof(DroppedObject);
        c.Name = name;
        c.Signature = sig;
        return c;
    }

    private static T ParseOrCorrupt<T>(Func<T> f)
    {
        try { return f(); }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException) { throw ByteReader.Corrupt("type layout"); }
    }

    private RefCodec ContainerFor(Type type, RefCodec cur, string body)
    {
        var nodes = ParseOrCorrupt(() => SaveLayout.ParseValues(body));
        var gen = cur.GetType().GetGenericTypeDefinition();
        var args = cur.GetType().GetGenericArguments();
        string where = SaveLayout.Short(type);
        Plan P(int i) => For(nodes[Math.Min(i, nodes.Count - 1)], args[i], where + (i == 0 ? " item" : " value"));
        if (gen == typeof(DictionaryCodec<,>) || gen == typeof(ConcurrentDictionaryCodec<,>))
        {
            if (nodes.Count != 2) throw ByteReader.Corrupt("dictionary layout");
            return (RefCodec)Activator.CreateInstance(typeof(MigMapCodec<,>).MakeGenericType(args), gen == typeof(DictionaryCodec<,>), P(0), P(1))!;
        }
        SeqKind kind = gen == typeof(ArrayCodec<>) ? SeqKind.Array : gen == typeof(ListCodec<>) ? SeqKind.List
            : gen == typeof(StackCodec<>) ? SeqKind.Stack : gen == typeof(QueueCodec<>) ? SeqKind.Queue
            : gen == typeof(HashSetCodec<>) ? SeqKind.HashSet : gen == typeof(LazyCodec<>) ? SeqKind.Lazy : SeqKind.Boxed;
        return (RefCodec)Activator.CreateInstance(typeof(MigSeqCodec<>).MakeGenericType(args), kind, P(0))!;
    }

    private RefCodec DroppedFor(string name, string body)
    {
        Report.Add($"type {SaveLayout.Short(name)} no longer exists (its objects were dropped)");
        if (body == "delegate") return new DroppedCodec(DroppedKind.Delegate, []);
        if (body.StartsWith('{')) // a class: its fields
            return new DroppedCodec(DroppedKind.Class, ParseOrCorrupt(() => SaveLayout.ParseFields(body)).Select(f => (Plan)new SkipPlan(f.Item2)).ToArray());
        var nodes = ParseOrCorrupt(() => SaveLayout.ParseValues(body));
        var skips = nodes.Select(n => (Plan)new SkipPlan(n)).ToArray();
        if (name.EndsWith("[]", StringComparison.Ordinal)) return new DroppedCodec(DroppedKind.Array, skips);
        var (def, _) = SaveLayout.SplitGeneric(name);
        const string G = "System.Private.CoreLib:System.Collections.Generic.";
        var kind = def switch
        {
            G + "List`1" => DroppedKind.List,
            G + "Stack`1" or "System.Collections:System.Collections.Generic.Stack`1" => DroppedKind.Stack,
            G + "Queue`1" or "System.Collections:System.Collections.Generic.Queue`1" => DroppedKind.Stack,
            G + "HashSet`1" or "System.Collections:System.Collections.Generic.HashSet`1" => DroppedKind.HashSet,
            G + "Dictionary`2" => DroppedKind.Dictionary,
            "System.Collections.Concurrent:System.Collections.Concurrent.ConcurrentDictionary`2" or
                "System.Private.CoreLib:System.Collections.Concurrent.ConcurrentDictionary`2" => DroppedKind.Stack,
            "System.Private.CoreLib:System.Lazy`1" => DroppedKind.Inline,
            _ => DroppedKind.Inline, // a boxed struct
        };
        return new DroppedCodec(kind, skips);
    }
}

/// <summary>A class whose saved layout differs: field by field through plans; new fields keep the constructor's value.</summary>
internal sealed class MigClassCodec : RefCodec
{
    private readonly MigrationContext _ctx;
    private readonly (FieldInfo? Field, Plan Plan)[] _fields;
    private readonly FieldInfo[] _skipped;
    private readonly ConstructorInfo? _ctor;
    private readonly SaveMigrationInfo _info;

    public MigClassCodec(MigrationContext ctx, Type t, List<(string, SavedNode)> saved)
    {
        _ctx = ctx;
        _fields = ctx.Match(saved, t, out var defaulted, out var dropped);
        _skipped = TypeLayout.SkippedFields(t);
        _ctor = defaulted.Count > 0 ? t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) : null;
        _info = new SaveMigrationInfo { Type = t, Defaulted = defaulted.Select(f => f.Name).ToArray(), Dropped = dropped };
    }

    protected override string ComputeSignature() => "";
    public override object Create(GraphReader r) => _ctor != null ? _ctor.Invoke(null) : RuntimeHelpers.GetUninitializedObject(Type);

    public override void ReadBody(GraphReader r, object o)
    {
        foreach (var (f, p) in _fields)
        {
            var v = p.Read(r);
            if (p is PermutePlan pp && f != null) pp.Later(_ctx, o, f, v);
            else f?.SetValue(o, v);
        }
        if (_ctor == null) ClassCodec.ResetSkipped(o, _skipped);
        if (o is IAfterLoad a) r.AddAfterLoad(a);
        _ctx.AddMigrated(o, _info);
    }
}

internal enum SeqKind : byte { Array, List, Stack, Queue, HashSet, Lazy, Boxed }

/// <summary>A collection whose element layout changed: element by element through the plan.</summary>
internal sealed class MigSeqCodec<T> : RefCodec
{
    private readonly SeqKind _kind;
    private readonly Plan _p;
    public MigSeqCodec(SeqKind kind, Plan p) { _kind = kind; _p = p; }
    public override bool Inline => _kind is SeqKind.Lazy or SeqKind.Boxed;
    protected override string ComputeSignature() => "";

    private T Item(GraphReader r) => (T)_p.Read(r)!;

    public override object Create(GraphReader r)
    {
        switch (_kind)
        {
            case SeqKind.Array: return new T[r.In.Count(r.In.Remaining * 64L + 4096)];
            case SeqKind.List: return new List<T>(r.In.Count(r.In.Remaining * 64L + 4096));
            case SeqKind.Stack: return new Stack<T>();
            case SeqKind.Queue: return new Queue<T>();
            case SeqKind.HashSet: return new HashSet<T>(Comparers.Of<T>(r.In.U8()));
            case SeqKind.Lazy: { T v = Item(r); var l = new Lazy<T>(() => v); _ = l.Value; return l; }
            default: return Item(r)!;
        }
    }

    public override void ReadBody(GraphReader r, object o)
    {
        if (_kind == SeqKind.Array)
        {
            var a = (T[])o;
            for (int i = 0; i < a.Length; i++) a[i] = Item(r);
            return;
        }
        var items = new T[r.In.Count(r.In.Remaining * 64L + 4096)];
        for (int i = 0; i < items.Length; i++) items[i] = Item(r);
        switch (o)
        {
            case List<T> l: l.AddRange(items); break;
            case Stack<T> s: foreach (var x in items) s.Push(x); break;
            case Queue<T> q: foreach (var x in items) q.Enqueue(x); break;
            case HashSet<T> h:
                r.AddFixup(() => { foreach (var x in items) if (!h.Add(x)) throw ByteReader.Corrupt("duplicate set item"); });
                break;
        }
    }
}

internal sealed class MigMapCodec<K, V> : RefCodec where K : notnull
{
    private readonly bool _plain;
    private readonly Plan _k, _v;
    public MigMapCodec(bool plain, Plan k, Plan v) { _plain = plain; _k = k; _v = v; }
    protected override string ComputeSignature() => "";

    public override object Create(GraphReader r) =>
        _plain ? new Dictionary<K, V>(Comparers.Of<K>(r.In.U8())) : new ConcurrentDictionary<K, V>();

    public override void ReadBody(GraphReader r, object o)
    {
        int n = r.In.Count(r.In.Remaining + 1L);
        var keys = new K[n];
        var vals = new V[n];
        for (int i = 0; i < n; i++) { keys[i] = (K)_k.Read(r)!; vals[i] = (V)_v.Read(r)!; }
        r.AddFixup(() =>
        {
            var d = (IDictionary<K, V>)o;
            for (int i = 0; i < n; i++)
            {
                if (keys[i] is null || d.ContainsKey(keys[i])) throw ByteReader.Corrupt("duplicate dictionary key");
                d.Add(keys[i], vals[i]);
            }
        });
    }
}

internal enum DroppedKind : byte { Class, Array, List, Stack, HashSet, Dictionary, Inline, Delegate }

/// <summary>Objects of a type this build no longer has: their bytes are skipped (references inside are still read,
/// so the object numbering stays right) and they load as a <see cref="DroppedObject"/>.</summary>
internal sealed class DroppedCodec : RefCodec
{
    private readonly DroppedKind _kind;
    private readonly Plan[] _p;
    public DroppedCodec(DroppedKind kind, Plan[] p) { _kind = kind; _p = p; }
    public override bool Inline => _kind is DroppedKind.Inline or DroppedKind.Delegate;
    protected override string ComputeSignature() => "";

    /// <summary>Set for a rebuilt type (<see cref="BlueprintIndexFields.Derived"/>): the object references get instead.</summary>
    public Func<object>? Make;

    public override object Create(GraphReader r)
    {
        if (Make != null) return Make();
        var o = new DroppedObject(Name);
        switch (_kind)
        {
            case DroppedKind.Array: o.Length = r.In.Count(r.In.Remaining * 64L + 4096); break;
            case DroppedKind.List: r.In.VarU(); break;
            case DroppedKind.HashSet or DroppedKind.Dictionary: r.In.U8(); break;
            case DroppedKind.Inline: _p[0].Read(r); break;
            case DroppedKind.Delegate: r.Skipping++; try { r.ReadRef(); } finally { r.Skipping--; } r.In.Str(); r.In.Str(); break;
        }
        return o;
    }

    public override void ReadBody(GraphReader r, object o)
    {
        switch (_kind)
        {
            case DroppedKind.Class: foreach (var p in _p) p.Read(r); break;
            case DroppedKind.Array: for (int i = 0; i < ((DroppedObject)o).Length; i++) _p[0].Read(r); break;
            default:
            {
                int n = r.In.Count(r.In.Remaining + 1L);
                for (int i = 0; i < n; i++) foreach (var p in _p) p.Read(r);
                break;
            }
        }
    }
}

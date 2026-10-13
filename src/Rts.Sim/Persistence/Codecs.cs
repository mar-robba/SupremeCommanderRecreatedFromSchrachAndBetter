using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Rts.Sim.Persistence;

/// <summary>
/// How objects of one runtime type are stored. Non-inline codecs: the object is created at its first reference
/// (<see cref="Create"/> reads a small header such as an array length) and its body is written/read later in
/// creation order, so cycles and shared references work and nothing recurses deeply. Inline codecs (immutable
/// objects such as delegates and Lazy) are fully written at the reference site.
/// </summary>
internal abstract class RefCodec
{
    public Type Type = null!;
    public string Name = "";
    /// <summary>Layout signature compared on load (strict compatibility check).</summary>
    public string Signature = "";
    public virtual bool Inline => false;

    public virtual void WriteHeader(GraphWriter w, object o) { }
    public abstract object Create(GraphReader r);
    public virtual void WriteBody(GraphWriter w, object o) { }
    public virtual void ReadBody(GraphReader r, object o) { }

    private static readonly ConcurrentDictionary<Type, RefCodec> Cache = new();

    public static RefCodec For(Type t) => Cache.GetOrAdd(t, static t =>
    {
        var c = Build(t);
        c.Type = t;
        c.Name = TypeNames.Of(t);
        c.Signature = c.Name + ":" + c.ComputeSignature();
        return c;
    });

    protected abstract string ComputeSignature();

    private static RefCodec Build(Type t)
    {
        if (t.IsArray)
        {
            if (!t.IsSZArray) throw Unsupported(t, "multi-dimensional arrays");
            return Make(typeof(ArrayCodec<>), t.GetElementType()!);
        }
        if (typeof(Delegate).IsAssignableFrom(t)) return new DelegateCodec();
        if (t == typeof(object) || t == typeof(Lock)) return new PlainCodec();
        if (t.IsValueType) return Make(typeof(BoxedCodec<>), t);
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            var a = t.GetGenericArguments();
            if (def == typeof(List<>)) return Make(typeof(ListCodec<>), a);
            if (def == typeof(Stack<>)) return Make(typeof(StackCodec<>), a);
            if (def == typeof(Queue<>)) return Make(typeof(QueueCodec<>), a);
            if (def == typeof(HashSet<>)) return Make(typeof(HashSetCodec<>), a);
            if (def == typeof(Dictionary<,>)) return Make(typeof(DictionaryCodec<,>), a);
            if (def == typeof(ConcurrentDictionary<,>)) return Make(typeof(ConcurrentDictionaryCodec<,>), a);
            if (def == typeof(Lazy<>)) return Make(typeof(LazyCodec<>), a);
        }
        if (IsSystemType(t)) throw Unsupported(t, "framework type without a save codec");
        if (t.IsPointer || t.IsByRefLike) throw Unsupported(t, "pointers / ref structs");
        return new ClassCodec(t);
    }

    public static bool IsSystemType(Type t)
    {
        string asm = t.Assembly.GetName().Name ?? "";
        return asm == "System.Private.CoreLib" || asm.StartsWith("System.", StringComparison.Ordinal)
            || asm.StartsWith("Microsoft.", StringComparison.Ordinal) || asm == "mscorlib" || asm == "netstandard";
    }

    public static NotSupportedException Unsupported(Type t, string why) =>
        new($"Save system: type '{t}' is not supported ({why}). Add a codec in Rts.Sim/Persistence/Codecs.cs, " +
            "store the data in supported types, or mark the field [NoSave] if it never affects the simulation.");

    private static RefCodec Make(Type generic, params Type[] args) =>
        (RefCodec)Activator.CreateInstance(generic.MakeGenericType(args), nonPublic: true)!;

    protected static string Sig(Type t)
    {
        var sb = new System.Text.StringBuilder();
        TypeLayout.AppendValueSig(sb, t, 1);
        return sb.ToString();
    }
}

/// <summary>Any class of the game assemblies: every non-[NoSave] instance field, through generated IL.</summary>
internal sealed class ClassCodec : RefCodec
{
    private readonly Type _t;
    private ObjWriter? _w;
    private ObjReader? _r;
    private readonly FieldInfo[] _skipped;
    private readonly bool _afterLoad;

    public ClassCodec(Type t)
    {
        _t = t;
        if (t.IsAbstract) throw Unsupported(t, "abstract type");
        _skipped = TypeLayout.SkippedFields(t);
        _afterLoad = typeof(IAfterLoad).IsAssignableFrom(t);
    }

    protected override string ComputeSignature() => TypeLayout.ClassSignature(_t);

    public override object Create(GraphReader r) => RuntimeHelpers.GetUninitializedObject(_t);

    public override void WriteBody(GraphWriter w, object o) => (_w ??= FieldCodegen.ClassWriter(_t))(w, o);

    public override void ReadBody(GraphReader r, object o)
    {
        (_r ??= FieldCodegen.ClassReader(_t))(r, o);
        foreach (var f in _skipped)
        {
            var ft = f.FieldType;
            if (ft.IsValueType || ft.IsAbstract || ft.IsInterface || ft.IsArray || ft == typeof(string)) continue;
            if (ft.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) == null) continue;
            f.SetValue(o, Activator.CreateInstance(ft, nonPublic: true));
        }
        if (_afterLoad) r.AddAfterLoad((IAfterLoad)o);
    }
}

internal sealed class PlainCodec : RefCodec
{
    protected override string ComputeSignature() => "";
    public override object Create(GraphReader r) => Activator.CreateInstance(Type)!;
}

internal sealed class ArrayCodec<T> : RefCodec
{
    protected override string ComputeSignature() => Sig(typeof(T));
    public override void WriteHeader(GraphWriter w, object o) => w.Out.VarU((ulong)((T[])o).Length);

    public override object Create(GraphReader r)
    {
        int min = Val<T>.MinEncodedSize;
        int n = r.In.Count(min == 0 ? (long)r.In.Remaining * 64 + 4096 : r.In.Remaining / min);
        return new T[n]; // never Array.Empty: identity (shared vs distinct empty arrays) is part of the graph
    }

    public override void WriteBody(GraphWriter w, object o) => Val<T>.WriteSpan(w, (T[])o);
    public override void ReadBody(GraphReader r, object o) => Val<T>.ReadSpan(r, (T[])o);
}

internal sealed class ListCodec<T> : RefCodec
{
    protected override string ComputeSignature() => Sig(typeof(T));
    public override void WriteHeader(GraphWriter w, object o) => w.Out.VarU((ulong)((List<T>)o).Capacity);
    public override object Create(GraphReader r) => new List<T>(r.In.Count((long)r.In.Remaining * 64 + 4096));

    public override void WriteBody(GraphWriter w, object o)
    {
        var list = (List<T>)o;
        w.Out.VarU((ulong)list.Count);
        Val<T>.WriteSpan(w, CollectionsMarshal.AsSpan(list));
    }

    public override void ReadBody(GraphReader r, object o)
    {
        var list = (List<T>)o;
        int min = Val<T>.MinEncodedSize;
        int n = r.In.Count(min == 0 ? (long)r.In.Remaining * 64 + 4096 : r.In.Remaining / min);
        CollectionsMarshal.SetCount(list, n);
        Val<T>.ReadSpan(r, CollectionsMarshal.AsSpan(list));
    }
}

internal sealed class StackCodec<T> : RefCodec
{
    protected override string ComputeSignature() => Sig(typeof(T));
    public override object Create(GraphReader r) => new Stack<T>();

    public override void WriteBody(GraphWriter w, object o)
    {
        var items = ((Stack<T>)o).ToArray(); // top first
        Array.Reverse(items);
        w.Out.VarU((ulong)items.Length);
        Val<T>.WriteSpan(w, items);
    }

    public override void ReadBody(GraphReader r, object o)
    {
        var s = (Stack<T>)o;
        var items = new T[r.In.Count((long)r.In.Remaining * 64 + 4096)];
        Val<T>.ReadSpan(r, items);
        s.EnsureCapacity(items.Length);
        foreach (var x in items) s.Push(x);
    }
}

internal sealed class QueueCodec<T> : RefCodec
{
    protected override string ComputeSignature() => Sig(typeof(T));
    public override object Create(GraphReader r) => new Queue<T>();

    public override void WriteBody(GraphWriter w, object o)
    {
        var items = ((Queue<T>)o).ToArray(); // front first
        w.Out.VarU((ulong)items.Length);
        Val<T>.WriteSpan(w, items);
    }

    public override void ReadBody(GraphReader r, object o)
    {
        var q = (Queue<T>)o;
        var items = new T[r.In.Count((long)r.In.Remaining * 64 + 4096)];
        Val<T>.ReadSpan(r, items);
        q.EnsureCapacity(items.Length);
        foreach (var x in items) q.Enqueue(x);
    }
}

/// <summary>Well-known comparers (only these can be saved; anything else is reported as unsupported).</summary>
internal static class Comparers
{
    public static byte Token<T>(IEqualityComparer<T>? c)
    {
        if (c == null || ReferenceEquals(c, EqualityComparer<T>.Default)) return 0;
        if (ReferenceEquals(c, StringComparer.Ordinal)) return 1;
        if (ReferenceEquals(c, StringComparer.OrdinalIgnoreCase)) return 2;
        if (ReferenceEquals(c, StringComparer.InvariantCulture)) return 3;
        if (ReferenceEquals(c, StringComparer.InvariantCultureIgnoreCase)) return 4;
        if (ReferenceEquals(c, ReferenceEqualityComparer.Instance)) return 5;
        throw new NotSupportedException($"Save system: comparer {c.GetType()} is not supported (use the default comparer)");
    }

    public static IEqualityComparer<T>? Of<T>(byte token) => token switch
    {
        0 => null,
        1 => (IEqualityComparer<T>)StringComparer.Ordinal,
        2 => (IEqualityComparer<T>)StringComparer.OrdinalIgnoreCase,
        3 => (IEqualityComparer<T>)StringComparer.InvariantCulture,
        4 => (IEqualityComparer<T>)StringComparer.InvariantCultureIgnoreCase,
        5 => (IEqualityComparer<T>)(object)ReferenceEqualityComparer.Instance,
        _ => throw ByteReader.Corrupt("unknown comparer"),
    };
}

/// <summary>Entries in enumeration order (= insertion order without removals); inserted after every object is populated
/// (keys may be objects whose hash depends on their fields).</summary>
internal sealed class DictionaryCodec<K, V> : RefCodec where K : notnull
{
    protected override string ComputeSignature() => Sig(typeof(K)) + "," + Sig(typeof(V));
    public override void WriteHeader(GraphWriter w, object o) => w.Out.U8(Comparers.Token(((Dictionary<K, V>)o).Comparer));

    public override object Create(GraphReader r)
    {
        var cmp = Comparers.Of<K>(r.In.U8());
        return new Dictionary<K, V>(cmp);
    }

    public override void WriteBody(GraphWriter w, object o)
    {
        var d = (Dictionary<K, V>)o;
        w.Out.VarU((ulong)d.Count);
        foreach (var kv in d)
        {
            K k = kv.Key; V v = kv.Value;
            Val<K>.Write(w, ref k);
            Val<V>.Write(w, ref v);
        }
    }

    public override void ReadBody(GraphReader r, object o)
    {
        var d = (Dictionary<K, V>)o;
        int n = r.In.Count(r.In.Remaining + 1L);
        var keys = new K[n];
        var vals = new V[n];
        for (int i = 0; i < n; i++) { Val<K>.Read(r, ref keys[i]); Val<V>.Read(r, ref vals[i]); }
        r.AddFixup(() =>
        {
            d.EnsureCapacity(n);
            for (int i = 0; i < n; i++)
                if (keys[i] is null || !d.TryAdd(keys[i], vals[i])) throw ByteReader.Corrupt("duplicate dictionary key");
        });
    }
}

internal sealed class HashSetCodec<T> : RefCodec
{
    protected override string ComputeSignature() => Sig(typeof(T));
    public override void WriteHeader(GraphWriter w, object o) => w.Out.U8(Comparers.Token(((HashSet<T>)o).Comparer));
    public override object Create(GraphReader r) => new HashSet<T>(Comparers.Of<T>(r.In.U8()));

    public override void WriteBody(GraphWriter w, object o)
    {
        var s = (HashSet<T>)o;
        w.Out.VarU((ulong)s.Count);
        foreach (var x in s) { T v = x; Val<T>.Write(w, ref v); }
    }

    public override void ReadBody(GraphReader r, object o)
    {
        var s = (HashSet<T>)o;
        var items = new T[r.In.Count(r.In.Remaining + 1L)];
        for (int i = 0; i < items.Length; i++) Val<T>.Read(r, ref items[i]);
        r.AddFixup(() =>
        {
            s.EnsureCapacity(items.Length);
            foreach (var x in items)
                if (!s.Add(x)) throw ByteReader.Corrupt("duplicate set item");
        });
    }
}

/// <summary>Entries sorted by key when the key is comparable (enumeration order of a ConcurrentDictionary depends on hashing).</summary>
internal sealed class ConcurrentDictionaryCodec<K, V> : RefCodec where K : notnull
{
    protected override string ComputeSignature() => Sig(typeof(K)) + "," + Sig(typeof(V));
    public override object Create(GraphReader r) => new ConcurrentDictionary<K, V>();

    public override void WriteBody(GraphWriter w, object o)
    {
        var entries = ((ConcurrentDictionary<K, V>)o).ToArray();
        if (typeof(IComparable<K>).IsAssignableFrom(typeof(K)) || typeof(IComparable).IsAssignableFrom(typeof(K)))
            Array.Sort(entries, static (a, b) => Comparer<K>.Default.Compare(a.Key, b.Key));
        w.Out.VarU((ulong)entries.Length);
        foreach (var kv in entries)
        {
            K k = kv.Key; V v = kv.Value;
            Val<K>.Write(w, ref k);
            Val<V>.Write(w, ref v);
        }
    }

    public override void ReadBody(GraphReader r, object o)
    {
        var d = (ConcurrentDictionary<K, V>)o;
        int n = r.In.Count(r.In.Remaining + 1L);
        var keys = new K[n];
        var vals = new V[n];
        for (int i = 0; i < n; i++) { Val<K>.Read(r, ref keys[i]); Val<V>.Read(r, ref vals[i]); }
        r.AddFixup(() =>
        {
            for (int i = 0; i < n; i++)
                if (keys[i] is null || !d.TryAdd(keys[i], vals[i])) throw ByteReader.Corrupt("duplicate dictionary key");
        });
    }
}

/// <summary>Lazy values are forced and stored (the factory is not saved).</summary>
internal sealed class LazyCodec<T> : RefCodec
{
    public override bool Inline => true;
    protected override string ComputeSignature() => Sig(typeof(T));

    public override void WriteHeader(GraphWriter w, object o)
    {
        T v = ((Lazy<T>)o).Value;
        Val<T>.Write(w, ref v);
    }

    public override object Create(GraphReader r)
    {
        T v = default!;
        Val<T>.Read(r, ref v);
        var lazy = new Lazy<T>(() => v, LazyThreadSafetyMode.ExecutionAndPublication);
        _ = lazy.Value;
        return lazy;
    }
}

internal sealed class BoxedCodec<T> : RefCodec where T : struct
{
    public override bool Inline => true;
    protected override string ComputeSignature() => Sig(typeof(T));

    public override void WriteHeader(GraphWriter w, object o)
    {
        T v = (T)o;
        Val<T>.Write(w, ref v);
    }

    public override object Create(GraphReader r)
    {
        T v = default;
        Val<T>.Read(r, ref v);
        return v;
    }
}

/// <summary>Single-cast delegates: target object (a graph reference) + method (declaring type, name, signature).</summary>
internal sealed class DelegateCodec : RefCodec
{
    public override bool Inline => true;
    protected override string ComputeSignature() => "delegate";

    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static string MethodSig(MethodInfo m) =>
        m.Name + "(" + string.Join(",", m.GetParameters().Select(p => TypeNames.Of(p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType) + (p.ParameterType.IsByRef ? "&" : ""))) + ")" + TypeNames.Of(m.ReturnType);

    public override void WriteHeader(GraphWriter w, object o)
    {
        var d = (Delegate)o;
        if (d.HasSingleTarget == false) throw new NotSupportedException($"Save system: multicast delegate {d.GetType()} is not supported");
        var m = d.Method;
        if (m.DeclaringType == null || m is DynamicMethod || m.IsGenericMethod)
            throw new NotSupportedException($"Save system: delegate to {m} cannot be saved");
        w.WriteRef(d.Target);
        w.Out.Str(TypeNames.Of(m.DeclaringType));
        w.Out.Str(MethodSig(m));
    }

    public override object Create(GraphReader r)
    {
        object? target = r.ReadRef();
        var declaring = TypeNames.Resolve(r.In.Str() ?? throw ByteReader.Corrupt("delegate"));
        string sig = r.In.Str() ?? throw ByteReader.Corrupt("delegate");
        var m = declaring.GetMethods(All).FirstOrDefault(x => MethodSig(x) == sig)
                ?? throw new IncompatibleSaveException($"Method {declaring.Name}.{sig} no longer exists (saved delegate)");
        return m.IsStatic ? Delegate.CreateDelegate(Type, m) : Delegate.CreateDelegate(Type, target, m);
    }
}

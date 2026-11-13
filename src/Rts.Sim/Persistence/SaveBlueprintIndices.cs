using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Rts.Sim.World;

namespace Rts.Sim.Persistence;

/// <summary>
/// (11a-2) Every saved field that holds a unit blueprint INDEX (position in <c>BlueprintDb.All</c>). When a save was
/// made with another blueprint list (a unit added, removed or reordered), these values are translated by blueprint id
/// on load (ARCHITECTURE "Save migration"). Supported shapes: int / short, int[], List&lt;int&gt;, HashSet&lt;int&gt;,
/// Dictionary&lt;int, V&gt; (keys). <c>SaveCoverageTests</c> fails for a saved int-like field whose name looks like a
/// blueprint index (<see cref="LooksLikeIndex"/>) and that is in neither list below: add it to one of them.
/// </summary>
public static class BlueprintIndexFields
{
    /// <summary>(stable type name, field name) of every saved blueprint-index field.</summary>
    public static readonly (string Type, string Field)[] Indices =
    [
        ("Rts.Sim:Rts.Sim.World.UnitStore", "Bp"),
    ];

    /// <summary>Saved fields whose name looks like a blueprint index but that are not one (or are never saved state
    /// that outlives a tick).</summary>
    public static readonly (string Type, string Field)[] NotIndices =
    [
    ];

    private static readonly Regex IndexName = new("(^|[a-z_<])(Bp|bp|Blueprint|blueprint)", RegexOptions.CultureInvariant);

    /// <summary>The heuristic of the coverage test: an int-like (or int collection) field named like "Bp", "TargetBp",
    /// "BlueprintIndex", "_bpIds".</summary>
    public static bool LooksLikeIndex(FieldInfo f) => IndexName.IsMatch(f.Name) && SupportedShape(f.FieldType);

    public static bool SupportedShape(Type t) =>
        t == typeof(int) || t == typeof(short) || t == typeof(int[]) || t == typeof(List<int>) || t == typeof(HashSet<int>)
        || t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>) && t.GetGenericArguments()[0] == typeof(int);

    private static HashSet<FieldInfo>? _fields;
    private static readonly ConcurrentDictionary<Type, bool> AffectsCache = new();

    /// <summary>The fields of <see cref="Indices"/> (an entry that does not resolve is reported by the coverage test).</summary>
    public static HashSet<FieldInfo> Fields => _fields ??= Resolve(Indices, out _);

    internal static HashSet<FieldInfo> Resolve((string Type, string Field)[] list, out List<string> missing)
    {
        var set = new HashSet<FieldInfo>();
        missing = new List<string>();
        foreach (var (tn, fn) in list)
        {
            var f = TypeNames.TryResolve(tn) is { } t ? TypeLayout.SavedFields(t).FirstOrDefault(x => x.Name == fn) : null;
            if (f == null) missing.Add($"{tn}.{fn}");
            else set.Add(f);
        }
        return set;
    }

    public static bool IsIndex(FieldInfo f) => Fields.Contains(f);

    /// <summary>True when reading <paramref name="t"/> may meet a blueprint index stored inline (its own fields,
    /// value-type fields, or value-type items of a collection): such types take the slow read path when indices change.</summary>
    public static bool Affects(Type t) => AffectsCache.GetOrAdd(t, static t => AffectsUncached(t, 0));

    private static bool AffectsUncached(Type t, int depth)
    {
        if (depth > 16 || t.IsPrimitive || t.IsEnum || t == typeof(string)) return false;
        if (t.IsArray) { var e = t.GetElementType()!; return e.IsValueType && AffectsUncached(e, depth + 1); }
        if (t.IsGenericType && RefCodec.IsSystemType(t))
            return t.GetGenericArguments().Any(a => a.IsValueType && AffectsUncached(a, depth + 1));
        if (RefCodec.IsSystemType(t)) return false;
        foreach (var f in TypeLayout.SavedFields(t))
        {
            if (Fields.Contains(f)) return true;
            if (f.FieldType.IsValueType && AffectsUncached(f.FieldType, depth + 1)) return true;
        }
        return false;
    }

    /// <summary>Translates every blueprint index in a loaded collection in place (dictionary keys: rebuilt in order).</summary>
    internal static void RemapCollection(object coll, Func<int, int> map)
    {
        switch (coll)
        {
            case int[] a: for (int i = 0; i < a.Length; i++) a[i] = map(a[i]); break;
            case List<int> l: for (int i = 0; i < l.Count; i++) l[i] = map(l[i]); break;
            case HashSet<int> h: { var items = h.ToArray(); h.Clear(); foreach (var x in items) h.Add(map(x)); break; }
            case IDictionary d:
            {
                var entries = new List<DictionaryEntry>(d.Count);
                foreach (DictionaryEntry e in d) entries.Add(e);
                d.Clear();
                foreach (var e in entries) d.Add(map((int)e.Key), e.Value);
                break;
            }
            default: throw new InvalidOperationException($"Blueprint index field of unsupported type {coll.GetType()}");
        }
    }

    /// <summary>Every saved field reachable from the simulation roots (for the coverage test), as "Owner.Field".</summary>
    public static List<(Type Owner, FieldInfo Field)> ReachableFields()
    {
        var asm = typeof(SimWorld).Assembly;
        var all = asm.GetTypes();
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>();
        void Add(Type t)
        {
            if (t.IsArray) { Add(t.GetElementType()!); return; }
            if (t.IsGenericType) foreach (var a in t.GetGenericArguments()) Add(a);
            if (t.Assembly != asm || t.IsPrimitive || t.IsEnum || !seen.Add(t)) return;
            queue.Enqueue(t);
            if (!t.IsValueType) foreach (var s in all) if (s != t && t.IsAssignableFrom(s) && !s.IsAbstract) Add(s);
        }
        Add(typeof(Simulation));
        foreach (var tb in WorldState.Tables) Add(tb.ValueType);
        var list = new List<(Type, FieldInfo)>();
        while (queue.TryDequeue(out var t))
        {
            if (t.IsInterface || t.IsGenericTypeDefinition) continue;
            foreach (var f in TypeLayout.SavedFields(t))
            {
                list.Add((t, f));
                Add(f.FieldType);
            }
        }
        return list;
    }
}

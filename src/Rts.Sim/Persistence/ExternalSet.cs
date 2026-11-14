using System.Collections;
using System.Reflection;
using System.Text;

namespace Rts.Sim.Persistence;

/// <summary>
/// Immutable shared objects that are NOT stored in a save (the BlueprintDb and the Terrain): every object reachable
/// from the roots gets an id in a deterministic walk order, references to them are written as ids, and on load the
/// same walk over the freshly loaded db/terrain maps the ids back. Strings and value types have no identity and are
/// not registered. Framework collection internals are not walked (only arrays, List items and dictionary values).
/// (11a-2) Ids depend on the shape of the data, so a save also stores a stable <see cref="PathOf"/> for every id it
/// uses ("bp:uef_tank/Weapons[1]"), and a load with another shape maps them back by path (<see cref="IdOfPath"/>).
/// </summary>
public sealed class ExternalSet
{
    private readonly Dictionary<object, int> _ids = new(ReferenceEqualityComparer.Instance);
    private readonly List<object> _objs = new();
    // (11a-2) how each object was first reached: parent id (-1 = root) and the edge label
    private readonly List<(int Parent, string Label)> _edges = new();
    private Dictionary<string, int>? _byPath;

    public ExternalSet(params object[] roots)
    {
        var queue = new Queue<object>();
        for (int i = 0; i < roots.Length; i++) Discover(roots[i], queue, -1, "#" + i);
        int k = 0;
        while (queue.TryDequeue(out var o)) Visit(o, k++, queue);
        var sb = new StringBuilder();
        foreach (var o in _objs) sb.Append(TypeNames.Of(o.GetType())).Append(';');
        Fingerprint = Hash64.Of(sb.ToString());
    }

    public int Count => _objs.Count;
    /// <summary>Hash of the walk (types in id order): differs if the shared data has a different shape.</summary>
    public ulong Fingerprint { get; }

    public bool TryGetId(object o, out int id) => _ids.TryGetValue(o, out id);
    public object Get(int id) => _objs[id];

    /// <summary>A name of object <paramref name="id"/> that does not depend on the walk order: a unit blueprint is
    /// "bp:ID", anything else the path of fields / indices from the nearest blueprint or root ("#0" = the db).</summary>
    public string PathOf(int id)
    {
        // an empty array is the shared Array.Empty<T>() (also what "= []" gives sim fields): not tied to one place
        if (_objs[id] is Array { Length: 0 } empty) return EmptyPrefix + TypeNames.Of(empty.GetType());
        var parts = new List<string>();
        for (int i = id; ; )
        {
            if (_objs[i] is Data.UnitBlueprint ub) { parts.Add("bp:" + ub.Id); break; }
            var (parent, label) = _edges[i];
            parts.Add(label);
            if (parent < 0) break;
            i = parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    public const string EmptyPrefix = "empty:";

    /// <summary>The object a saved <see cref="PathOf"/> names in this build (null = none).</summary>
    public object? Resolve(string path)
    {
        if (path.StartsWith(EmptyPrefix, StringComparison.Ordinal))
            return TypeNames.TryResolve(path[EmptyPrefix.Length..]) is { IsArray: true } at ? EmptyOf(at) : null;
        int id = IdOfPath(path);
        return id >= 0 ? Get(id) : null;
    }

    private static object EmptyOf(Type arrayType) =>
        typeof(Array).GetMethod(nameof(Array.Empty))!.MakeGenericMethod(arrayType.GetElementType()!).Invoke(null, null)!;

    /// <summary>The id of the object at <paramref name="path"/> in this set, -1 if there is none.</summary>
    public int IdOfPath(string path)
    {
        if (_byPath == null)
        {
            var d = new Dictionary<string, int>(_objs.Count, StringComparer.Ordinal);
            for (int i = 0; i < _objs.Count; i++) d.TryAdd(PathOf(i), i);
            _byPath = d;
        }
        return _byPath.TryGetValue(path, out int id) ? id : -1;
    }

    private void Discover(object? o, Queue<object> queue, int parent, string label)
    {
        if (o == null || o is string || o.GetType().IsValueType || o is Type || o is Delegate) return;
        if (_ids.ContainsKey(o)) return;
        _ids[o] = _objs.Count;
        _objs.Add(o);
        _edges.Add((parent, label));
        queue.Enqueue(o);
    }

    private void Visit(object o, int id, Queue<object> queue)
    {
        var t = o.GetType();
        if (o is Array arr)
        {
            var et = t.GetElementType()!;
            if (et.IsPrimitive || et.IsEnum || et == typeof(string)) return;
            int i = 0;
            foreach (var x in arr) VisitValue(x, queue, id, null, i++);
            return;
        }
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            var l = (IList)o;
            for (int i = 0; i < l.Count; i++) VisitValue(l[i], queue, id, null, i);
            return;
        }
        if (o is IDictionary dict)
        {
            foreach (DictionaryEntry e in dict)
            {
                string key = e.Key?.ToString() ?? "";
                VisitValue(e.Key, queue, id, "{" + key + "}", -1);
                VisitValue(e.Value, queue, id, "[" + key + "]", -1);
            }
            return;
        }
        if (RefCodec.IsSystemType(t)) return;
        foreach (var f in TypeLayout.AllFields(t)) VisitValue(f.GetValue(o), queue, id, f.Name, -1);
    }

    /// <summary><paramref name="name"/> = the field name or dictionary label, else <paramref name="index"/> = the item index.</summary>
    private void VisitValue(object? v, Queue<object> queue, int parent, string? name, int index)
    {
        if (v == null || v is string) return;
        var t = v.GetType();
        if (t.IsValueType)
        {
            if (t.IsPrimitive || t.IsEnum || RefCodec.IsSystemType(t) || !TypeLayout.ContainsReferences(t)) return;
            string prefix = name ?? "[" + index + "]";
            foreach (var f in TypeLayout.AllFields(t)) VisitValue(f.GetValue(v), queue, parent, prefix + "." + f.Name, -1);
            return;
        }
        if (_ids.ContainsKey(v)) return;
        Discover(v, queue, parent, name ?? "[" + index + "]");
    }
}

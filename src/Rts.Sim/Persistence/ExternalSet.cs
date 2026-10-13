using System.Collections;
using System.Reflection;
using System.Text;

namespace Rts.Sim.Persistence;

/// <summary>
/// Immutable shared objects that are NOT stored in a save (the BlueprintDb and the Terrain): every object reachable
/// from the roots gets an id in a deterministic walk order, references to them are written as ids, and on load the
/// same walk over the freshly loaded db/terrain maps the ids back. Strings and value types have no identity and are
/// not registered. Framework collection internals are not walked (only arrays, List items and dictionary values).
/// </summary>
public sealed class ExternalSet
{
    private readonly Dictionary<object, int> _ids = new(ReferenceEqualityComparer.Instance);
    private readonly List<object> _objs = new();

    public ExternalSet(params object[] roots)
    {
        var queue = new Queue<object>();
        foreach (var r in roots) Discover(r, queue);
        while (queue.TryDequeue(out var o)) Visit(o, queue);
        var sb = new StringBuilder();
        foreach (var o in _objs) sb.Append(TypeNames.Of(o.GetType())).Append(';');
        Fingerprint = Hash64.Of(sb.ToString());
    }

    public int Count => _objs.Count;
    /// <summary>Hash of the walk (types in id order): differs if the shared data has a different shape.</summary>
    public ulong Fingerprint { get; }

    public bool TryGetId(object o, out int id) => _ids.TryGetValue(o, out id);
    public object Get(int id) => _objs[id];

    private void Discover(object? o, Queue<object> queue)
    {
        if (o == null || o is string || o.GetType().IsValueType || o is Type || o is Delegate) return;
        if (_ids.ContainsKey(o)) return;
        _ids[o] = _objs.Count;
        _objs.Add(o);
        queue.Enqueue(o);
    }

    private void Visit(object o, Queue<object> queue)
    {
        var t = o.GetType();
        if (o is Array arr)
        {
            var et = t.GetElementType()!;
            if (et.IsPrimitive || et.IsEnum || et == typeof(string)) return;
            foreach (var x in arr) VisitValue(x, queue);
            return;
        }
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            foreach (var x in (IList)o) VisitValue(x, queue);
            return;
        }
        if (o is IDictionary dict)
        {
            foreach (DictionaryEntry e in dict) { VisitValue(e.Key, queue); VisitValue(e.Value, queue); }
            return;
        }
        if (RefCodec.IsSystemType(t)) return;
        foreach (var f in TypeLayout.AllFields(t)) VisitValue(f.GetValue(o), queue);
    }

    private void VisitValue(object? v, Queue<object> queue)
    {
        if (v == null || v is string) return;
        var t = v.GetType();
        if (t.IsValueType)
        {
            if (t.IsPrimitive || t.IsEnum || RefCodec.IsSystemType(t) || !TypeLayout.ContainsReferences(t)) return;
            foreach (var f in TypeLayout.AllFields(t)) VisitValue(f.GetValue(v), queue);
            return;
        }
        Discover(v, queue);
    }
}

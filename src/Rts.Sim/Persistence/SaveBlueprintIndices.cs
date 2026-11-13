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
        ("Rts.Sim:Rts.Sim.World.ProjectileStore", "SourceBp"),
        ("Rts.Sim:Rts.Sim.World.PropStore", "BlueprintIndex"),
        ("Rts.Sim:Rts.Sim.World.UpgradeState", "TargetBp"),
        ("Rts.Sim:Rts.Sim.World.FactoryState", "Queue"), // List<int> of queued blueprints (the name heuristic misses it)
        ("Rts.Sim:Rts.Sim.Orders.Order", "BlueprintIndex"),
        ("Rts.Sim:Rts.Sim.Orders.DebugSpawnCommand", "<BlueprintIndex>k__BackingField"),
        ("Rts.Sim:Rts.Sim.Orders.FactoryDequeueCommand", "<BlueprintIndex>k__BackingField"),
        ("Rts.Sim:Rts.Sim.Orders.FactoryEnqueueCommand", "<BlueprintIndex>k__BackingField"),
        ("Rts.Sim:Rts.Sim.Orders.SandboxSpawnCommand", "<BlueprintIndex>k__BackingField"),
        ("Rts.Sim:Rts.Sim.Combat.ShotRequest", "Bp"),
        ("Rts.Sim:Rts.Sim.Commander.PendingDeathBlast", "Bp"),
        ("Rts.Sim:Rts.Sim.Experimental.PendingBlast", "Bp"),
        ("Rts.Sim:Rts.Sim.Systems.GhostInfo", "Bp"),
        ("Rts.Sim:Rts.Sim.Systems.SpawnJob", "Blueprint"),
        ("Rts.Sim:Rts.Sim.AI.KnownStructure", "Bp"),
        ("Rts.Sim:Rts.Sim.AI.AiPlacement+Reservation", "Bp"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpAA"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpAirFactory"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpAirScout"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpArtillery"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpBomber"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpEnergyStorage"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpEngineer"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpHydro"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpInterceptor"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpLandFactory"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpMassStorage"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpMex"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpMobileAA"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpPD"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpPgen"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpRadar"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpScout"),
        ("Rts.Sim:Rts.Sim.AI.AiBrain", "BpTank"),
    ];

    /// <summary>Types whose every saved int / int[] field is a blueprint index (whatever its name), e.g. the AI's
    /// per-army unit roster.</summary>
    public static readonly string[] IndexTypes =
    [
        "Rts.Sim:Rts.Sim.AI.AiRoster",
    ];

    /// <summary>Saved arrays INDEXED by blueprint index (one entry per blueprint, in classes): when the blueprint list
    /// changed they are rebuilt in this build's order by id (a new blueprint's entry = default).</summary>
    public static readonly (string Type, string Field)[] PerBlueprint =
    [
        ("Rts.Sim:Rts.Sim.AI.AiPerception", "Count"),
        ("Rts.Sim:Rts.Sim.AI.AiPerception", "Complete"),
        ("Rts.Sim:Rts.Sim.AI.AiPerception", "Queued"),
        ("Rts.Sim:Rts.Sim.AI.AiPlacement", "_failUntil"),
        ("Rts.Sim:Rts.Sim.Intel.IntelService", "_canSubmerge"),
    ];

    /// <summary>Saved objects computed only from the blueprints: when any blueprint differs (ids or content) the
    /// saved one is skipped and a fresh one is built from this build's db (every reference gets the fresh one).</summary>
    public static readonly Dictionary<string, Func<Data.BlueprintDb, object>> Derived = new(StringComparer.Ordinal)
    {
        ["Rts.Sim:Rts.Sim.Combat.CombatCatalog"] = static db => new Combat.CombatCatalog(db),
    };

    /// <summary>Per-world state keys (<see cref="WorldState.Tables"/>) that are caches of blueprint data: dropped when
    /// any blueprint differs (their owner builds them again on first use).</summary>
    public static readonly HashSet<string> DerivedTables = new(StringComparer.Ordinal)
    {
        "Rts.Sim.Naval.Torpedoes+DefenseTable.Table",
    };

    private static HashSet<FieldInfo>? _perBp;
    public static bool IsPerBlueprint(FieldInfo f) => (_perBp ??= Resolve(PerBlueprint, out _)).Contains(f);

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
    public static HashSet<FieldInfo> Fields => _fields ??= All(out _);

    /// <summary><see cref="Indices"/> plus the int-like fields of <see cref="IndexTypes"/>; <paramref name="missing"/> =
    /// entries that do not resolve in this build.</summary>
    public static HashSet<FieldInfo> All(out List<string> missing)
    {
        var set = Resolve(Indices, out missing);
        foreach (var tn in IndexTypes)
        {
            if (TypeNames.TryResolve(tn) is not { } t) { missing.Add(tn); continue; }
            foreach (var f in TypeLayout.SavedFields(t))
                if (SupportedShape(f.FieldType)) set.Add(f);
        }
        return set;
    }

    public static HashSet<FieldInfo> Resolve((string Type, string Field)[] list, out List<string> missing)
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
            if (Fields.Contains(f) || IsPerBlueprint(f)) return true;
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

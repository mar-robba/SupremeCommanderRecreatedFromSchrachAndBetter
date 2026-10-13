using System.Reflection;
using System.Runtime.CompilerServices;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Sim.Persistence;

/// <summary>
/// Full simulation state as bytes. Roots: the <see cref="Simulation"/> (which reaches the SimWorld and everything it
/// owns) plus every per-world state object that systems attach with a static
/// <c>ConditionalWeakTable&lt;SimWorld, T&gt;</c> anywhere in Rts.Sim (found by reflection, so new systems are covered
/// automatically). The BlueprintDb and Terrain are not stored (see <see cref="ExternalSet"/>).
/// SEQUENTIAL ONLY: never call while a tick runs (sim thread between ticks, or runner stopped/not started).
/// </summary>
public static class WorldState
{
    /// <summary>Bumped when the encoding itself changes (type layouts are checked separately per type).</summary>
    public const int FormatVersion = 1;

    /// <summary>A static ConditionalWeakTable&lt;SimWorld, T&gt; field holding per-world state.</summary>
    public sealed class WorldTable
    {
        public required string Key;
        public required FieldInfo Field;
        public required Type ValueType;
        private MethodInfo? _tryGet, _addOrUpdate;

        public object? Get(SimWorld w)
        {
            var table = Field.GetValue(null);
            if (table == null) return null;
            _tryGet ??= table.GetType().GetMethod("TryGetValue")!;
            var args = new object?[] { w, null };
            return (bool)_tryGet.Invoke(table, args)! ? args[1] : null;
        }

        public void Set(SimWorld w, object value)
        {
            var table = Field.GetValue(null) ?? throw new InvalidOperationException($"{Key} is not initialized");
            _addOrUpdate ??= table.GetType().GetMethod("AddOrUpdate")!;
            _addOrUpdate.Invoke(table, [w, value]);
        }
    }

    private static List<WorldTable>? _tables;

    /// <summary>Every saved per-world table of the sim assembly, sorted by key. [NoSave] fields/types are skipped.</summary>
    public static IReadOnlyList<WorldTable> Tables => _tables ??= FindTables();

    private static List<WorldTable> FindTables()
    {
        var list = new List<WorldTable>();
        foreach (var t in typeof(SimWorld).Assembly.GetTypes())
        {
            foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var ft = f.FieldType;
                if (!ft.IsGenericType || ft.GetGenericTypeDefinition() != typeof(ConditionalWeakTable<,>)) continue;
                var args = ft.GetGenericArguments();
                if (args[0] != typeof(SimWorld)) continue;
                if (f.IsDefined(typeof(NoSaveAttribute), false) || TypeLayout.IsNoSaveType(args[1])) continue;
                list.Add(new WorldTable { Key = t.FullName + "." + f.Name, Field = f, ValueType = args[1] });
            }
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return list;
    }

    public static ExternalSet ExternalsOf(BlueprintDb db, Terrain terrain) => new(db, terrain);

    public static byte[] Capture(Simulation sim)
    {
        var o = new ByteWriter(1 << 20);
        Capture(sim, o);
        return o.ToArray();
    }

    public static void Capture(Simulation sim, ByteWriter o, List<(int Offset, Type Type)>? bodyMap = null)
    {
        var w = sim.World;
        var ext = ExternalsOf(w.Blueprints, w.Terrain);
        o.VarU(FormatVersion);
        o.VarU((ulong)ext.Count);
        o.Raw(ext.Fingerprint);
        var g = new GraphWriter(o, ext) { BodyMap = bodyMap };
        g.WriteRef(sim);
        var present = new List<(string key, object value)>();
        foreach (var t in Tables)
            if (t.Get(w) is { } v) present.Add((t.Key, v));
        o.VarU((ulong)present.Count);
        foreach (var (key, value) in present)
        {
            o.Str(key);
            g.WriteRef(value);
        }
        g.Flush();
    }

    /// <summary>Rebuilds a simulation from <see cref="Capture"/> bytes, sharing the given blueprints and terrain
    /// (which must be the ones the state was captured with: same blueprint set, same map).</summary>
    public static Simulation Restore(byte[] data, BlueprintDb db, Terrain terrain) => Restore(new ByteReader(data), db, terrain);

    public static Simulation Restore(ByteReader input, BlueprintDb db, Terrain terrain)
    {
        ulong version = input.VarU();
        if (version != FormatVersion) throw new IncompatibleSaveException($"State format {version} is not supported (this build reads {FormatVersion})");
        var ext = ExternalsOf(db, terrain);
        int count = input.Count(int.MaxValue);
        ulong fp = input.Raw<ulong>();
        if (count != ext.Count || fp != ext.Fingerprint)
            throw new IncompatibleSaveException("The blueprints or the map of this file differ from the ones of this build");
        var g = new GraphReader(input, ext);
        if (g.ReadRef() is not Simulation sim) throw ByteReader.Corrupt("no simulation root");
        int n = input.Count(Tables.Count);
        var attached = new List<(WorldTable table, object value)>();
        for (int i = 0; i < n; i++)
        {
            string key = input.Str() ?? throw ByteReader.Corrupt("table key");
            var table = Tables.FirstOrDefault(t => t.Key == key)
                        ?? throw new IncompatibleSaveException($"Per-world state '{key}' does not exist in this build");
            var value = g.ReadRef() ?? throw ByteReader.Corrupt("null table value");
            if (!table.ValueType.IsInstanceOfType(value)) throw ByteReader.Corrupt("table value type");
            attached.Add((table, value));
        }
        g.Complete();
        if (!ReferenceEquals(sim.World?.Blueprints, db) || !ReferenceEquals(sim.World.Terrain, terrain))
            throw ByteReader.Corrupt("world root");
        foreach (var (table, value) in attached) table.Set(sim.World, value);
        g.RunAfterLoad();
        return sim;
    }

    /// <summary>Debug aid for tests: which object body contains byte <paramref name="offset"/> of a capture.</summary>
    public static string Locate(Simulation sim, int offset)
    {
        var map = new List<(int Offset, Type Type)>();
        Capture(sim, new ByteWriter(1 << 20), map);
        int k = map.FindLastIndex(m => m.Offset <= offset);
        return k < 0 ? "header/roots" : $"{map[k].Type} (+{offset - map[k].Offset} bytes into body #{k})";
    }

    /// <summary>Hash of the full serialized state (stronger than <see cref="Simulation.StateHash"/>, much slower).</summary>
    public static ulong FullHash(Simulation sim) => Hash64.Of(Capture(sim));
}

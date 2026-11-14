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
    public const int FormatVersion = 2;

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
        int start = o.Length;
        o.VarU(FormatVersion);
        o.VarU((ulong)ext.Count);
        o.Raw(ext.Fingerprint);
        o.Raw(GameInfo.BlueprintHashOf(w.Blueprints));
        o.Raw(GameInfo.TerrainHashOf(w.Terrain));
        // (11a-2) format 2: the blueprint id table (index → id + content hash), so a build with another blueprint
        // list translates the saved indices by id, and a slot for the offset of the external-key table (written last)
        var bps = w.Blueprints.All;
        var hashes = BlueprintHashes(w.Blueprints);
        o.VarU((ulong)bps.Count);
        for (int i = 0; i < bps.Count; i++) { o.Str(bps[i].Id); o.Raw(hashes[i]); }
        int slot = o.Length;
        o.Raw(0);
        var g = new GraphWriter(o, ext) { BodyMap = bodyMap, UsedExternals = new HashSet<int>() };
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
        o.PatchRaw(slot, o.Length - start);
        var used = g.UsedExternals.ToArray();
        Array.Sort(used);
        o.VarU((ulong)used.Length);
        foreach (int id in used) { o.VarU((ulong)id); o.Str(ext.PathOf(id)); }
    }

    [NoSave] private static readonly ConditionalWeakTable<BlueprintDb, ulong[]> BpHashes = new();

    /// <summary>(11a-2) Content hash of each blueprint (index order), cached per db: tells which ones a save saw differently.</summary>
    public static ulong[] BlueprintHashes(BlueprintDb db) => BpHashes.GetValue(db, static db =>
    {
        var h = new ulong[db.All.Count];
        var o = new ByteWriter(1 << 12);
        for (int i = 0; i < h.Length; i++)
        {
            o.Clear();
            var g = new GraphWriter(o);
            g.WriteRef(db.All[i]);
            g.Flush();
            h[i] = Hash64.Of(o.Span);
        }
        return h;
    });

    /// <summary>Rebuilds a simulation from <see cref="Capture"/> bytes, sharing the given blueprints and terrain
    /// (the map must be the one the state was captured with; (11a-2) the blueprints and the saved type layouts may
    /// differ: ARCHITECTURE "Save migration").</summary>
    public static Simulation Restore(byte[] data, BlueprintDb db, Terrain terrain) => Restore(new ByteReader(data), db, terrain, out _);

    public static Simulation Restore(byte[] data, BlueprintDb db, Terrain terrain, out SaveMigrationReport migration) =>
        Restore(new ByteReader(data), db, terrain, out migration);

    public static Simulation Restore(ByteReader input, BlueprintDb db, Terrain terrain) => Restore(input, db, terrain, out _);

    /// <summary>Same; <paramref name="migration"/> = what had to be translated (not <c>Migrated</c> = the state matched
    /// this build exactly).</summary>
    public static Simulation Restore(ByteReader input, BlueprintDb db, Terrain terrain, out SaveMigrationReport migration)
    {
        int start = input.Position;
        ulong version = input.VarU();
        if (version is < 1 or > FormatVersion) throw new IncompatibleSaveException($"State format {version} is not supported (this build reads 1 to {FormatVersion})");
        var ext = ExternalsOf(db, terrain);
        int count = input.Count(int.MaxValue);
        ulong fp = input.Raw<ulong>();
        ulong bpHash = input.Raw<ulong>();
        bool sameShape = count == ext.Count && fp == ext.Fingerprint;
        if (version == 1 && (!sameShape || bpHash != GameInfo.BlueprintHashOf(db)))
            throw new IncompatibleSaveException("The unit blueprints changed since this file was made (an older save without a blueprint table: it loads only with the same blueprints)");
        if (input.Raw<ulong>() != GameInfo.TerrainHashOf(terrain))
            throw new IncompatibleSaveException("The map of this file differs from the given terrain");
        var g = new GraphReader(input, ext);
        bool bpChanged = false;
        if (version >= 2)
        {
            bpChanged = ReadBlueprintTable(input, db, g, bpHash);
            int extAt = start + input.Raw<int>();
            // the walk order of the shared data follows the blueprint list: any change → map the ids by path
            if (!sameShape || bpChanged) ReadExternalKeys(input.At(extAt), ext, g);
        }
        if (g.ReadRef() is not Simulation sim) throw ByteReader.Corrupt("no simulation root");
        int n = input.Count(int.MaxValue);
        var attached = new List<(WorldTable table, object value)>();
        for (int i = 0; i < n; i++)
        {
            string key = input.Str() ?? throw ByteReader.Corrupt("table key");
            var table = Tables.FirstOrDefault(t => t.Key == key);
            bool derived = bpChanged && BlueprintIndexFields.DerivedTables.Contains(key);
            if (table == null || derived)
            {
                // (11a-2) state this build no longer has (or a cache built from the old blueprints): read and drop
                g.Skipping++;
                try { g.ReadRef(); } finally { g.Skipping--; }
                g.Migration.Report.Add(table == null ? $"per-world state {key}: no longer exists (dropped)" : $"per-world state {key}: rebuilt from the new blueprints");
                continue;
            }
            var value = g.ReadRef() ?? throw ByteReader.Corrupt("null table value");
            if (!table.ValueType.IsInstanceOfType(value)) throw ByteReader.Corrupt("table value type");
            attached.Add((table, value));
        }
        g.Complete();
        if (!ReferenceEquals(sim.World?.Blueprints, db) || !ReferenceEquals(sim.World.Terrain, terrain))
            throw ByteReader.Corrupt("world root");
        foreach (var (table, value) in attached) table.Set(sim.World, value);
        g.RunAfterLoad();
        if (bpChanged) ClampToBlueprints(sim.World, g.Migration.Report);
        migration = g.MigrationReport ?? new SaveMigrationReport();
        return sim;
    }

    /// <summary>(11a-2) Compares the saved blueprint table with <paramref name="db"/>: a different id list sets the
    /// index translation; returns true when any blueprint differs (ids or content).</summary>
    private static bool ReadBlueprintTable(ByteReader input, BlueprintDb db, GraphReader g, ulong savedAllHash)
    {
        int n = input.Count(input.Remaining);
        var ids = new string[n];
        var hashes = new ulong[n];
        for (int i = 0; i < n; i++) { ids[i] = input.Str() ?? throw ByteReader.Corrupt("blueprint id"); hashes[i] = input.Raw<ulong>(); }
        if (savedAllHash == GameInfo.BlueprintHashOf(db) && n == db.All.Count) return false;
        var cur = BlueprintHashes(db);
        bool sameIds = n == db.All.Count;
        for (int i = 0; sameIds && i < n; i++) sameIds = ids[i] == db.All[i].Id;
        var report = g.Migration.Report;
        var changed = new List<string>();
        var map = new int[n];
        for (int i = 0; i < n; i++)
        {
            map[i] = db.IndexOf(ids[i]);
            if (map[i] >= 0 && cur[map[i]] != hashes[i]) changed.Add(ids[i]);
        }
        if (!sameIds)
        {
            g.Migration.SetBlueprintMap(map, ids);
            var gone = ids.Where(id => db.IndexOf(id) < 0).ToList();
            var added = db.All.Select(b => b.Id).Except(ids, StringComparer.Ordinal).ToList();
            report.Add($"blueprint list changed: {n} saved, {db.All.Count} now; indices translated by id" +
                       (added.Count > 0 ? $"; new: {Few(added)}" : "") + (gone.Count > 0 ? $"; gone (unused by this game): {Few(gone)}" : ""));
        }
        if (changed.Count > 0) report.Add($"blueprints changed ({changed.Count}): {Few(changed)}");
        if (sameIds && changed.Count == 0) report.Add("blueprint data changed (shared blueprint data re-linked by path)");
        g.Migration.RebuildFrom = db;
        return true;
    }

    private static string Few(List<string> ids) => ids.Count <= 12 ? string.Join(", ", ids) : string.Join(", ", ids.Take(12)) + $" and {ids.Count - 12} more";

    /// <summary>(11a-2) Saved external ids → this build's objects, by their stable paths.</summary>
    private static void ReadExternalKeys(ByteReader r, ExternalSet ext, GraphReader g)
    {
        int n = r.Count(r.Remaining);
        var ids = new int[n];
        var paths = new string[n];
        int max = -1;
        for (int i = 0; i < n; i++)
        {
            ids[i] = r.Count(int.MaxValue);
            paths[i] = r.Str() ?? throw ByteReader.Corrupt("external key");
            max = Math.Max(max, ids[i]);
        }
        var map = new object?[max + 1];
        var keys = new string?[max + 1];
        for (int i = 0; i < n; i++) { map[ids[i]] = ext.Resolve(paths[i]); keys[ids[i]] = paths[i]; }
        g.ExternalMap = map;
        g.ExternalPaths = keys;
    }

    /// <summary>(11a-2) After a load with changed blueprints: a unit whose saved health is above its new maximum
    /// (veterancy included) is set to that maximum.</summary>
    private static void ClampToBlueprints(SimWorld w, SaveMigrationReport report)
    {
        var u = w.Units;
        int clamped = 0;
        for (int i = 0; i < u.Alive.Length; i++)
        {
            if (!u.Alive[i]) continue;
            float max = Combat.Veterancy.MaxHealth(w, i);
            if (u.Health[i] > max) { u.Health[i] = max; clamped++; }
        }
        if (clamped > 0) report.Add($"{clamped} unit(s) had more health than their new maximum (set to the maximum)");
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

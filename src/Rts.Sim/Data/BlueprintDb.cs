using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rts.Sim.Data;

/// <summary>All unit blueprints, indexed by runtime index and by string id. Immutable after Finalize().</summary>
public sealed class BlueprintDb
{
    private readonly List<UnitBlueprint> _all = [];
    private readonly Dictionary<string, int> _byId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<UnitBlueprint> All => _all;
    public int Count => _all.Count;
    public UnitBlueprint this[int index] => _all[index];
    public UnitBlueprint this[string id] => _all[_byId[id]];

    public bool TryGet(string id, out UnitBlueprint bp)
    {
        if (_byId.TryGetValue(id, out int i)) { bp = _all[i]; return true; }
        bp = null!;
        return false;
    }

    public int IndexOf(string id) => _byId.TryGetValue(id, out int i) ? i : -1;

    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var o = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        o.Converters.Add(new Vector3ArrayConverter());
        return o;
    }

    public void Add(UnitBlueprint bp)
    {
        if (string.IsNullOrWhiteSpace(bp.Id)) throw new InvalidDataException("Blueprint without Id");
        if (_byId.ContainsKey(bp.Id)) throw new InvalidDataException($"Duplicate blueprint id '{bp.Id}'");
        bp.Index = _all.Count;
        _all.Add(bp);
        _byId[bp.Id] = bp.Index;
    }

    /// <summary>Loads every *.json under the directory (recursively). Each file holds one blueprint object.</summary>
    public static BlueprintDb LoadDirectory(string dir)
    {
        var db = new BlueprintDb();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            try
            {
                var bp = JsonSerializer.Deserialize<UnitBlueprint>(File.ReadAllText(file), JsonOptions)
                         ?? throw new InvalidDataException("empty file");
                if (string.IsNullOrEmpty(bp.Id)) bp.Id = Path.GetFileNameWithoutExtension(file);
                db.Add(bp);
            }
            catch (Exception e) when (e is not InvalidDataException)
            {
                throw new InvalidDataException($"Error loading blueprint '{file}': {e.Message}", e);
            }
        }
        db.FinalizeDb();
        return db;
    }

    /// <summary>Resolves string references and derived flags. Must be called once after all Add() calls.</summary>
    public void FinalizeDb()
    {
        foreach (var bp in _all)
        {
            bp.CategoryFlags = UnitCategory.None;
            foreach (var c in bp.Categories)
            {
                if (!Enum.TryParse<UnitCategory>(c, true, out var flag))
                    throw new InvalidDataException($"Blueprint '{bp.Id}': unknown category '{c}'");
                bp.CategoryFlags |= flag;
            }
            if ((bp.CategoryFlags & UnitCategory.Structure) == 0) bp.CategoryFlags |= UnitCategory.Mobile;

            foreach (var w in bp.Weapons)
            {
                w.TargetMask = LayerMask.None;
                foreach (var l in w.TargetLayers)
                {
                    if (!Enum.TryParse<LayerMask>(l, true, out var m))
                        throw new InvalidDataException($"Blueprint '{bp.Id}' weapon '{w.Name}': unknown layer '{l}'");
                    w.TargetMask |= m;
                }
            }

            var builds = new List<int>();
            foreach (var id in bp.Builds)
            {
                if (!_byId.TryGetValue(id, out int idx))
                    throw new InvalidDataException($"Blueprint '{bp.Id}': Builds references unknown id '{id}'");
                builds.Add(idx);
            }
            bp.BuildIndices = builds.ToArray();

            if (bp.IsMobile && bp.Movement.Layer == MotionLayer.None)
                throw new InvalidDataException($"Blueprint '{bp.Id}': mobile unit needs Movement.Layer");
            if (bp.Economy.BuildTime <= 0) bp.Economy.BuildTime = 1;
        }

        // Second pass (needs every BuildIndices): BuiltBy back-references and upgrade chains.
        foreach (var bp in _all)
        {
            foreach (var id in bp.BuiltBy)
            {
                if (!_byId.TryGetValue(id, out int builder)) continue;
                var b = _all[builder];
                if (Array.IndexOf(b.BuildIndices, bp.Index) < 0) b.BuildIndices = [.. b.BuildIndices, bp.Index];
            }

            bp.UpgradesToIndex = -1;
            if (!string.IsNullOrEmpty(bp.UpgradesTo))
            {
                if (!_byId.TryGetValue(bp.UpgradesTo, out int to))
                    throw new InvalidDataException($"Blueprint '{bp.Id}': UpgradesTo references unknown id '{bp.UpgradesTo}'");
                if (to == bp.Index) throw new InvalidDataException($"Blueprint '{bp.Id}': UpgradesTo itself");
                bp.UpgradesToIndex = to;
            }
        }
        foreach (var bp in _all) bp.UpgradesFromIndex = -1;
        foreach (var bp in _all)
        {
            if (bp.UpgradesToIndex < 0) continue;
            var to = _all[bp.UpgradesToIndex];
            if (to.UpgradesFromIndex >= 0)
                throw new InvalidDataException($"Blueprint '{to.Id}': upgraded into from both '{_all[to.UpgradesFromIndex].Id}' and '{bp.Id}'");
            to.UpgradesFromIndex = bp.Index;
        }
    }
}

/// <summary>Reads/writes Vector3 as a JSON array [x, y, z].</summary>
public sealed class Vector3ArrayConverter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Vector3 must be [x,y,z]");
        Span<float> v = stackalloc float[3];
        int n = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (n < 3) v[n] = reader.GetSingle();
            n++;
        }
        if (n != 3) throw new JsonException("Vector3 must have 3 numbers");
        return new Vector3(v[0], v[1], v[2]);
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }
}

/// <summary>Locates the repository "data" folder from the running executable.</summary>
public static class DataPaths
{
    public static string FindDataRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "blueprints");
            if (Directory.Exists(candidate)) return Path.Combine(dir.FullName, "data");
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the 'data' folder (with data/blueprints) above " + AppContext.BaseDirectory);
    }

    public static string UnitBlueprintDir => Path.Combine(FindDataRoot(), "blueprints", "units");
}

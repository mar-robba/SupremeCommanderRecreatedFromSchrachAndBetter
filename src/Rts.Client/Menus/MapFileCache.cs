using Rts.Sim.Map;

namespace Rts.Client.Menus;

/// <summary>
/// (8b-2) Client-side cache of the installed map files (<see cref="MapLibrary"/>, data/maps): the header list for the
/// setup picker and decoded files by id (immutable terrains; thread-safe, the preview loads them on a worker).
/// </summary>
public static class MapFileCache
{
    private static readonly object Lock = new();
    private static readonly Dictionary<string, MapFile> Files = new(StringComparer.OrdinalIgnoreCase);
    private static List<MapFileInfo>? _list;

    /// <summary>Installed maps (header only), sorted by players, size, then name.</summary>
    public static IReadOnlyList<MapFileInfo> List
    {
        get
        {
            lock (Lock)
                return _list ??= MapLibrary.List().OrderBy(m => m.Players).ThenBy(m => m.Width).ThenBy(m => m.Name, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>(8b-3) Re-scan the folders (a map was downloaded).</summary>
    public static void Invalidate()
    {
        lock (Lock) _list = null;
    }

    public static MapFileInfo? Info(string id) =>
        List.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? (File.Exists(id) ? TryHeader(id) : null);

    /// <summary>The decoded map (cached); <see cref="MapFileException"/> when missing or damaged.</summary>
    public static MapFile Get(string id)
    {
        lock (Lock)
            if (Files.TryGetValue(id, out var f)) return f;
        var loaded = MapLibrary.Load(id);
        lock (Lock) Files[id] = loaded;
        return loaded;
    }

    /// <summary>Step through "Random map" (null) and the installed maps.</summary>
    public static string? Next(string? current, int dir)
    {
        var ids = new List<string?> { null };
        ids.AddRange(List.Select(m => m.Id));
        int i = Math.Max(0, ids.FindIndex(x => string.Equals(x, current, StringComparison.OrdinalIgnoreCase)));
        return ids[((i + dir) % ids.Count + ids.Count) % ids.Count];
    }

    private static MapFileInfo? TryHeader(string path)
    {
        try { return MapFile.ReadHeader(path); }
        catch (Exception e) when (e is MapFileException or IOException) { return null; }
    }
}

using Rts.Sim.Data;

namespace Rts.Sim.Map;

/// <summary>
/// (8b-2) The installed map files: <c>data/maps/*.rtsmap</c> (found like the blueprints, from the executable up).
/// A map is named by its id = file name without the extension; <see cref="Resolve"/> also accepts a path.
/// Listing reads only the headers.
/// </summary>
public static class MapLibrary
{
    public static string DefaultDir => Path.Combine(DataPaths.FindDataRoot(), "maps");

    /// <summary>Headers of every readable map file in the folder, by id (ordinal); damaged files are skipped.</summary>
    public static List<MapFileInfo> List(string? dir = null)
    {
        var result = new List<MapFileInfo>();
        dir ??= SafeDefaultDir();
        if (dir == null || !Directory.Exists(dir)) return result;
        var files = Directory.GetFiles(dir, "*" + MapFile.Extension);
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var f in files)
        {
            try { result.Add(MapFile.ReadHeader(f)); }
            catch (Exception e) when (e is MapFileException or IOException or UnauthorizedAccessException) { }
        }
        return result;
    }

    /// <summary>The file of a map id (or an existing path); case-insensitive id match as a fallback. Null = not found.</summary>
    public static string? Resolve(string nameOrPath, string? dir = null)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return null;
        if (File.Exists(nameOrPath)) return Path.GetFullPath(nameOrPath);
        dir ??= SafeDefaultDir();
        if (dir == null || !Directory.Exists(dir)) return null;
        string id = nameOrPath.EndsWith(MapFile.Extension, StringComparison.OrdinalIgnoreCase)
            ? nameOrPath[..^MapFile.Extension.Length] : nameOrPath;
        var exact = Path.Combine(dir, id + MapFile.Extension);
        if (File.Exists(exact)) return exact;
        var files = Directory.GetFiles(dir, "*" + MapFile.Extension);
        Array.Sort(files, StringComparer.Ordinal);
        return files.FirstOrDefault(f => string.Equals(MapFile.IdOf(f), id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Loads a map by id or path; <see cref="MapFileException"/> when missing or damaged.</summary>
    public static MapFile Load(string nameOrPath, string? dir = null)
    {
        var path = Resolve(nameOrPath, dir) ?? throw new MapFileException($"Map file '{nameOrPath}' not found (looked in {dir ?? SafeDefaultDir() ?? "data/maps"})");
        return MapFile.Load(path);
    }

    private static string? SafeDefaultDir()
    {
        try { return DefaultDir; }
        catch (DirectoryNotFoundException) { return null; }
    }
}

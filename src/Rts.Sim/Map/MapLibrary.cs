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

    /// <summary>(8b-3) Maps received from a multiplayer host (app configuration, not sim state): <c>downloaded_maps</c>
    /// relative to the current directory in dev mode, set to the user data folder by <c>UserDirs.Apply</c> in release.
    /// <see cref="List"/>, <see cref="Resolve"/> and <see cref="Find"/> also look here (after data/maps).</summary>
    public static string DownloadDir { get; set; } = "downloaded_maps";

    /// <summary>Headers of every readable map file in the folder (null = data/maps, then <see cref="DownloadDir"/>), by
    /// id (ordinal); damaged files are skipped.</summary>
    public static List<MapFileInfo> List(string? dir = null)
    {
        if (dir == null)
        {
            var all = ListDir(SafeDefaultDir());
            all.AddRange(ListDir(DownloadDir));
            return all;
        }
        return ListDir(dir);
    }

    /// <summary>(8b-3) The file of map <paramref name="id"/> whose content hash is <paramref name="hash"/>: data/maps
    /// first, then <paramref name="downloadDir"/> (default <see cref="DownloadDir"/>); same id first, then any file with
    /// that hash (a renamed copy). Null = not installed.</summary>
    public static string? Find(string id, ulong hash, string? downloadDir = null)
    {
        string?[] dirs = [SafeDefaultDir(), downloadDir ?? DownloadDir];
        foreach (var dir in dirs)
        {
            if (dir == null || !Directory.Exists(dir)) continue;
            var exact = Path.Combine(dir, id + MapFile.Extension);
            if (File.Exists(exact) && HashOf(exact) == hash) return Path.GetFullPath(exact);
        }
        foreach (var dir in dirs)
            foreach (var m in ListDir(dir))
                if (m.Hash == hash && m.Path != null) return Path.GetFullPath(m.Path);
        return null;
    }

    private static ulong? HashOf(string path)
    {
        try { return MapFile.ReadHeader(path).Hash; }
        catch (Exception e) when (e is MapFileException or IOException or UnauthorizedAccessException) { return null; }
    }

    private static List<MapFileInfo> ListDir(string? dir)
    {
        var result = new List<MapFileInfo>();
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
        if (dir == null) return ResolveIn(nameOrPath, SafeDefaultDir()) ?? ResolveIn(nameOrPath, DownloadDir); // (8b-3)
        return ResolveIn(nameOrPath, dir);
    }

    private static string? ResolveIn(string nameOrPath, string? dir)
    {
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

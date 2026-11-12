namespace Rts.Sim.Persistence;

/// <summary>An entry of a save/replay folder listing (header only; <see cref="Error"/> set if unreadable).</summary>
public sealed record SaveEntry(string Path, GameInfo? Info, string? Error, DateTime Modified)
{
    public string FileName => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>Save and replay folders (relative to the working directory): naming, listing, pruning.</summary>
public static class SaveStore
{
    public static string SavesDir = "saves";
    public static string ReplaysDir = "replays";
    public const string SaveExt = ".rtssave";
    public const string ReplayExt = ".rtsreplay";
    public const string QuickSaveName = "quicksave";
    /// <summary>Only the newest replays are kept.</summary>
    public static int MaxReplays = 50;

    public static string SavePath(string name) => Path.Combine(SavesDir, Sanitize(name) + SaveExt);

    /// <summary>Writes a replay file named after its date and map, then keeps only the newest <see cref="MaxReplays"/>.</summary>
    public static string WriteReplay(byte[] file, GameInfo info)
    {
        Directory.CreateDirectory(ReplaysDir);
        string map = info.Map == null ? Sanitize(info.MapFile ?? "custom") : $"{info.Map.Preset}{info.Map.Size}";
        string baseName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{map}_{info.Players.Count}p";
        string path = Path.Combine(ReplaysDir, baseName + ReplayExt);
        for (int k = 2; File.Exists(path); k++) path = Path.Combine(ReplaysDir, $"{baseName}_{k}{ReplayExt}");
        WriteAtomic(path, file);
        Prune(ReplaysDir, ReplayExt, MaxReplays);
        return path;
    }

    public static string WriteSave(byte[] file, string name)
    {
        Directory.CreateDirectory(SavesDir);
        string path = SavePath(name);
        WriteAtomic(path, file);
        return path;
    }

    /// <summary>Write to a temp file then move: a crash never leaves a half-written save over a good one.</summary>
    public static void WriteAtomic(string path, byte[] data)
    {
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }

    public static List<SaveEntry> ListSaves() => List(SavesDir, SaveExt);
    public static List<SaveEntry> ListReplays() => List(ReplaysDir, ReplayExt);

    /// <summary>Files of a folder, newest first, with their headers.</summary>
    public static List<SaveEntry> List(string dir, string ext)
    {
        var list = new List<SaveEntry>();
        if (!Directory.Exists(dir)) return list;
        foreach (var path in Directory.EnumerateFiles(dir, "*" + ext))
        {
            var modified = File.GetLastWriteTimeUtc(path);
            try { list.Add(new SaveEntry(path, SaveContainer.ReadInfo(path, out _), null, modified)); }
            catch (Exception e) when (e is SaveFormatException or IOException or UnauthorizedAccessException)
            {
                list.Add(new SaveEntry(path, null, e.Message, modified));
            }
        }
        list.Sort((a, b) => b.Modified.CompareTo(a.Modified));
        return list;
    }

    public static void Prune(string dir, string ext, int keep)
    {
        if (!Directory.Exists(dir)) return;
        var files = Directory.EnumerateFiles(dir, "*" + ext).Select(p => new FileInfo(p)).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        foreach (var f in files.Skip(keep))
        {
            try { f.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public static string Sanitize(string name)
    {
        var chars = name.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' ? c : '_').ToArray();
        var s = new string(chars).Trim();
        return s.Length == 0 ? "save" : s.Length > 60 ? s[..60] : s;
    }
}

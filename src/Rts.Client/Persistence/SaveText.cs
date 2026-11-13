using System.Globalization;
using Rts.Client.Menus;
using Rts.Sim.Map;
using Rts.Sim.Persistence;

namespace Rts.Client.Persistence;

/// <summary>Texts of the save/load and replay menus (pure helpers, unit tested).</summary>
public static class SaveText
{
    /// <summary>Game length: "m:ss", or "h:mm:ss" from one hour.</summary>
    public static string Length(float seconds)
    {
        int s = Math.Max(0, (int)seconds);
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    /// <summary>Local date and time of a file, e.g. "2026-10-13 14:05".</summary>
    public static string Date(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Default name of a new save: map and local date/time, e.g. "Plains 256 2026-10-13 14-05" (file-name safe).</summary>
    public static string DefaultSaveName(GameConfig cfg, DateTime now)
    {
        string map = cfg.Scene != null ? $"Scene {cfg.Scene}" : $"{MapPresets.Name(cfg.Preset)} {cfg.MapSize}";
        string kind = cfg.Sandbox ? "Sandbox " : "";
        return SaveStore.Sanitize($"{kind}{map} {now.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture)}");
    }

    /// <summary>True if <paramref name="name"/> would overwrite one of the <paramref name="existing"/> saves.</summary>
    public static bool Overwrites(string name, IEnumerable<SaveEntry> existing)
    {
        string path = Path.GetFullPath(SaveStore.SavePath(name));
        return existing.Any(e => string.Equals(Path.GetFullPath(e.Path), path, StringComparison.Ordinal));
    }

    /// <summary>Title of a list entry: the save name (or the file name), "quicksave" / "autosave_N" marked.</summary>
    public static string Title(SaveEntry e)
    {
        string name = e.Info?.Name is { Length: > 0 } n ? n : e.FileName;
        if (Autosave.Title(e.FileName, name) is string auto) return auto;
        if (e.FileName != SaveStore.QuickSaveName) return name;
        return name == SaveStore.QuickSaveName ? "Quicksave" : $"{name} (quicksave)";
    }

    /// <summary>Second line of a list entry: map, players, length (or the error of an unreadable file).</summary>
    public static string Details(SaveEntry e)
    {
        if (e.Info is not GameInfo i) return "Unreadable: " + (e.Error ?? "unknown error");
        return $"{i.MapDescription}  ·  {i.PlayersDescription}  ·  {Length(i.GameSeconds)}";
    }

    /// <summary>Short tags of an entry: sandbox, result, version warning.</summary>
    public static List<string> Tags(SaveEntry e, bool replay)
    {
        var tags = new List<string>();
        if (e.Info is not GameInfo i) return tags;
        if (i.Sandbox) tags.Add("SANDBOX");
        if (i.Scene != null) tags.Add("SCENE");
        if (i.Multiplayer != null) tags.Add(i.Multiplayer.Auto ? "MULTIPLAYER AUTOSAVE" : "MULTIPLAYER"); // (10a-3)
        if (replay && i.WinningTeam >= 0)
        {
            var winners = i.Players.Where(p => p.Team == i.WinningTeam).Select(p => p.IsAI ? $"AI {p.Difficulty}" : p.Name).ToList();
            tags.Add(winners.Count > 0 ? "WINNER: " + string.Join(", ", winners).ToUpperInvariant() : "FINISHED");
        }
        if (replay && !SaveGame.ReplayMathMatches(i)) tags.Add("OLD VERSION"); // (10a-1) refused on open
        else if (replay && i.SimBuild != GameInfo.CurrentSimBuild) tags.Add("OTHER BUILD");
        return tags;
    }

    /// <summary>User-facing text of a load/save error (no stack traces).</summary>
    public static string ErrorMessage(Exception e)
    {
        e = e is AggregateException { InnerExceptions.Count: 1 } ag ? ag.InnerExceptions[0] : e;
        return e switch
        {
            IncompatibleSaveException => "Incompatible file: " + e.Message,
            SaveFormatException => "Damaged or invalid file: " + e.Message,
            FileNotFoundException or DirectoryNotFoundException => "File not found: " + e.Message,
            IOException or UnauthorizedAccessException => "Cannot access the file: " + e.Message,
            _ => $"Unexpected error ({e.GetType().Name}): {e.Message}",
        };
    }
}

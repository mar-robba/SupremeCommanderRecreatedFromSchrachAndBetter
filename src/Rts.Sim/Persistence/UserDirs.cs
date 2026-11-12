namespace Rts.Sim.Persistence;

/// <summary>Where the game keeps the player's files (see <see cref="UserDirs"/>).</summary>
/// <param name="Release">True when a <c>release.txt</c> marker sits next to the executable (packaged build).</param>
/// <param name="Version">The marker's first line (the package version label), null in dev mode.</param>
/// <param name="SavesDir">Saves folder: <c>saves</c> (relative to the current directory) in dev mode, absolute in release.</param>
/// <param name="ReplaysDir">Replays folder, same rule.</param>
/// <param name="ScreenshotsDir">In-game screenshots folder, same rule.</param>
/// <param name="ConfigDir">Always absolute: holds <c>settings.json</c>.</param>
/// <param name="UserDataDir">Always absolute: the release saves/replays/screenshots parent; <c>crash.log</c> is here in both modes.</param>
public sealed record UserDirLayout(bool Release, string? Version, string SavesDir, string ReplaysDir, string ScreenshotsDir,
    string ConfigDir, string UserDataDir);

/// <summary>
/// User folders (app configuration, not sim state). Dev mode (no marker: <c>dotnet run</c>, tests, Rts.Bench):
/// saves/replays/screenshots stay relative to the current directory as always. Release mode (a <c>release.txt</c>
/// marker next to the executable, written by <c>tools/package/package.sh</c>): they live in the user data folder
/// <c>$XDG_DATA_HOME/RtsGame</c> (default <c>~/.local/share/RtsGame</c>; Windows <c>%APPDATA%\RtsGame</c>).
/// Settings are always in <c>$XDG_CONFIG_HOME/RtsGame</c> (default <c>~/.config/RtsGame</c>; Windows <c>%APPDATA%\RtsGame</c>).
/// <c>RTS_USER_DIR</c> (env) replaces the user data folder in both modes (and forces release-style folders).
/// The client calls <see cref="Apply"/> once at startup; tests and the bench never do. docs/DEV_GUIDE.md "Packaging".
/// </summary>
public static class UserDirs
{
    public const string AppFolder = "RtsGame";
    public const string ReleaseMarker = "release.txt";

    private static UserDirLayout? _current;

    /// <summary>The layout of this process (resolved on first use from the real executable folder and environment).</summary>
    public static UserDirLayout Current => _current ??= ResolveForProcess();

    public static UserDirLayout ResolveForProcess() =>
        Resolve(AppContext.BaseDirectory, Environment.GetEnvironmentVariable, OperatingSystem.IsWindows(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ReadMarker);

    /// <summary>Points <see cref="SaveStore"/> at the resolved folders. Call once at client startup.</summary>
    public static UserDirLayout Apply()
    {
        var l = Current;
        SaveStore.SavesDir = l.SavesDir;
        SaveStore.ReplaysDir = l.ReplaysDir;
        // (8b-3) maps received in multiplayer: next to saves/ (dev: ./downloaded_maps, release: the user data folder)
        Rts.Sim.Map.MapLibrary.DownloadDir = Path.Combine(Path.GetDirectoryName(l.SavesDir) ?? "", "downloaded_maps");
        return l;
    }

    /// <summary>Pure resolver. <paramref name="readMarker"/> returns the marker file's text or null if it doesn't exist.</summary>
    public static UserDirLayout Resolve(string exeDir, Func<string, string?> env, bool windows, string home,
        Func<string, string?>? readMarker = null)
    {
        readMarker ??= ReadMarker;
        string? marker = readMarker(Path.Combine(exeDir, ReleaseMarker));
        bool release = marker != null;
        string? version = release ? FirstLine(marker!) : null;

        string? appData = windows ? NonEmpty(env("APPDATA")) ?? Path.Combine(home, "AppData", "Roaming") : null;
        string configBase = windows ? appData! : NonEmpty(env("XDG_CONFIG_HOME")) ?? Path.Combine(home, ".config");
        string dataBase = windows ? appData! : NonEmpty(env("XDG_DATA_HOME")) ?? Path.Combine(home, ".local", "share");
        string configDir = Path.Combine(configBase, AppFolder);
        string? overrideDir = NonEmpty(env("RTS_USER_DIR"));
        string dataDir = overrideDir ?? Path.Combine(dataBase, AppFolder);

        bool userFolders = release || overrideDir != null;
        return new UserDirLayout(release, version,
            userFolders ? Path.Combine(dataDir, "saves") : "saves",
            userFolders ? Path.Combine(dataDir, "replays") : "replays",
            userFolders ? Path.Combine(dataDir, "screenshots") : "screenshots",
            configDir, dataDir);
    }

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string FirstLine(string text)
    {
        foreach (var line in text.Split('\n'))
            if (line.Trim() is { Length: > 0 } t) return t;
        return "";
    }

    private static string? ReadMarker(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }
}

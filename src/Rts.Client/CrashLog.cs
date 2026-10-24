using System.Runtime.InteropServices;
using Rts.Sim.Persistence;

namespace Rts.Client;

/// <summary>
/// Crash log: unhandled exceptions (main thread, other threads, unobserved tasks) are appended with time, version and
/// the full exception to <c>&lt;user data dir&gt;/crash.log</c> (<see cref="UserDirs"/>: ~/.local/share/RtsGame in both dev
/// and release mode), trimmed to the last ~256 KB. The runtime still prints the exception to stderr and exits
/// non-zero. A fatal crash in an interactive run leaves <c>crash.pending</c>; the next start shows a notice in the main
/// menu (<see cref="PreviousCrash"/>). Sim-thread faults are caught by SimRunner (shown in the HUD) and are not here.
/// </summary>
public static class CrashLog
{
    public const int MaxChars = 256 * 1024;
    public const string EntryHeader = "===== ";

    public static string FilePath => Path.Combine(UserDirs.Current.UserDataDir, "crash.log");
    private static string PendingPath => Path.Combine(UserDirs.Current.UserDataDir, "crash.pending");

    /// <summary>Set at startup when the previous interactive run crashed: the crash log path.</summary>
    public static string? PreviousCrash { get; private set; }

    private static readonly object Lock = new();
    private static Exception? _last;
    private static bool _interactive, _installed;

    /// <summary>Hooks the process-wide handlers. <paramref name="interactive"/> = not an automated (screenshot) run.</summary>
    public static void Install(bool interactive)
    {
        if (_installed) return;
        _installed = true;
        _interactive = interactive;
        if (interactive)
        {
            try
            {
                if (File.Exists(PendingPath)) { PreviousCrash = FilePath; File.Delete(PendingPath); }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Record(ex, "unhandled", fatal: e.IsTerminating);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Record(e.Exception, "unobserved task", fatal: false);
    }

    /// <summary>Appends <paramref name="e"/> to the log (once per exception object; bad command-line arguments are skipped).</summary>
    public static void Record(Exception e, string where, bool fatal = true)
    {
        if (IsArgumentError(e)) return;
        lock (Lock)
        {
            if (ReferenceEquals(e, _last)) return;
            _last = e;
            try
            {
                Append(FilePath, FormatEntry(DateTime.Now, AppVersion.Label, where, fatal, e), MaxChars);
                if (fatal && _interactive) File.WriteAllText(PendingPath, FilePath);
                Console.Error.WriteLine("Crash log: " + FilePath);
            }
            catch (Exception io) { Console.Error.WriteLine("Could not write the crash log: " + io.Message); }
        }
    }

    /// <summary>Unknown/malformed command-line arguments (thrown by ClientOptions.Parse) are usage errors, not crashes.</summary>
    public static bool IsArgumentError(Exception e) =>
        e is ArgumentException or FormatException && e.StackTrace?.Contains(nameof(ClientOptions) + "." + nameof(ClientOptions.Parse)) == true;

    public static string FormatEntry(DateTime time, string version, string where, bool fatal, Exception e) =>
        $"{EntryHeader}{time:yyyy-MM-dd HH:mm:ss} {version} ({where}{(fatal ? "" : ", non-fatal")}) =====\n" +
        $"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}, .NET {Environment.Version}\n{e}\n\n";

    /// <summary>Appends an entry, then trims the file to its newest entries when it grows past <paramref name="maxChars"/>.</summary>
    public static void Append(string path, string entry, int maxChars)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.AppendAllText(path, entry);
        if (new FileInfo(path).Length <= maxChars) return;
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, Trim(File.ReadAllText(path), maxChars));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Keeps about the newest 3/4 of <paramref name="maxChars"/>, starting at an entry header when there is one.</summary>
    public static string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        int cut = text.Length - maxChars * 3 / 4;
        int next = text.IndexOf(EntryHeader, cut, StringComparison.Ordinal);
        return next >= 0 ? text[next..] : text[cut..];
    }
}

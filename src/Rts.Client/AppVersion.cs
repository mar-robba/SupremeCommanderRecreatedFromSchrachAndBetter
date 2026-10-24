using System.Reflection;
using Rts.Sim.Persistence;

namespace Rts.Client;

/// <summary>
/// The client's version label, e.g. <c>v0.7.0 (4cee893)</c>, plus <c> dev</c> outside a packaged build
/// (<see cref="UserDirs"/> release marker). The number is <c>&lt;Version&gt;</c> in Rts.Client.csproj; the part after '+'
/// is the git commit (SourceLink, full hash) or <c>git describe --always --dirty</c> passed by tools/package/package.sh.
/// Shown in the main menu corner and written into crash.log.
/// </summary>
public static class AppVersion
{
    public static string Informational =>
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    private static string? _label;
    public static string Label => _label ??= Format(Informational, UserDirs.Current.Release);

    /// <summary>"0.7.0+3fd01cf9f6bd…" (40-hex hash, shortened) or "0.7.0+4cee893-dirty" → "v0.7.0 (…)" (+ " dev").</summary>
    public static string Format(string informational, bool release)
    {
        int plus = informational.IndexOf('+');
        string ver = plus < 0 ? informational : informational[..plus];
        string meta = plus < 0 ? "" : informational[(plus + 1)..];
        if (meta.Length >= 40 && meta.All(Uri.IsHexDigit)) meta = meta[..7];
        string s = "v" + ver + (meta.Length > 0 ? $" ({meta})" : "");
        return release ? s : s + " dev";
    }
}

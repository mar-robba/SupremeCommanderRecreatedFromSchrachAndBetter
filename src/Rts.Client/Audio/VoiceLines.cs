using System.Text.Json;

namespace Rts.Client.Audio;

/// <summary>One spoken line of <c>data/audio/voice/lines.json</c> (the generator tools/voice/generate.py reads the same file).</summary>
/// <param name="Priority">Higher cuts in front of waiting lines (a line being spoken is never interrupted).</param>
/// <param name="Cooldown">Seconds before the same line may be queued again.</param>
/// <param name="MaxWait">Seconds a queued line may wait before it is dropped as stale.</param>
public sealed record VoiceLine(string Id, int Priority, double Cooldown, double MaxWait, IReadOnlyDictionary<string, string> Text);

/// <summary>(11b-1) The voice line ids the game raises (must match lines.json; a test checks it).</summary>
public static class VoiceIds
{
    public const string NukeLaunchDetected = "nuke_launch_detected", StrategicMissileLaunched = "strategic_missile_launched",
        CommanderUnderAttack = "commander_under_attack", EnemyCommanderDestroyed = "enemy_commander_destroyed",
        AllyCommanderDestroyed = "ally_commander_destroyed", EnemyDefeated = "enemy_defeated", AllyDefeated = "ally_defeated",
        ExperimentalDetected = "experimental_detected", TacticalLaunchDetected = "tactical_launch_detected",
        BaseUnderAttack = "base_under_attack", UnitsUnderAttack = "units_under_attack", AllyUnderAttack = "ally_under_attack",
        StructureLost = "structure_lost", ExperimentalComplete = "experimental_complete",
        EnhancementComplete = "enhancement_complete", UpgradeComplete = "upgrade_complete", MassLow = "mass_low",
        EnergyLow = "energy_low", UnitCapReached = "unit_cap_reached";

    public static readonly string[] All =
    [
        NukeLaunchDetected, StrategicMissileLaunched, CommanderUnderAttack, EnemyCommanderDestroyed, AllyCommanderDestroyed,
        EnemyDefeated, AllyDefeated, ExperimentalDetected, TacticalLaunchDetected, BaseUnderAttack, UnitsUnderAttack,
        AllyUnderAttack, StructureLost, ExperimentalComplete, EnhancementComplete, UpgradeComplete, MassLow, EnergyLow,
        UnitCapReached,
    ];
}

/// <summary>
/// (11b-1) The line table (<c>data/audio/voice/lines.json</c>) and where the recorded files live:
/// <c>data/audio/voice/&lt;language&gt;/&lt;id&gt;.wav</c>, 16-bit mono PCM at <see cref="SampleRate"/>. Pure (no raylib).
/// </summary>
public sealed class VoiceLineTable
{
    /// <summary>The spoken language (owner decision: Italian voice, English on-screen text).</summary>
    public const string DefaultLanguage = "it";
    /// <summary>Sample rate of the voice files (tools/voice/generate.py SAMPLE_RATE); raylib converts to the device rate.</summary>
    public const int SampleRate = 22050;
    public const double DefaultMaxWait = 4.0;

    public readonly IReadOnlyList<VoiceLine> Lines;
    public readonly IReadOnlyList<string> Languages;
    private readonly Dictionary<string, VoiceLine> _byId = new();

    public VoiceLineTable(IReadOnlyList<VoiceLine> lines, IReadOnlyList<string> languages)
    {
        Lines = lines;
        Languages = languages;
        foreach (var l in lines) _byId[l.Id] = l;
    }

    public VoiceLine? this[string id] => _byId.GetValueOrDefault(id);

    /// <summary>The voice folder: <c>&lt;data&gt;/audio/voice</c>.</summary>
    public static string DefaultDir() => Path.Combine(Rts.Sim.Data.DataPaths.FindDataRoot(), "audio", "voice");

    public static string FilePath(string voiceDir, string language, string id) => Path.Combine(voiceDir, language, id + ".wav");

    /// <summary>Parses lines.json; throws on malformed json (callers that must not fail use <see cref="TryLoad"/>).</summary>
    public static VoiceLineTable Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        var langs = new List<string>();
        if (root.TryGetProperty("languages", out var la))
            foreach (var l in la.EnumerateArray()) langs.Add(l.GetString() ?? "");
        var lines = new List<VoiceLine>();
        foreach (var e in root.GetProperty("lines").EnumerateArray())
        {
            var text = new Dictionary<string, string>();
            if (e.TryGetProperty("text", out var t))
                foreach (var p in t.EnumerateObject()) text[p.Name] = p.Value.GetString() ?? "";
            lines.Add(new VoiceLine(
                e.GetProperty("id").GetString() ?? "",
                e.TryGetProperty("priority", out var pr) ? pr.GetInt32() : 0,
                e.TryGetProperty("cooldown", out var cd) ? cd.GetDouble() : 0,
                e.TryGetProperty("maxWait", out var mw) ? mw.GetDouble() : DefaultMaxWait,
                text));
        }
        return new VoiceLineTable(lines, langs);
    }

    /// <summary>Loads <c>&lt;voiceDir&gt;/lines.json</c>; null (and a console message) when missing or broken.</summary>
    public static VoiceLineTable? TryLoad(string voiceDir)
    {
        try
        {
            return Parse(File.ReadAllText(Path.Combine(voiceDir, "lines.json")));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Voice lines not loaded: " + e.Message);
            return null;
        }
    }
}

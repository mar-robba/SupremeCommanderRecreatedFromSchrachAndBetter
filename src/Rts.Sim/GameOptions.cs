using System.Text.Json.Serialization;

namespace Rts.Sim;

/// <summary>When an army is defeated (SupCom lobby "Victory Condition"). Append-only.</summary>
public enum VictoryCondition : byte
{
    /// <summary>An army loses when its commander dies; its other units self-destruct (SupCom default).</summary>
    Assassination,
    /// <summary>An army loses when it has no finished structure, engineer or commander left.</summary>
    Supremacy,
    /// <summary>An army loses when it has no finished unit left.</summary>
    Annihilation,
    /// <summary>Nobody is ever defeated and nobody wins.</summary>
    Sandbox,
}

/// <summary>Starting stored resources. Append-only.</summary>
public enum StartResources : byte
{
    /// <summary>Full commander storage (650 mass / 4000 energy for the UEF ACU): the classic start.</summary>
    Normal,
    /// <summary>Half of the commander storage.</summary>
    Low,
    /// <summary>Full storage plus a bank of 2x the storage that refills the storage as it is spent (3x in total).</summary>
    High,
}

/// <summary>Fog of war (SupCom lobby "Fog of War"). Append-only.</summary>
public enum FogMode : byte
{
    /// <summary>Unexplored black, explored grey, vision from units.</summary>
    Normal,
    /// <summary>The whole map starts explored (terrain/props known); enemy units are still hidden outside vision.</summary>
    Explored,
    /// <summary>No fog: everyone sees everything (<c>IntelService.Enabled = false</c>).</summary>
    None,
}

/// <summary>Game speed rule (SupCom lobby "Game Speed"). Client/runner only: never affects the sim. Append-only.</summary>
public enum GameSpeedMode : byte
{
    /// <summary>Speed keys work (single player: local; multiplayer: requests to the host). Today's behavior.</summary>
    Adjustable,
    /// <summary>Locked at 1x: speed keys disabled (pause still works).</summary>
    Normal,
    /// <summary>Locked at 2x: speed keys disabled (pause still works).</summary>
    Fast,
}

/// <summary>
/// Game options chosen in the skirmish setup screen or by the multiplayer host (lobby codec in Rts.Net
/// <c>LobbyOptions</c>), applied by <see cref="GameSetup.CreateSkirmish"/> and kept in <c>SimWorld.Options</c>
/// (saved with the game, so loads and replays keep them; a copy is in the save/replay header <c>GameInfo.Options</c>).
/// Immutable once the game is created: the snapshot hands the same object to the client (read only).
/// <para><b>The defaults are today's behavior</b> (benchmarks, tests and CLI games use them; the reference AI match
/// must not change). The setup screen and the lobby start from <see cref="SetupDefaults"/> (cheats off).</para>
/// </summary>
public sealed class GameOptions
{
    /// <summary>Unit cap choices of the setup screen / lobby (per army, structures included).</summary>
    public static readonly int[] UnitCaps = [250, 500, 750, 1000];

    public VictoryCondition Victory = VictoryCondition.Assassination;
    /// <summary>Max live units per army (structures included): factories and engineers don't start new units at it.</summary>
    public int UnitCap = 1000;
    public StartResources Resources = StartResources.Normal;
    public FogMode Fog = FogMode.Normal;
    public GameSpeedMode Speed = GameSpeedMode.Adjustable;
    /// <summary>
    /// Cheats: debug spawns are honored (<c>SimWorld.CheatsEnabled</c>) and the sandbox panel/console can be opened in a
    /// normal game (<c>EnableCheatToolsCommand</c>). Default true = today's behavior (debug spawns always worked).
    /// </summary>
    public bool Cheats = true;
    /// <summary>Each army starts with a small base (a few mex + pgens + a land factory, from faction roles).</summary>
    public bool Prebuilt;

    /// <summary>Defaults of the setup screen and the multiplayer lobby (as SupCom: cheats off).</summary>
    public static GameOptions SetupDefaults() => new() { Cheats = false };

    public GameOptions Clone() => (GameOptions)MemberwiseClone();

    /// <summary>The locked speed multiplier of <see cref="Speed"/> (null = adjustable).</summary>
    [JsonIgnore] public float? LockedSpeed => Speed switch { GameSpeedMode.Normal => 1f, GameSpeedMode.Fast => 2f, _ => null };

    public static string Name(VictoryCondition v) => v.ToString();
    public static string Name(StartResources r) => r.ToString();
    public static string Name(FogMode f) => f switch { FogMode.Explored => "Explored", FogMode.None => "None", _ => "Normal" };
    public static string Name(GameSpeedMode s) => s.ToString();

    /// <summary>One-line summary for menus and headers, e.g. "Assassination, cap 500, fog Normal".</summary>
    public string Describe()
    {
        var parts = new List<string> { Name(Victory), $"cap {UnitCap}" };
        if (Resources != StartResources.Normal) parts.Add($"resources {Name(Resources)}");
        if (Fog != FogMode.Normal) parts.Add($"fog {Name(Fog)}");
        if (Speed != GameSpeedMode.Adjustable) parts.Add($"speed {Name(Speed)}");
        if (Prebuilt) parts.Add("prebuilt");
        if (Cheats) parts.Add("cheats");
        return string.Join(", ", parts);
    }

    /// <summary>Same values (codec round-trip tests; no Equals/GetHashCode override: Rts.Sim avoids GetHashCode).</summary>
    public bool SameAs(GameOptions o) => o.Victory == Victory && o.UnitCap == UnitCap && o.Resources == Resources &&
        o.Fog == Fog && o.Speed == Speed && o.Cheats == Cheats && o.Prebuilt == Prebuilt;

    /// <summary>Max unit cap accepted by <see cref="Parse"/> (the unit store capacity of a skirmish).</summary>
    public const int MaxUnitCap = 16384;

    /// <summary>
    /// Parses a CLI spec like <c>victory=supremacy,cap=500,resources=high,fog=explored,speed=fast,cheats=on,prebuilt=on</c>
    /// (keys and values case-insensitive; booleans on/off/true/false/1/0; cap 1..16384; missing keys keep the defaults of
    /// <c>new GameOptions()</c>; empty = defaults). Throws <see cref="FormatException"/> naming the valid choices.
    /// </summary>
    public static GameOptions Parse(string? spec)
    {
        var o = new GameOptions();
        if (string.IsNullOrWhiteSpace(spec)) return o;
        foreach (var raw in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = raw.IndexOf('=');
            if (eq <= 0) throw new FormatException($"Bad game option '{raw}': expected key=value (keys: {SpecKeys})");
            string key = raw[..eq].Trim().ToLowerInvariant(), val = raw[(eq + 1)..].Trim();
            switch (key)
            {
                case "victory": o.Victory = ParseEnum<VictoryCondition>(key, val); break;
                case "cap":
                case "unitcap":
                    if (!int.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int cap) ||
                        cap < 1 || cap > MaxUnitCap)
                        throw new FormatException($"Bad unit cap '{val}': expected 1..{MaxUnitCap} (setup choices: {string.Join(", ", UnitCaps)})");
                    o.UnitCap = cap;
                    break;
                case "resources": o.Resources = ParseEnum<StartResources>(key, val); break;
                case "fog": o.Fog = ParseEnum<FogMode>(key, val); break;
                case "speed": o.Speed = ParseEnum<GameSpeedMode>(key, val); break;
                case "cheats": o.Cheats = ParseBool(key, val); break;
                case "prebuilt": o.Prebuilt = ParseBool(key, val); break;
                default: throw new FormatException($"Unknown game option '{key}' (keys: {SpecKeys})");
            }
        }
        return o;
    }

    /// <summary>The spec of these options for <see cref="Parse"/> (every key, lowercase), e.g. for logs and the CLI.</summary>
    public string ToSpec() =>
        $"victory={Victory.ToString().ToLowerInvariant()},cap={UnitCap},resources={Resources.ToString().ToLowerInvariant()}," +
        $"fog={Fog.ToString().ToLowerInvariant()},speed={Speed.ToString().ToLowerInvariant()},cheats={(Cheats ? "on" : "off")},prebuilt={(Prebuilt ? "on" : "off")}";

    private const string SpecKeys = "victory, cap, resources, fog, speed, cheats, prebuilt";

    private static T ParseEnum<T>(string key, string val) where T : struct, Enum
    {
        foreach (var v in Enum.GetValues<T>())
            if (string.Equals(v.ToString(), val, StringComparison.OrdinalIgnoreCase)) return v;
        throw new FormatException($"Bad {key} '{val}' (choices: {string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))})");
    }

    private static bool ParseBool(string key, string val) => val.ToLowerInvariant() switch
    {
        "on" or "true" or "1" or "yes" => true,
        "off" or "false" or "0" or "no" => false,
        _ => throw new FormatException($"Bad {key} '{val}' (choices: on, off, true, false, 1, 0)"),
    };
}

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rts.Client.Menus;
using Rts.Sim;
using Rts.Sim.Map;
using Rts.Sim.Persistence;
using Rts.Sim.World;

namespace Rts.Client.Persistence;

/// <summary>
/// The client's game config stored as JSON in <see cref="GameInfo.ClientData"/> of saves and replays, so a loaded
/// game (or a replay) rebuilds its session: player slots (colors, teams, AI difficulty), sandbox flags, scene, and
/// the camera at save time. Missing or unreadable data falls back to what the <see cref="GameInfo"/> header knows.
/// </summary>
public sealed class ClientGameData
{
    public int MapSize = 256;
    public MapPreset Preset = MapPreset.Plains;
    public float WaterAmount = -1;
    public int Seed = 42;
    public List<SlotData> Slots = new();
    public bool Demo;
    public int Stress;
    public string? Scene;
    public bool Sandbox;
    public bool SandboxAi;
    /// <summary>Camera at save time (x, z, distance); null = the local start position.</summary>
    public float[]? Camera;

    public sealed class SlotData
    {
        public SlotKind Kind;
        public AiDifficulty Difficulty = AiDifficulty.Normal;
        public int Team;
        public int ColorIndex;
        public string? Faction;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static ClientGameData From(GameConfig cfg, Vector3? camera = null) => new()
    {
        MapSize = cfg.MapSize, Preset = cfg.Preset, WaterAmount = cfg.WaterAmount, Seed = cfg.Seed,
        Slots = cfg.Slots.Select(s => new SlotData { Kind = s.Kind, Difficulty = s.Difficulty, Team = s.Team, ColorIndex = s.ColorIndex, Faction = s.Faction }).ToList(),
        Demo = cfg.Demo, Stress = cfg.Stress, Scene = cfg.Scene, Sandbox = cfg.Sandbox, SandboxAi = cfg.SandboxAi,
        Camera = camera is Vector3 c ? [c.X, c.Y, c.Z] : null,
    };

    public GameConfig ToConfig()
    {
        var cfg = new GameConfig
        {
            MapSize = MapSize, Preset = Preset, WaterAmount = WaterAmount, Seed = Seed, Demo = Demo, Stress = Stress,
            Scene = Scene, Sandbox = Sandbox, SandboxAi = SandboxAi,
        };
        for (int i = 0; i < GameConfig.MaxSlots; i++)
        {
            var slot = cfg.Slots[i];
            if (i < Slots.Count)
            {
                var d = Slots[i];
                slot.Kind = d.Kind;
                slot.Difficulty = d.Difficulty;
                slot.Team = Math.Clamp(d.Team, 0, 4);
                slot.ColorIndex = Math.Max(0, d.ColorIndex);
                if (!string.IsNullOrEmpty(d.Faction)) slot.Faction = d.Faction;
            }
            else slot.Kind = SlotKind.Closed;
        }
        if (Camera is { Length: 3 } c) cfg.SceneCamera = new Vector3(c[0], c[1], c[2]);
        return cfg;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Null if the text is missing or not valid client data.</summary>
    public static ClientGameData? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ClientGameData>(json, Json); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }

    /// <summary>
    /// Client config of a loaded save/replay: its stored client data, else rebuilt from the header (map settings and
    /// players). The camera at save time (if stored) is in <see cref="GameConfig.SceneCamera"/>.
    /// </summary>
    public static GameConfig ConfigFor(GameInfo info)
    {
        var cfg = FromJson(info.ClientData)?.ToConfig() ?? FromHeader(info);
        cfg.Sandbox = info.Sandbox;
        // Game options live in the sim (saved); the header copy keeps them for restarts. Null = pre-6b file = defaults.
        cfg.Options = info.Options?.Clone() ?? new GameOptions();
        return cfg;
    }

    private static GameConfig FromHeader(GameInfo info)
    {
        var cfg = new GameConfig { Scene = info.Scene, Sandbox = info.Sandbox, MapFile = info.MapFile };
        if (info.Map != null)
        {
            cfg.MapSize = info.Map.Size;
            cfg.Seed = info.Map.Seed;
            cfg.Preset = info.Map.Preset;
            cfg.WaterAmount = info.Map.WaterAmount;
        }
        for (int i = 0; i < GameConfig.MaxSlots; i++)
        {
            var slot = cfg.Slots[i];
            if (i >= info.Players.Count) { slot.Kind = SlotKind.Closed; continue; }
            var p = info.Players[i];
            slot.Kind = i == info.LocalArmy ? SlotKind.Human : SlotKind.AI;
            slot.Difficulty = p.Difficulty;
            slot.Team = p.Team is >= 1 and <= 4 ? p.Team : 0;
            int ci = Array.IndexOf(GameSetup.ArmyColors, p.Color);
            slot.ColorIndex = ci >= 0 ? ci : i;
            if (!string.IsNullOrEmpty(p.Faction)) slot.Faction = p.Faction;
        }
        return cfg;
    }
}

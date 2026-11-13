using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Sim.Persistence;

public sealed class PlayerInfo
{
    public string Name = "";
    public int Team;
    public uint Color;
    public bool IsAI;
    public AiDifficulty Difficulty;
    /// <summary>Faction id of the army (v1 files from before factions have "": migrated to the default faction).</summary>
    public string Faction = "";
}

/// <summary>
/// Metadata stored (as JSON) at the start of save and replay files: readable without decoding the state, so menus
/// can list files quickly. Also carries the compatibility data checked on load.
/// </summary>
public sealed class GameInfo
{
    /// <summary>Map generator settings (the terrain is regenerated on load). Null = the terrain is embedded in the file.</summary>
    public MapGenSettings? Map;
    public List<PlayerInfo> Players = new();
    /// <summary>The army of the local human player (-1 = observer).</summary>
    public int LocalArmy;
    public bool Sandbox;
    /// <summary>Client scene name (--scene), informational.</summary>
    public string? Scene;
    /// <summary>Opaque client data (e.g. the client's game config as JSON) restored with a loaded game.</summary>
    public string? ClientData;
    /// <summary>(6b) Game options of the game (null in files from before 6b = defaults, <c>new GameOptions()</c>).</summary>
    public GameOptions? Options;
    /// <summary>(8b-2) Map file id (<c>data/maps/&lt;id&gt;.rtsmap</c>) the game is played on, null = generated or
    /// embedded terrain. Loading needs the same file: <see cref="MapFileHash"/> must match its content hash.</summary>
    public string? MapFile;
    public ulong MapFileHash;
    /// <summary>(8b-2) Display name of the map file (informational).</summary>
    public string? MapName;
    /// <summary>(8b-3) Visual biome id (<see cref="Biomes.Id"/>; null = temperate). Never affects the simulation.</summary>
    public string? Biome;
    /// <summary>(10a-3) Set in multiplayer saves only (lobby, save id, shared state hash); not written when null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MultiplayerSaveInfo? Multiplayer;

    /// <summary>Save slot name / replay title.</summary>
    public string Name = "";
    public DateTime CreatedUtc = DateTime.UtcNow;
    /// <summary>Saves: the saved tick. Replays: the last recorded tick (game length = Ticks / 10 s).</summary>
    public int Ticks;
    /// <summary>Winning team at the end of a replay (-1 = none/unfinished).</summary>
    public int WinningTeam = -1;

    // Compatibility data (filled by the writer).
    public string GameVersion = "";
    /// <summary>Module version id of Rts.Sim: identical simulation code (replays only re-simulate exactly with it).</summary>
    public string SimBuild = "";
    public ulong BlueprintHash;
    public ulong TerrainHash;
    /// <summary>(10a-1) <see cref="Core.DMath.Version"/> of the build that wrote the file. Files from before the
    /// deterministic math have no field = 0 (the old <c>MathF</c> sim): their replays are refused
    /// (<see cref="SaveGame.CheckReplayMath"/>) and saves on generated maps fail with a clear message (the generator
    /// uses the math too). No initializer on purpose: a missing JSON field must stay 0.</summary>
    public int MathVersion;
    /// <summary>Container version of the file this header was read from (set by the reader; older = migrated on
    /// load, see <see cref="SaveMigrations"/>). Not written.</summary>
    [JsonIgnore] public int FileVersion = SaveContainer.Version;

    public float GameSeconds => Ticks / (float)SimWorld.TicksPerSecond;

    public static string CurrentSimBuild => typeof(SimWorld).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..12];
    public static string CurrentGameVersion =>
        typeof(SimWorld).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "dev";

    /// <summary>Header for a skirmish created by <see cref="GameSetup.CreateSkirmish"/> with these settings.</summary>
    public static GameInfo ForSkirmish(SkirmishSettings s, Simulation sim, int localArmy, bool sandbox)
    {
        var info = new GameInfo { Map = s.MapFile == null ? GameSetup.MapSettingsFor(s) : null, LocalArmy = localArmy, Sandbox = sandbox };
        if (s.MapFile != null) { info.MapFile = s.MapFile.Info.Id; info.MapFileHash = s.MapFile.Info.Hash; info.MapName = s.MapFile.Info.Name; }
        info.Biome = Biomes.Id(GameSetup.BiomeFor(s)); // (8b-3)
        info.FillFrom(sim);
        return info;
    }

    /// <summary>Players, compatibility hashes and tick from a world (sequential/sim thread only).</summary>
    public void FillFrom(Simulation sim)
    {
        var w = sim.World;
        Players = w.Armies.Select(a => new PlayerInfo { Name = a.Name, Team = a.Team, Color = a.Color, IsAI = a.IsAI, Difficulty = a.AiDifficulty, Faction = a.Faction }).ToList();
        Ticks = w.Tick;
        WinningTeam = w.WinningTeam;
        Options = w.Options.Clone();
        GameVersion = CurrentGameVersion;
        SimBuild = CurrentSimBuild;
        BlueprintHash = BlueprintHashOf(w.Blueprints);
        TerrainHash = TerrainHashOf(w.Terrain);
        MathVersion = Core.DMath.Version; // (10a-1)
    }

    public GameInfo Clone() => FromJson(ToJson());

    public string MapDescription => Map != null ? $"{MapPresets.Name(Map.Preset)} {Map.Size} (seed {Map.Seed})"
        : MapFile != null ? $"{MapName ?? MapFile} (map file)" : "custom map";
    public string PlayersDescription => string.Join(" vs ", Players.Select(p => p.IsAI ? $"AI {p.Difficulty}" : p.Name));

    private static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static GameInfo FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<GameInfo>(json, Json) ?? throw new SaveFormatException("Damaged file (empty header)"); }
        catch (JsonException e) { throw new SaveFormatException("Damaged file (bad header)", e); }
    }

    [NoSave] private static readonly ConditionalWeakTable<BlueprintDb, StrongBox<ulong>> BpHashes = new();

    /// <summary>Hash of every blueprint's data (any change in units/stats/indices changes it).</summary>
    public static ulong BlueprintHashOf(BlueprintDb db) => BpHashes.GetValue(db, static db =>
    {
        var o = new ByteWriter(1 << 16);
        var g = new GraphWriter(o);
        g.WriteRef(db.All.ToList());
        g.Flush();
        return new StrongBox<ulong>(Hash64.Of(o.Span));
    }).Value;

    public static ulong TerrainHashOf(Terrain t)
    {
        ulong h = Hash64.Of(System.Runtime.InteropServices.MemoryMarshal.AsBytes(t.Heights.AsSpan()));
        h = Hash64.Of(System.Runtime.InteropServices.MemoryMarshal.AsBytes(t.Pass.AsSpan()), h);
        Span<float> dep = stackalloc float[3];
        foreach (var d in t.Deposits) // field by field: the struct has padding
        {
            dep[0] = d.Pos.X; dep[1] = d.Pos.Y; dep[2] = (float)d.Type;
            h = Hash64.Of(System.Runtime.InteropServices.MemoryMarshal.AsBytes(dep), h);
        }
        h = Hash64.Of(System.Runtime.InteropServices.MemoryMarshal.AsBytes(t.StartPositions.AsSpan()), h);
        return Hash64.Of(BitConverter.GetBytes(t.WaterLevel), h ^ (ulong)t.Width * 31 ^ (ulong)t.Height);
    }
}

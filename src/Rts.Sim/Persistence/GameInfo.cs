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
        var info = new GameInfo { Map = GameSetup.MapSettingsFor(s), LocalArmy = localArmy, Sandbox = sandbox };
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
        GameVersion = CurrentGameVersion;
        SimBuild = CurrentSimBuild;
        BlueprintHash = BlueprintHashOf(w.Blueprints);
        TerrainHash = TerrainHashOf(w.Terrain);
    }

    public GameInfo Clone() => FromJson(ToJson());

    public string MapDescription => Map == null ? "custom map" : $"{MapPresets.Name(Map.Preset)} {Map.Size} (seed {Map.Seed})";
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

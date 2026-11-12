using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Client.Menus;

public enum SlotKind { Human, AI, Closed }

/// <summary>One player slot of the skirmish setup screen.</summary>
public sealed class PlayerSlot
{
    public SlotKind Kind;
    public AiDifficulty Difficulty = AiDifficulty.Normal;
    /// <summary>0 = no team (free for all), 1..4 = team number.</summary>
    public int Team;
    /// <summary>Index into <see cref="GameSetup.ArmyColors"/>.</summary>
    public int ColorIndex;
    /// <summary>Faction id (data/factions) or <see cref="FactionDb.Random"/> (resolved from the seed at game start).</summary>
    public string Faction = FactionDb.DefaultFaction;

    public PlayerSlot Clone() => (PlayerSlot)MemberwiseClone();
}

/// <summary>
/// Everything needed to start (and restart) a skirmish: map settings, the 8 player slots and debug options.
/// Open slots become armies in slot order (army index = start position index).
/// </summary>
public sealed class GameConfig
{
    public const int MaxSlots = 8;
    public static readonly int[] MapSizes = [256, 512, 1024];

    public int MapSize = 256;
    public MapPreset Preset = MapPreset.Plains;
    /// <summary>0..1, negative = preset default.</summary>
    public float WaterAmount = -1;
    public int Seed = 42;
    /// <summary>(8b-2) Map file id (data/maps) to play on; null = "Random map" (generator settings above).</summary>
    public string? MapFile;
    public readonly List<PlayerSlot> Slots = new();

    // Debug / CLI options carried over restarts.
    public bool Demo;
    public int Stress;
    /// <summary>Scripted scene (--scene naval, see NavalScene.cs) and the camera it picked (x, z, distance).</summary>
    public string? Scene;
    public System.Numerics.Vector3? SceneCamera;

    /// <summary>Sandbox mode: cheats/spawn panel and console, no end of game (see Rts.Client/Sandbox).</summary>
    public bool Sandbox;
    /// <summary>Sandbox only: the AI slots start with their AI switched on (off by default: passive enemy armies).</summary>
    public bool SandboxAi;
    /// <summary>Game options (victory, cap, resources, fog, speed, cheats, prebuilt). Setup screen default = cheats off;
    /// CLI games without <c>--options</c> use <c>new GameOptions()</c> (cheats on = the debug keys keep working).</summary>
    public GameOptions Options = GameOptions.SetupDefaults();

    public GameConfig()
    {
        for (int i = 0; i < MaxSlots; i++)
            Slots.Add(new PlayerSlot { Kind = i == 0 ? SlotKind.Human : i == 1 ? SlotKind.AI : SlotKind.Closed, ColorIndex = i });
    }

    public GameConfig Clone()
    {
        var copy = new GameConfig { MapSize = MapSize, Preset = Preset, WaterAmount = WaterAmount, Seed = Seed, Demo = Demo, Stress = Stress, Sandbox = Sandbox, SandboxAi = SandboxAi, Scene = Scene, Options = Options.Clone(), MapFile = MapFile };
        for (int i = 0; i < MaxSlots; i++) copy.Slots[i] = Slots[i].Clone();
        return copy;
    }

    public IEnumerable<PlayerSlot> OpenSlots => Slots.Where(s => s.Kind != SlotKind.Closed);
    public int Players => OpenSlots.Count();

    /// <summary>Why the game can't start (null if it can). With <paramref name="factions"/>: also unknown slot factions.</summary>
    public string? Problem(FactionDb? factions = null)
    {
        if (Players < 2) return "At least two players are needed";
        int humans = OpenSlots.Count(s => s.Kind == SlotKind.Human);
        if (humans != 1) return "Exactly one human player is needed";
        var open = OpenSlots.ToList();
        bool allSameTeam = open.All(s => s.Team > 0 && s.Team == open[0].Team);
        if (allSameTeam) return "Everybody is on the same team";
        if (MapFile != null && MapFileCache.Info(MapFile) is { } map && Players > map.Players) return $"{map.Name} has room for {map.Players} players";
        if (MapFile != null && MapFileCache.Info(MapFile) == null) return $"Map file '{MapFile}' not found";
        return factions == null ? null : FactionProblem(factions);
    }

    /// <summary>Faction choices of a slot: every faction of data/factions (file order), then "random" (picked from the seed).</summary>
    public static IReadOnlyList<string> FactionChoices(FactionDb factions) => [.. factions.All.Select(f => f.Id), FactionDb.Random];

    /// <summary>The faction choice <paramref name="dir"/> steps after <paramref name="current"/> (wrapping; unknown → the first).</summary>
    public static string NextFaction(FactionDb factions, string current, int dir)
    {
        var choices = FactionChoices(factions);
        int i = -1;
        for (int k = 0; k < choices.Count; k++)
            if (string.Equals(choices[k], current, StringComparison.OrdinalIgnoreCase)) i = k;
        int n = choices.Count;
        return choices[i < 0 ? 0 : ((i + dir) % n + n) % n];
    }

    /// <summary>The first open slot whose faction is neither "random" nor in data/factions (null = all fine).</summary>
    public string? FactionProblem(FactionDb factions)
    {
        foreach (var s in OpenSlots)
            if (!string.Equals(s.Faction, FactionDb.Random, StringComparison.OrdinalIgnoreCase) && factions.Get(s.Faction) == null)
                return $"Unknown faction '{s.Faction}' (known: {string.Join(", ", factions.All.Select(f => f.Id))}, random)";
        return null;
    }

    public float EffectiveWater => WaterAmount < 0 ? MapPresets.DefaultWater(Preset) : WaterAmount;

    public MapGenSettings MapSettings() => new()
    {
        Size = MapSize, Players = Math.Max(2, Players), Seed = Seed, Preset = Preset, WaterAmount = WaterAmount,
    };

    /// <summary>Skirmish settings for the sim plus the local (human) army index.</summary>
    public SkirmishSettings ToSkirmish(out int localArmy)
    {
        var open = OpenSlots.ToList();
        int n = open.Count;
        localArmy = Math.Max(0, open.FindIndex(s => s.Kind == SlotKind.Human));
        var names = new string[n];
        int aiNo = 0;
        for (int a = 0; a < n; a++)
            names[a] = open[a].Kind == SlotKind.Human ? "Player" : $"AI {++aiNo} ({open[a].Difficulty})";
        return new SkirmishSettings
        {
            MapSize = MapSize, Seed = Seed, Players = n, MapPreset = Preset, WaterAmount = WaterAmount,
            AiPlayers = open.Select(s => s.Kind == SlotKind.AI).ToArray(),
            AiDifficulties = open.Select(s => s.Difficulty).ToArray(),
            Factions = open.Select(s => s.Faction).ToArray(),
            // Free-for-all slots get a unique team id; numbered teams share theirs.
            Teams = open.Select((s, a) => s.Team > 0 ? s.Team : 100 + a).ToArray(),
            Colors = open.Select(s => GameSetup.ArmyColors[s.ColorIndex % GameSetup.ArmyColors.Length]).ToArray(),
            Names = names,
            Options = Options.Clone(),
            MapFile = MapFile != null ? MapFileCache.Get(MapFile) : null,
        };
    }

    /// <summary>Config of a game started directly from the command line (army 0 human, the others AI, FFA).</summary>
    public static GameConfig FromOptions(ClientOptions o)
    {
        var c = new GameConfig { MapSize = o.MapSize, Seed = o.Seed, Preset = o.Preset, WaterAmount = o.WaterAmount, Demo = o.Demo, Stress = o.Stress, Sandbox = o.Sandbox, Scene = o.Scene, MapFile = o.MapFile };
        c.Options = o.GameOptions != null ? GameOptions.Parse(o.GameOptions) : new GameOptions();
        int players = Math.Clamp(o.Players, 2, MaxSlots);
        for (int i = 0; i < MaxSlots; i++)
            c.Slots[i].Kind = i == 0 ? SlotKind.Human : i < players ? SlotKind.AI : SlotKind.Closed;
        c.ApplyFactionOptions(o);
        if (o.Teams != null) // (6b) --teams 1,1,2,2
            for (int i = 0; i < Math.Min(o.Teams.Length, MaxSlots); i++) c.Slots[i].Team = Math.Clamp(o.Teams[i], 0, 4);
        return c;
    }

    /// <summary>
    /// CLI factions: <c>--factions a,b,...</c> sets slot i to the i-th id (in slot order, "random" allowed),
    /// <c>--faction X</c> then sets the human slot. Unknown ids are reported when the game starts.
    /// </summary>
    public void ApplyFactionOptions(ClientOptions o)
    {
        if (o.Factions != null)
            for (int i = 0; i < Math.Min(o.Factions.Length, MaxSlots); i++)
                if (!string.IsNullOrWhiteSpace(o.Factions[i])) Slots[i].Faction = o.Factions[i].Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(o.Faction))
        {
            int human = Slots.FindIndex(s => s.Kind == SlotKind.Human);
            Slots[human >= 0 ? human : 0].Faction = o.Faction.Trim().ToLowerInvariant();
        }
    }
}

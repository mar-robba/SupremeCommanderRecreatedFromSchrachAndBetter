using Rts.Sim;
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
    public readonly List<PlayerSlot> Slots = new();

    // Debug / CLI options carried over restarts.
    public bool Demo;
    public int Stress;
    /// <summary>Scripted scene (--scene naval, see NavalScene.cs) and the camera it picked (x, z, distance).</summary>
    public string? Scene;
    public System.Numerics.Vector3? SceneCamera;

    public GameConfig()
    {
        for (int i = 0; i < MaxSlots; i++)
            Slots.Add(new PlayerSlot { Kind = i == 0 ? SlotKind.Human : i == 1 ? SlotKind.AI : SlotKind.Closed, ColorIndex = i });
    }

    public GameConfig Clone()
    {
        var copy = new GameConfig { MapSize = MapSize, Preset = Preset, WaterAmount = WaterAmount, Seed = Seed, Demo = Demo, Stress = Stress, Scene = Scene };
        for (int i = 0; i < MaxSlots; i++) copy.Slots[i] = Slots[i].Clone();
        return copy;
    }

    public IEnumerable<PlayerSlot> OpenSlots => Slots.Where(s => s.Kind != SlotKind.Closed);
    public int Players => OpenSlots.Count();

    /// <summary>Why the game can't start (null if it can).</summary>
    public string? Problem()
    {
        if (Players < 2) return "At least two players are needed";
        int humans = OpenSlots.Count(s => s.Kind == SlotKind.Human);
        if (humans != 1) return "Exactly one human player is needed";
        var open = OpenSlots.ToList();
        bool allSameTeam = open.All(s => s.Team > 0 && s.Team == open[0].Team);
        if (allSameTeam) return "Everybody is on the same team";
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
            // Free-for-all slots get a unique team id; numbered teams share theirs.
            Teams = open.Select((s, a) => s.Team > 0 ? s.Team : 100 + a).ToArray(),
            Colors = open.Select(s => GameSetup.ArmyColors[s.ColorIndex % GameSetup.ArmyColors.Length]).ToArray(),
            Names = names,
        };
    }

    /// <summary>Config of a game started directly from the command line (army 0 human, the others AI, FFA).</summary>
    public static GameConfig FromOptions(ClientOptions o)
    {
        var c = new GameConfig { MapSize = o.MapSize, Seed = o.Seed, Preset = o.Preset, WaterAmount = o.WaterAmount, Demo = o.Demo, Stress = o.Stress, Scene = o.Scene };
        int players = Math.Clamp(o.Players, 2, MaxSlots);
        for (int i = 0; i < MaxSlots; i++)
            c.Slots[i].Kind = i == 0 ? SlotKind.Human : i < players ? SlotKind.AI : SlotKind.Closed;
        return c;
    }
}

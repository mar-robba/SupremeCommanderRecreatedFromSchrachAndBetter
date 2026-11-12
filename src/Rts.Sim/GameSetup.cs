using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Sim;

public sealed class SkirmishSettings
{
    public int MapSize = 256;
    public int Seed = 42;
    public int Players = 2;
    /// <summary>Map generator preset and water amount (0..1, negative = preset default).</summary>
    public MapPreset MapPreset = MapPreset.Plains;
    public float WaterAmount = -1;
    /// <summary>Which armies are AI-controlled (army 0 is the local human by default).</summary>
    public bool[] AiPlayers = [false, true];
    /// <summary>Skirmish AI difficulty per army (default Normal; only used for AI armies).</summary>
    public AiDifficulty[]? AiDifficulties;
    /// <summary>Team per army; default = every army on its own team (free for all).</summary>
    public int[]? Teams;
    /// <summary>Optional per-army colors (0xRRGGBBAA) and names; defaults: <see cref="GameSetup.ArmyColors"/>, "Player"/"AI n".</summary>
    public uint[]? Colors;
    public string[]? Names;
    /// <summary>
    /// Faction id per army (data/factions; <see cref="FactionDb.Random"/> = picked from the seed; missing/null/"" =
    /// <see cref="FactionDb.DefaultFaction"/>). The army's starting commander is its faction's Commander.
    /// </summary>
    public string[]? Factions;
    public int UnitCapacity = 16384;
    /// <summary>Starting stored resources; negative = full storage (the commander's StorageMass/StorageEnergy).</summary>
    public float StartMass = -1;
    public float StartEnergy = -1;
    /// <summary>Game options (victory, unit cap, start resources, fog, cheats, prebuilt base); defaults = classic game.
    /// <see cref="StartMass"/>/<see cref="StartEnergy"/> (when >= 0) override <see cref="GameOptions.Resources"/>.</summary>
    public GameOptions Options = new();
    /// <summary>(8b-2) Play on this map file instead of a generated map (null = generator with MapSize/Seed/MapPreset/
    /// WaterAmount). Players must not exceed its start positions; <see cref="MapFile.TerrainFor"/> picks spread starts.</summary>
    public MapFile? MapFile;
}

public static class GameSetup
{
    /// <summary>Army colors as 0xRRGGBBAA: blue, red, green, yellow, orange, purple, cyan, white.</summary>
    public static readonly uint[] ArmyColors =
    [
        0x2F6FE0FF, 0xE03A2FFF, 0x3CB043FF, 0xE8C31AFF, 0xF07F17FF, 0x9B4FD8FF, 0x2FC9D8FF, 0xE6E6E6FF,
    ];

    /// <summary>The map generator settings of a skirmish (saves/replays store them to regenerate the terrain).</summary>
    public static MapGenSettings MapSettingsFor(SkirmishSettings s) => new()
    {
        Size = s.MapSize, Players = s.Players, Seed = s.Seed, Preset = s.MapPreset, WaterAmount = s.WaterAmount,
    };

    /// <summary>
    /// The concrete faction of every army of a skirmish ("random" resolved deterministically from the seed). Throws
    /// <see cref="InvalidOperationException"/> for an unknown faction id.
    /// </summary>
    public static string[] ResolveFactions(BlueprintDb db, SkirmishSettings s)
    {
        var result = new string[s.Players];
        for (int a = 0; a < s.Players; a++)
        {
            string? choice = s.Factions != null && a < s.Factions.Length ? s.Factions[a] : null;
            result[a] = db.Factions.Resolve(choice, s.Seed, a);
            if (db.Factions.Get(result[a]) == null)
                throw new InvalidOperationException($"Unknown faction '{result[a]}' (known: {string.Join(", ", db.Factions.All.Select(f => f.Id))})");
        }
        return result;
    }

    /// <summary>The terrain of a skirmish: the map file's (for its player count) or a generated one.</summary>
    public static Terrain TerrainFor(SkirmishSettings s)
    {
        if (s.MapFile == null) return MapGenerator.Generate(MapSettingsFor(s));
        if (s.Players > s.MapFile.Info.Players)
            throw new InvalidOperationException($"The map '{s.MapFile.Info.Name}' has {s.MapFile.Info.Players} start positions, not {s.Players}");
        return s.MapFile.TerrainFor(s.Players);
    }

    public static Simulation CreateSkirmish(BlueprintDb db, SkirmishSettings s)
    {
        var terrain = TerrainFor(s);
        var factions = ResolveFactions(db, s);
        var armies = new Army[s.Players];
        for (int a = 0; a < s.Players; a++)
        {
            armies[a] = new Army
            {
                Index = a,
                Name = s.Names != null && a < s.Names.Length ? s.Names[a] : a == 0 ? "Player" : $"AI {a}",
                Team = s.Teams != null && a < s.Teams.Length ? s.Teams[a] : a,
                Color = s.Colors != null && a < s.Colors.Length ? s.Colors[a] : ArmyColors[a % ArmyColors.Length],
                IsAI = a < s.AiPlayers.Length && s.AiPlayers[a],
                AiDifficulty = s.AiDifficulties != null && a < s.AiDifficulties.Length ? s.AiDifficulties[a] : AiDifficulty.Normal,
                Faction = factions[a],
            };
        }

        var world = new SimWorld(db, terrain, armies, (ulong)s.Seed, s.UnitCapacity);
        MapProps.SpawnInto(world); // generator trees and rocks (before any unit: their slots/ids come first)
        var o = world.Options = s.Options.Clone();
        world.CheatsEnabled = o.Cheats;
        // SupCom: storage comes from units (the commander at start); games begin with full storage.
        // Options: Low = half storage, High = full + a bank of 2x the storage (EconomyShare releases it).
        float startShare = o.Resources == StartResources.Low ? 0.5f : 1f;
        float bank = o.Resources == StartResources.High ? 2f : 0f;
        var commanders = new int[s.Players];
        foreach (var army in armies)
        {
            army.UnitCap = o.UnitCap;
            int cmd = commanders[army.Index] = db.Factions.CommanderOf(army.Faction);
            var cmdEco = db[cmd].Economy;
            army.Eco.MassMax = cmdEco.StorageMass;
            army.Eco.EnergyMax = cmdEco.StorageEnergy;
            army.Eco.Mass = s.StartMass < 0 ? cmdEco.StorageMass * startShare : Math.Min(s.StartMass, cmdEco.StorageMass);
            army.Eco.Energy = s.StartEnergy < 0 ? cmdEco.StorageEnergy * startShare : Math.Min(s.StartEnergy, cmdEco.StorageEnergy);
            if (s.StartMass < 0) army.StartBankMass = cmdEco.StorageMass * bank;
            if (s.StartEnergy < 0) army.StartBankEnergy = cmdEco.StorageEnergy * bank;
        }
        if (o.Fog == FogMode.None) world.Intel.Enabled = false;
        else if (o.Fog == FogMode.Explored) // the whole map starts explored; IntelSystem keeps explored cells
            foreach (var team in world.Intel.Teams) Array.Fill(team.Fog, Systems.IntelService.FogExplored);
        var center = new Vector2(terrain.Width * 0.5f, terrain.Height * 0.5f);
        for (int a = 0; a < s.Players; a++)
        {
            var sp = terrain.StartPositions[a % terrain.StartPositions.Length];
            var h = world.SpawnUnit(commanders[a], a, sp, DMath.HeadingOf(center - sp), complete: true);
            armies[a].Commander = h;
        }
        if (o.Prebuilt) PrebuiltBase.Place(world);
        // Nav clearance grids now (loading) instead of in the first tick (~50 ms on 1024): a pure function of terrain
        // + occupancy, so building them earlier changes nothing (later structures update them incrementally).
        world.Paths.EnsureGrids();
        return new Simulation(world);
    }
}

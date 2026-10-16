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

    public static Simulation CreateSkirmish(BlueprintDb db, SkirmishSettings s)
    {
        var terrain = MapGenerator.Generate(MapSettingsFor(s));
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
        // SupCom: storage comes from units (the commander at start); games begin with full storage.
        var commanders = new int[s.Players];
        foreach (var army in armies)
        {
            int cmd = commanders[army.Index] = db.Factions.CommanderOf(army.Faction);
            var cmdEco = db[cmd].Economy;
            army.Eco.MassMax = cmdEco.StorageMass;
            army.Eco.EnergyMax = cmdEco.StorageEnergy;
            army.Eco.Mass = s.StartMass < 0 ? cmdEco.StorageMass : Math.Min(s.StartMass, cmdEco.StorageMass);
            army.Eco.Energy = s.StartEnergy < 0 ? cmdEco.StorageEnergy : Math.Min(s.StartEnergy, cmdEco.StorageEnergy);
        }
        var center = new Vector2(terrain.Width * 0.5f, terrain.Height * 0.5f);
        for (int a = 0; a < s.Players; a++)
        {
            var sp = terrain.StartPositions[a % terrain.StartPositions.Length];
            var h = world.SpawnUnit(commanders[a], a, sp, DMath.HeadingOf(center - sp), complete: true);
            armies[a].Commander = h;
        }
        return new Simulation(world);
    }
}

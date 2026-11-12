using Rts.Net.Lobby;
using Rts.Sim;
using Rts.Sim.Data;

namespace Rts.Net.Session;

/// <summary>
/// Builds the identical starting simulation on every peer from the <see cref="GameStart"/> (army slots in slot order
/// are armies 0..n-1). Pure sim work: safe on a background thread.
/// </summary>
public static class NetGameSetup
{
    /// <param name="map">(8b-3) The lobby's map file (<see cref="LoadMap"/>), null = generated map.</param>
    public static SkirmishSettings ToSkirmish(GameStart start, Rts.Sim.Map.MapFile? map = null)
    {
        var lobby = start.Lobby;
        int n = start.ArmySlots.Length;
        var slots = start.ArmySlots.Select(i => lobby.Slots[i]).ToArray();
        int aiNo = 0;
        var o = lobby.Options;
        return new SkirmishSettings
        {
            MapSize = o.MapSize, Seed = o.Seed, Players = n, MapPreset = o.Preset, WaterAmount = o.Water,
            AiPlayers = slots.Select(s => s.Kind == NetSlotKind.AI).ToArray(),
            AiDifficulties = slots.Select(s => s.Difficulty).ToArray(),
            // Free-for-all slots get a unique team id; numbered teams share theirs (as in the skirmish setup).
            Teams = slots.Select((s, a) => s.Team > 0 ? s.Team : 100 + a).ToArray(),
            Colors = slots.Select(s => GameSetup.ArmyColors[s.Color % GameSetup.ArmyColors.Length]).ToArray(),
            Names = slots.Select(s => s.Kind == NetSlotKind.AI ? $"AI {++aiNo} ({s.Difficulty})" : s.Name).ToArray(),
            // "" = the default faction; "random" is resolved from the seed (identical on every peer).
            Factions = slots.Select(s => string.IsNullOrWhiteSpace(s.Faction) ? null : s.Faction).ToArray()!,
            Options = o.Game.Clone(),
            MapFile = map,
            Biome = o.BiomeChoice, // (8b-3) visual only; null = the map file's
        };
    }

    /// <summary>(8b-3) The lobby's map file (id + content hash) from data/maps or <paramref name="mapDir"/> (null = the
    /// default download folder); null for a generated map. <see cref="Rts.Sim.Map.MapFileException"/> when missing.</summary>
    public static Rts.Sim.Map.MapFile? LoadMap(GameStart start, string? mapDir = null)
    {
        var o = start.Lobby.Options;
        if (!o.HasMapFile) return null;
        var path = Rts.Sim.Map.MapLibrary.Find(o.MapFile, o.MapHash, mapDir)
            ?? throw new Rts.Sim.Map.MapFileException($"The map file '{o.MapName}' ({o.MapFile}, {o.MapHash:X16}) is not installed");
        var f = Rts.Sim.Map.MapFile.Load(path);
        if (f.Info.Hash != o.MapHash) throw new Rts.Sim.Map.MapFileException($"The map file '{o.MapFile}' differs from the host's");
        f.Info.Id = o.MapFile; // a renamed/downloaded copy is still recorded under the lobby's id
        return f;
    }

    public static Simulation CreateSimulation(BlueprintDb db, GameStart start, string? mapDir = null) =>
        GameSetup.CreateSkirmish(db, ToSkirmish(start, LoadMap(start, mapDir)));

    /// <summary>(8b-3) Same, with an already loaded map file.</summary>
    public static Simulation CreateSimulation(BlueprintDb db, GameStart start, Rts.Sim.Map.MapFile? map) =>
        GameSetup.CreateSkirmish(db, ToSkirmish(start, map));
}

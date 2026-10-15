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
    public static SkirmishSettings ToSkirmish(GameStart start)
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
        };
    }

    public static Simulation CreateSimulation(BlueprintDb db, GameStart start) => GameSetup.CreateSkirmish(db, ToSkirmish(start));
}

using Rts.Client.Persistence;
using Rts.Net.Lobby;
using Rts.Net.Session;
using Rts.Sim;
using Rts.Sim.Persistence;

namespace Rts.Client.Menus;

/// <summary>
/// Client config + replay recording of a multiplayer game, derived from the host's <see cref="GameStart"/>.
/// The lobby slots map 1:1 onto the <see cref="GameConfig"/> slots (army slots in slot order = armies 0..n-1, the
/// same order <see cref="NetGameSetup"/> uses), so colors/teams/start positions match the simulation. The local
/// player's slot is the config's Human slot; the other army slots (remote humans and AIs) are stored as AI slots
/// in the replay's client data (names come from the simulation's armies, see <see cref="NetGameSetup.ToSkirmish"/>).
/// </summary>
public static class NetGameConfig
{
    public static GameConfig From(GameStart start, int localPlayerId) => From(start.Lobby, localPlayerId);

    /// <summary>Config of a lobby (also used for the lobby's map preview).</summary>
    public static GameConfig From(LobbyState lobby, int localPlayerId)
    {
        var o = lobby.Options;
        var cfg = new GameConfig { MapSize = o.MapSize, Preset = o.Preset, WaterAmount = o.Water, Seed = o.Seed, Options = o.Game.Clone() };
        cfg.MapFile = o.HasMapFile ? o.MapFile : null; // (8b-3)
        cfg.Biome = o.EffectiveBiome;
        for (int i = 0; i < GameConfig.MaxSlots; i++)
        {
            var c = cfg.Slots[i];
            var s = i < lobby.Slots.Count ? lobby.Slots[i] : null;
            if (s == null || !s.IsArmy)
            {
                c.Kind = SlotKind.Closed;
                continue;
            }
            c.Kind = s.Kind == NetSlotKind.Human && s.PlayerId == localPlayerId ? SlotKind.Human : SlotKind.AI;
            c.Difficulty = s.Difficulty;
            c.Team = Math.Clamp(s.Team, 0, 4);
            c.ColorIndex = s.Color % Rts.Sim.GameSetup.ArmyColors.Length;
            c.Faction = string.IsNullOrWhiteSpace(s.Faction) ? Rts.Sim.Data.FactionDb.DefaultFaction : s.Faction;
        }
        return cfg;
    }

    /// <summary>Replay recording of the game (call before the runner starts: reads the world).</summary>
    public static GameRecording Recording(GameStart start, GameConfig cfg, Simulation sim, int localArmy, Rts.Sim.Map.MapFile? map = null)
    {
        var info = GameInfo.ForSkirmish(NetGameSetup.ToSkirmish(start, map), sim, localArmy, false); // (8b-3) records map id + hash
        info.Name = "Multiplayer";
        info.ClientData = ClientGameData.From(cfg).ToJson();
        return new GameRecording(info);
    }
}

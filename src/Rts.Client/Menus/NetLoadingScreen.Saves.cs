using System.Numerics;
using Rts.Client.Persistence;
using Rts.Net.Session;
using Rts.Sim;
using Rts.Sim.Persistence;

namespace Rts.Client.Menus;

/// <summary>(10a-3) Loading a multiplayer save: the world comes from this peer's copy of the save (found by save id +
/// state hash, or downloaded from the host) instead of a new game; the replay goes on from the save's.</summary>
public sealed partial class NetLoadingScreen
{
    /// <summary>Worker thread: decodes this peer's copy (checked against the lobby's save id, state hash and tick).</summary>
    private static (Simulation sim, ulong hash, Rts.Sim.Map.MapFile? map, LoadedGame? loaded) LoadSaved(AppHost app, NetSession session, NetGame game)
    {
        string path = session.SavePath ?? throw new InvalidOperationException("The save file is not here yet");
        var g = NetSession.LoadSavedGame(app.Db, game.Start, path, session.MapDir);
        return (g.Sim, g.Sim.StateHash(), null, g);
    }

    /// <summary>The recording of a loaded game: the save's header (this peer's army) and its replay continued. The
    /// camera at save time is restored only if this peer wrote the file (a downloaded copy holds the host's camera).</summary>
    private GameRecording LoadedRecording(LoadedGame g, GameConfig cfg, int army)
    {
        var info = g.Info;
        if (_s.WroteSave(info, _game.Start) && ClientGameData.FromJson(info.ClientData)?.Camera is { Length: 3 } c)
            cfg.SceneCamera = new Vector3(c[0], c[1], c[2]);
        info.Multiplayer = null;
        info.LocalArmy = army;
        info.ClientData = ClientGameData.From(cfg).ToJson();
        return new GameRecording(info, g.Replay);
    }
}

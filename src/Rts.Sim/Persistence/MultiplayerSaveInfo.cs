namespace Rts.Sim.Persistence;

/// <summary>
/// (10a-3) Header part of a multiplayer save (<see cref="GameInfo.Multiplayer"/>; null in every other file, so older
/// files and single-player saves are unchanged). Every peer of the game writes its own file at the same tick: the
/// shared identity is <see cref="SaveId"/> + <see cref="StateHash"/> (the hash of the saved world state bytes), never
/// the whole file (its header has per-peer data: the local army, the camera in <see cref="GameInfo.ClientData"/>,
/// the creation time).
/// </summary>
public sealed class MultiplayerSaveInfo
{
    /// <summary>Unique id of the save order (the host chooses it; equal on every peer).</summary>
    public string SaveId = "";
    /// <summary><see cref="Hash64"/> of the captured world state bytes (<see cref="SaveGame.StateHashOf"/>).</summary>
    public ulong StateHash;
    /// <summary>The saved tick (= <see cref="GameInfo.Ticks"/>).</summary>
    public int Tick;
    /// <summary>The game's lobby (slots with names, factions, teams, colours, AI flags, observers, options, map id + hash)
    /// encoded by Rts.Net (base64 of <c>LobbySave</c>; opaque here).</summary>
    public string Lobby = "";
    /// <summary>Lobby slot of the peer that wrote this file (-1 = an observer) and its name.</summary>
    public int SavedBySlot = -1;
    public string SavedBy = "";
    /// <summary>Written by the multiplayer autosave.</summary>
    public bool Auto;
    /// <summary>Number of human slots of the game (display: "Multiplayer · 3 humans").</summary>
    public int Humans;

    /// <summary>The shared hash of a captured state (<see cref="SaveGame.Captured.State"/>).</summary>
    public static ulong HashState(ReadOnlySpan<byte> state) => Hash64.Of(state);

    /// <summary>Decodes a save file (container + payload hash checks) and returns its header and the hash of its world
    /// state: what makes two peers' copies "the same save". Throws <see cref="SaveFormatException"/> when damaged.</summary>
    public static (GameInfo info, ulong stateHash) ReadStateHash(byte[] file)
    {
        var (info, payload) = SaveContainer.Decode(file, SaveFileKind.Save);
        var r = new ByteReader(payload);
        int n = r.Count(r.Remaining);
        return (info, HashState(r.Take(n)));
    }
}

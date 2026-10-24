using Rts.Sim.Persistence;

namespace Rts.Net.Wire;

/// <summary>Message types (first payload byte). Append-only: the values are on the wire.</summary>
public enum MsgType : byte
{
    None = 0,
    /// <summary>C→H: u32 protocol, str simBuild, str name.</summary>
    Hello = 1,
    /// <summary>H→C: varu playerId.</summary>
    Welcome = 2,
    /// <summary>H→C: str reason (join refused; the host closes the connection).</summary>
    Reject = 3,
    /// <summary>H→C: <see cref="Lobby.LobbyState"/>.</summary>
    Lobby = 4,
    /// <summary>C→H: <see cref="Lobby.LobbyRequest"/>.</summary>
    LobbyRequest = 5,
    /// <summary>C→H: str text. H→C: vari playerId (-1 = system), str name, str text.</summary>
    Chat = 6,
    /// <summary>H→C: <see cref="Lobby.GameStart"/> (everybody builds the simulation).</summary>
    Start = 7,
    /// <summary>C→H: u64 StateHash of the freshly built simulation.</summary>
    Loaded = 8,
    /// <summary>H→C: everybody loaded with the same state: the simulation may run.</summary>
    Go = 9,
    /// <summary>Both: varu turn player, varu tick, block commands (<see cref="Lockstep.TurnCodec"/>).</summary>
    Turn = 10,
    /// <summary>C→H: varu tick, u64 StateHash.</summary>
    Hash = 11,
    /// <summary>H→C: varu tick, str detail.</summary>
    Desync = 12,
    /// <summary>H→C: varu turn player, varu last tick, u8 <see cref="Rts.Sim.Orders.DropAction"/>, str reason.</summary>
    Drop = 13,
    /// <summary>Both: f32 speed, u8 paused (C→H = request, H→C = the new state).</summary>
    Speed = 14,
    /// <summary>H→C: varu input delay in ticks.</summary>
    Delay = 15,
    /// <summary>Both: i64 sender timestamp.</summary>
    Ping = 16,
    /// <summary>Both: i64 echoed timestamp.</summary>
    Pong = 17,
    /// <summary>Both: str reason (graceful leave / host closed the game).</summary>
    Leave = 18,
    /// <summary>H→C: str reason (game aborted while loading).</summary>
    Abort = 19,
    /// <summary>H→C: varu count, then per turn player varu ping ms (display only).</summary>
    Pings = 20,
}

/// <summary>Builds message frames.</summary>
public static class Msg
{
    public const uint ProtocolVersion = 2; // 2: LobbyOptions.Game (game options)

    [ThreadStatic] private static ByteWriter? _w;

    /// <summary>Encodes a message: <paramref name="body"/> writes the fields into a (thread-local, reused) writer.</summary>
    public static byte[] Build(MsgType type, Action<ByteWriter>? body = null)
    {
        var w = _w ??= new ByteWriter(256);
        w.Clear();
        body?.Invoke(w);
        return Frame.Make(type, w.Span);
    }

    public static byte[] Text(MsgType type, string text) => Build(type, w => w.Str(text));
}

using Rts.Net.Lobby;
using Rts.Sim.Orders;

namespace Rts.Net.Session;

/// <summary>What a session tells its UI (queued by network threads, drained by the UI: <see cref="NetSession.TryGetEvent"/>).</summary>
public abstract record NetEvent;

/// <summary>The lobby changed (read <see cref="NetSession.Lobby"/>).</summary>
public sealed record LobbyChangedEvent : NetEvent;

/// <summary>A chat line (also system lines: PlayerId -1).</summary>
public sealed record ChatEvent(ChatLine Line) : NetEvent;

/// <summary>The host started the game: build the simulation (<see cref="NetGameSetup"/>), then <see cref="NetGame.ReportLoaded"/>.</summary>
public sealed record GameStartingEvent(NetGame Game) : NetEvent;

/// <summary>Every peer loaded the same state: the lockstep runs.</summary>
public sealed record GameGoEvent : NetEvent;

/// <summary>The start was cancelled (a different starting state, the host left while loading...).</summary>
public sealed record StartAbortedEvent(string Reason) : NetEvent;

/// <summary>A player left the running game (timeout, quit, connection lost): its army is taken over by the AI or defeated.</summary>
public sealed record PlayerDroppedEvent(string Name, int Army, DropAction Action, string Reason) : NetEvent;

/// <summary>The state hashes differ: the game is out of sync. Write the replay (both sides do) and tell the player.</summary>
public sealed record DesyncEvent(int Tick, string Detail) : NetEvent;

/// <summary>Game speed / pause changed (by the player named, "" = host decision).</summary>
public sealed record SpeedChangedEvent(float Speed, bool Paused, string By) : NetEvent;

/// <summary>The connection to the host is gone (client), or the session ended. In a game the client continues offline
/// (the other players' armies are handed to the AI or defeated, per the host's option).</summary>
public sealed record DisconnectedEvent(string Reason) : NetEvent;

/// <summary>One chat line.</summary>
public sealed record ChatLine(int PlayerId, string Name, string Text)
{
    public bool IsSystem => PlayerId < 0;
}

/// <summary>Session phase.</summary>
public enum NetPhase { Lobby, Loading, Playing, Closed }

/// <summary>Per-player network status for the UI.</summary>
public sealed record NetPlayerStatus(int TurnPlayer, string Name, int Army, int PingMs, bool Dropped, bool Local);

/// <summary>Helper for the lobby → game slot mapping in the UI (the slot a human player sits in).</summary>
public static class LobbyExtensions
{
    public static NetSlot? SlotOfPlayer(this LobbyState l, int playerId)
    {
        int i = l.SlotOf(playerId);
        return i < 0 ? null : l.Slots[i];
    }
}

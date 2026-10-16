namespace Rts.Sim.Orders;

/// <summary>What happens to the army of a player who left a multiplayer game (lobby option of the host).</summary>
public enum DropAction : byte { AiTakeover, Defeat }

/// <summary>
/// A multiplayer player left (disconnect/timeout/quit). Never issued by a client: the lockstep layer (Rts.Net) adds
/// it to the first tick after the player's last turn on every peer, so all sims apply it at the same tick (and the
/// replay records it). <see cref="DropAction.AiTakeover"/>: the skirmish AI takes the army over;
/// <see cref="DropAction.Defeat"/>: the army is destroyed (like losing its commander).
/// </summary>
public sealed record PlayerLeftCommand(int Army, DropAction Action) : Command(Army);

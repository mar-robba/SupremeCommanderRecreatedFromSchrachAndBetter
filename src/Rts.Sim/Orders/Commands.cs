using System.Numerics;
using Rts.Sim.Core;

namespace Rts.Sim.Orders;

/// <summary>
/// Player input to the simulation. Commands are the ONLY way the client/AI changes the sim: they are queued
/// thread-safely, stamped with a tick, and applied at the start of that tick by CommandSystem (sorted by
/// army then sequence) - this is what makes replays and lockstep multiplayer possible.
/// Commands are validated in CommandSystem (ownership, blueprint legality) - never trust them.
/// </summary>
public abstract record Command(int Army)
{
    /// <summary>Sequence number assigned by the runner (ordering within a tick).</summary>
    public long Sequence { get; set; }
    /// <summary>Tick at which the command is executed (assigned by the runner).</summary>
    public int ExecuteTick { get; set; }
}

/// <summary>Give an order to units. Queue=false replaces current orders (normal click), true appends (shift).
/// Move-like orders given to several units get formation offsets computed by the sim.</summary>
public sealed record IssueOrderCommand(int Army, UnitHandle[] Units, Order Order, bool Queue) : Command(Army);

public sealed record StopCommand(int Army, UnitHandle[] Units) : Command(Army);

/// <summary>Add Count units of BlueprintIndex to the factories' build queue.</summary>
public sealed record FactoryEnqueueCommand(int Army, UnitHandle[] Factories, int BlueprintIndex, int Count) : Command(Army);

/// <summary>Remove Count entries of BlueprintIndex from the end of the factories' queues.</summary>
public sealed record FactoryDequeueCommand(int Army, UnitHandle[] Factories, int BlueprintIndex, int Count) : Command(Army);

public sealed record FactoryRepeatCommand(int Army, UnitHandle[] Factories, bool Repeat) : Command(Army);

/// <summary>Self-destruct (Ctrl+K in SupCom).</summary>
public sealed record SelfDestructCommand(int Army, UnitHandle[] Units) : Command(Army);

/// <summary>Cheat/debug: spawn completed units. Only honored when SimWorld.CheatsEnabled.</summary>
public sealed record DebugSpawnCommand(int Army, int BlueprintIndex, Vector2 Pos, int Count) : Command(Army);

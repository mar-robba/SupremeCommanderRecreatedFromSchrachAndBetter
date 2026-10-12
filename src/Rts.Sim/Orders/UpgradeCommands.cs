using Rts.Sim.Core;

namespace Rts.Sim.Orders;

/// <summary>
/// Start the in-place upgrade (blueprint <c>UpgradesTo</c>) of the given own, complete structures. Structures that
/// can't upgrade or are already upgrading are ignored. See <see cref="Systems.UpgradeSystem"/>.
/// </summary>
public sealed record UpgradeCommand(int Army, UnitHandle[] Units) : Command(Army);

/// <summary>Cancel the upgrades in progress of the given structures; what was spent is refunded (like a cancelled build).</summary>
public sealed record CancelUpgradeCommand(int Army, UnitHandle[] Units) : Command(Army);

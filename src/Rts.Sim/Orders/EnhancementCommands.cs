using Rts.Sim.Core;
using Rts.Sim.Data;

namespace Rts.Sim.Orders;

/// <summary>
/// Commander/SCU enhancement: the given own, complete, idle-or-busy enhanceable units stop what they are doing and
/// build enhancement <paramref name="Enhancement"/> (its <see cref="EnhancementBlueprint.Id"/>) on themselves.
/// Ignored for units that can't install it now (slot taken by another enhancement, missing prerequisite, already
/// enhancing). Progress, drain, assist and cancel work like an in-place upgrade (<see cref="CancelUpgradeCommand"/>
/// cancels with refund; any new order interrupts it). See <see cref="Commander.EnhancementSystem"/>.
/// </summary>
public sealed record EnhanceCommand(int Army, UnitHandle[] Units, string Enhancement) : Command(Army);

/// <summary>Removes whatever is installed in <paramref name="Slot"/> (with its prerequisites): instant, no refund
/// (SupCom). Ignored while the unit is enhancing.</summary>
public sealed record RemoveEnhancementCommand(int Army, UnitHandle[] Units, EnhancementSlot Slot) : Command(Army);

using Rts.Sim.Core;

namespace Rts.Sim.Orders;

/// <summary>
/// Team play (SupCom diplomacy): give units to an ally. Applied by <c>DiplomacySystem</c> (sequential, via
/// CommandSystem), so replays and lockstep multiplayer carry it. Only own units, only to a living ally (same team),
/// never commanders (<see cref="Systems.UnitTransfer"/> refuses them); units beyond the receiver's unit cap stay.
/// </summary>
public sealed record GiveUnitsCommand(int Army, UnitHandle[] Units, int ToArmy) : Command(Army);

/// <summary>
/// Give stored mass/energy to a living ally. The amounts are clamped to what the giver has stored and to the
/// receiver's free storage (the rest stays with the giver).
/// </summary>
public sealed record GiveResourcesCommand(int Army, int ToArmy, float Mass, float Energy) : Command(Army);

/// <summary>
/// Cheats option: turn on the sandbox tools (spawn panel F9, console F10) in a normal game. Honored only when
/// <c>SimWorld.CheatsEnabled</c> (<see cref="GameOptions.Cheats"/>); keeps the unit caps and the victory condition.
/// Any army may send it (like the sandbox commands it enables).
/// </summary>
public sealed record EnableCheatToolsCommand(int Army) : Command(Army);

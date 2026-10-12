using Rts.Sim.Core;

namespace Rts.Sim.Orders;

/// <summary>
/// SupCom pause button: Paused=true stops factories, engineers, upgrades, missile silos and mass fabricators (and
/// switches off upkeep users such as shields/radar): they stop consuming and progressing until unpaused. A paused
/// structure with an in-place upgrade in progress only pauses the upgrade (it keeps producing). Executed by
/// <see cref="Systems.PauseSystem"/>.
/// </summary>
public sealed record SetPausedCommand(int Army, UnitHandle[] Units, bool Paused) : Command(Army);

/// <summary>Submarines: Submerged=true dives, false surfaces (SupCom dive toggle). Executed by
/// <see cref="Naval.Submarines"/>; other units ignore it.</summary>
public sealed record SetSubmergedCommand(int Army, UnitHandle[] Units, bool Submerged) : Command(Army);

using Rts.Sim.Core;

namespace Rts.Sim.Orders;

/// <summary>ACU Overcharge toggle (SupCom): On=true arms Overcharge (stays armed until toggled off), false disarms it
/// (a held charge goes back to storage). Only units with an Overcharge weapon react; executed by
/// <see cref="Combat.OverchargeSystem.Apply"/>.</summary>
public sealed record SetOverchargeCommand(int Army, UnitHandle[] Units, bool On) : Command(Army);

using Rts.Sim.Core;

namespace Rts.Sim.Orders;

/// <summary>SupCom fire states (per unit, <see cref="World.UnitStore.FireStates"/>). Append new values at the END.</summary>
public enum FireState : byte
{
    /// <summary>Default: weapons acquire any valid target.</summary>
    FireAtWill,
    /// <summary>Weapons acquire targets only while the unit is under fire (hit in the last
    /// <see cref="Combat.FireStates.ReturnFireTicks"/> ticks).</summary>
    ReturnFire,
    /// <summary>Weapons never pick targets on their own; explicit Attack / attack-ground orders still fire.</summary>
    HoldFire,
}

/// <summary>Sets the fire state of the army's units (units without weapons keep it too; it is harmless).
/// Executed by <see cref="Combat.FireStates.Apply"/>.</summary>
public sealed record SetFireStateCommand(int Army, UnitHandle[] Units, FireState State) : Command(Army);

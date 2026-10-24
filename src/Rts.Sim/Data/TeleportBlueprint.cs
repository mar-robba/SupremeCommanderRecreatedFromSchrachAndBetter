namespace Rts.Sim.Data;

/// <summary>
/// SupCom ACU Personal Teleporter (<see cref="UnitBlueprint.Teleport"/>, granted by an enhancement:
/// <c>"Effects": {"Teleport": {}}</c> = these defaults). A Teleport order (<see cref="Orders.Order.TeleportTo"/>) makes
/// the unit stand still and charge for <see cref="ChargeTime"/> seconds, paying
/// <c>EnergyBase + EnergyPerDistance × distance</c> energy (fixed when the charge starts) through its economy request
/// (an energy stall slows the charge), then it vanishes and reappears at the destination
/// (<see cref="Commander.TeleportSystem"/>).
/// </summary>
public sealed class TeleportBlueprint
{
    /// <summary>Seconds of charging with a full energy supply.</summary>
    public float ChargeTime = 10f;
    public float EnergyBase = 5000f;
    /// <summary>Extra energy per world unit between the unit and the destination.</summary>
    public float EnergyPerDistance = 50f;
    /// <summary>&gt; 0: farther destinations are pulled back to this distance. 0 = unlimited.</summary>
    public float MaxRange;

    public float Cost(float distance) => EnergyBase + EnergyPerDistance * distance;
}

namespace Rts.Sim.Data;

public sealed partial class UnitBlueprint
{
    /// <summary>Personal teleporter (Commander/TeleportSystem; ACU enhancement). null = can't teleport.</summary>
    public TeleportBlueprint? Teleport;
}

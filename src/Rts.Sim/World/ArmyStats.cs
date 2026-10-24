namespace Rts.Sim.World;

/// <summary>
/// Per-army game statistics for the end-of-game screen. Updated in sequential sim code only (deterministic):
/// <see cref="Systems.ConstructionSystem.CompleteUnit"/> (built), <see cref="Systems.DamageSystem"/> (killed),
/// <see cref="Systems.CleanupSystem"/> (lost), <see cref="Systems.EconomySystem"/> (collected/spent).
/// Not part of the gameplay state hash.
/// </summary>
public sealed class ArmyStats
{
    /// <summary>Mobile units finished (factories and engineers; the starting commander is not counted).</summary>
    public int UnitsBuilt;
    /// <summary>Structures finished.</summary>
    public int StructuresBuilt;
    /// <summary>Own finished units/structures destroyed (reclaimed or cancelled ones are not counted).</summary>
    public int UnitsLost;
    /// <summary>Enemy units/structures destroyed by this army's weapons.</summary>
    public int UnitsKilled;
    /// <summary>Total mass/energy income (production + reclaim) over the game.</summary>
    public float MassCollected;
    public float EnergyCollected;
    /// <summary>Total mass/energy consumed (construction, upkeep).</summary>
    public float MassSpent;
    public float EnergySpent;
    /// <summary>(6b) Mass/energy given to allies (GiveResourcesCommand + storage overflow shared by EconomyShare).</summary>
    public float MassGiven, EnergyGiven;
    /// <summary>(6b) Mass/energy received from allies (gifts + their overflow).</summary>
    public float MassReceived, EnergyReceived;
}

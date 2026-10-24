using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Hooks called when units spawn/die. Each module initializes its own per-unit state here;
/// add a call to your system's InitUnit/OnUnitDeath instead of putting module logic in SimWorld.
/// </summary>
public static class UnitLifecycle
{
    public static void OnSpawn(SimWorld w, int i, UnitBlueprint bp)
    {
        MovementSystem.InitUnit(w, i, bp);
        WeaponSystem.InitUnit(w, i, bp);
        ConstructionSystem.InitUnit(w, i, bp);
        FactorySystem.InitUnit(w, i, bp);
        EconomySystem.InitUnit(w, i, bp);
        Strategic.StrategicSystem.InitUnit(w, i, bp);
        Transport.TransportSystem.InitUnit(w, i, bp);
        Experimental.ExperimentalSystem.InitUnit(w, i, bp);
        Combat.OverchargeSystem.InitUnit(w, i, bp);
        Commander.TeleportSystem.InitUnit(w, i, bp);
    }

    public static void OnDeath(SimWorld w, int i, UnitBlueprint bp)
    {
        ConstructionSystem.OnUnitDeath(w, i, bp);
        FactorySystem.OnUnitDeath(w, i, bp);
        Transport.TransportSystem.OnUnitDeath(w, i, bp); // cargo dies with its carrier
        Experimental.ExperimentalSystem.OnUnitDeath(w, i, bp);
        Commander.CommanderDeathBlast.OnUnitDeath(w, i, bp); // queues the blast (never damage here)
    }
}

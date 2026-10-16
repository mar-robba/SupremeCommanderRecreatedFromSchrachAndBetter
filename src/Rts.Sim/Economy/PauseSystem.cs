using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// SupCom pause toggle (<see cref="SetPausedCommand"/>): sets/clears <see cref="UnitFlags.Paused"/>. Every system
/// that consumes resources honours the flag: engineers/commanders stop building (ConstructionSystem), factories stop
/// their queue (FactorySystem), upgrades stop (UpgradeSystem), silos stop stockpiling (SiloSystem), mass fabricators
/// and other upkeep users stop producing/consuming (EconomySystem), shields drop, radar/sonar and stealth/cloak/stealth fields switch off.
/// Paused units still move and fight.
/// </summary>
public static class PauseSystem
{
    /// <summary>Units the pause button applies to: builders, silos, upgradeable structures and upkeep users.</summary>
    public static bool CanPause(UnitBlueprint bp) =>
        bp.Economy.BuildRate > 0 || bp.Silo != null || bp.Economy.MaintenanceEnergy > 0 || bp.UpgradesToIndex >= 0
        || bp.Has(UnitCategory.MassFabricator)
        || bp.Intel.RadarStealth || bp.Intel.SonarStealth || bp.Intel.Cloak || bp.Intel.StealthFieldRadius > 0; // intel toggle

    public static void Apply(SimWorld w, SetPausedCommand c)
    {
        var u = w.Units;
        foreach (var h in c.Units)
        {
            if (!CommandSystem.Owns(w, c.Army, h) || !CanPause(w.BpOf(h.Index))) continue;
            if (c.Paused) u.Flags[h.Index] |= UnitFlags.Paused;
            else u.Flags[h.Index] &= ~UnitFlags.Paused;
        }
    }
}

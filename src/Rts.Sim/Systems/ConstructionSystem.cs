using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Engineers/commanders: executes Build/Repair/Reclaim/Capture orders and Guard-assist; applies build power to
/// construction sites using economy efficiency. STUB. TODO(economy/construction): see docs/ROADMAP.md.
/// </summary>
public static class ConstructionSystem
{
    public static void InitUnit(SimWorld w, int i, UnitBlueprint bp) { }

    public static void OnUnitDeath(SimWorld w, int i, UnitBlueprint bp) { }

    /// <summary>Stops any build/repair/reclaim activity of unit i (called on Stop / order replace).</summary>
    public static void StopBuilder(SimWorld w, int i)
    {
        ref var b = ref w.Units.Builder[i];
        b.Mode = BuilderMode.None;
        b.Target = Core.UnitHandle.None;
        w.Units.Flags[i] &= ~UnitFlags.Building;
    }

    public static void Run(SimWorld w) { }
}

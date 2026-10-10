using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Mass/energy flow economy (SupCom style). STUB (only storage caps + commander production).
/// TODO(economy): see docs/ROADMAP.md. Model: each tick sum production and requests of all units per army,
/// efficiency = min(1, available / demand) per resource, update storage; consumers apply efficiency.
/// </summary>
public static class EconomySystem
{
    public static void InitUnit(SimWorld w, int i, UnitBlueprint bp)
    {
        ref var e = ref w.Units.Econ[i];
        e = default;
    }

    public static void Run(SimWorld w) { }
}

using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Factory build queues: builds units inside the factory, then releases them with the factory's rally orders.
/// STUB. TODO(economy/construction): see docs/ROADMAP.md.
/// </summary>
public static class FactorySystem
{
    public static void InitUnit(SimWorld w, int i, UnitBlueprint bp)
    {
        if (bp.Has(UnitCategory.Factory)) w.Units.Factory[i].Queue ??= new List<int>();
    }

    public static void OnUnitDeath(SimWorld w, int i, UnitBlueprint bp) { }

    public static void Enqueue(SimWorld w, FactoryEnqueueCommand c) { }

    public static void Dequeue(SimWorld w, FactoryDequeueCommand c) { }

    public static void Run(SimWorld w) { }
}

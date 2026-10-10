using System.Numerics;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Snapshot;

public struct UnitView
{
    public int Index;
    public int Gen;
    public int Bp;
    public int Army;
    public Vector3 Pos;
    public float Heading;
    /// <summary>Yaw of the first weapon's turret (world space).</summary>
    public float TurretHeading;
    public float HealthFrac;
    public float BuildFrac;
    public UnitFlags Flags;
    /// <summary>Range in SimSnapshot.Orders.</summary>
    public int OrderStart, OrderCount;
    /// <summary>Range in SimSnapshot.FactoryQueue.</summary>
    public int QueueStart, QueueCount;
    /// <summary>0..1 progress of what the unit is building (factory/engineer), -1 if none.</summary>
    public float WorkProgress;
    public bool FactoryRepeat;
}

public struct OrderView
{
    public OrderType Type;
    public Vector3 Pos;
    public int BlueprintIndex;
}

public struct ProjectileView
{
    public int Id;
    public Vector3 Pos;
    public Vector3 Vel;
    public int Army;
    public int SourceBp;
    public int WeaponIndex;
}

public struct ArmyView
{
    public int Index;
    public int Team;
    public uint Color;
    public bool Defeated;
    public int UnitCount;
    public float Mass, MassMax, MassIncome, MassDemand, MassEfficiency;
    public float Energy, EnergyMax, EnergyIncome, EnergyDemand, EnergyEfficiency;
}

/// <summary>
/// Immutable (after publish) copy of the sim state for the render thread. Pooled and reused: the client must
/// return snapshots to SimRunner when done with them.
/// </summary>
public sealed class SimSnapshot
{
    public int Tick;
    /// <summary>Stopwatch timestamp when published (for interpolation).</summary>
    public long PublishedAt;
    public double TickDurationMs;
    public int WinningTeam;

    public UnitView[] Units = new UnitView[256];
    public int UnitCount;
    /// <summary>Unit slot index -> position in Units[], or -1. Sized to the unit capacity.</summary>
    public int[] SlotToView = [];

    public OrderView[] Orders = new OrderView[256];
    public int OrderCount;
    public int[] FactoryQueue = new int[64];
    public int FactoryQueueCount;

    public ProjectileView[] Projectiles = new ProjectileView[256];
    public int ProjectileCount;

    public ArmyView[] Armies = [];
    public readonly List<EffectEvent> Effects = new();

    public bool TryGetUnit(int slot, int gen, out UnitView view)
    {
        if ((uint)slot < (uint)SlotToView.Length)
        {
            int v = SlotToView[slot];
            if (v >= 0 && Units[v].Gen == gen) { view = Units[v]; return true; }
        }
        view = default;
        return false;
    }
}

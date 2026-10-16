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
    /// <summary>Units destroyed by this unit (veterancy kills, UnitStore.Kills).</summary>
    public int Kills;
    /// <summary>In-place structure upgrade in progress: target blueprint (-1 = not upgrading) and its 0..1 progress.</summary>
    public int UpgradeBp;
    public float UpgradeFrac;
    /// <summary>Effective max health (blueprint MaxHealth × veterancy bonus, see Combat/Veterancy).</summary>
    public float MaxHealth;
    /// <summary>What the unit's build power is doing (Build/Repair/Reclaim/Assist/Capture/Upgrade), None if idle.</summary>
    public BuilderMode WorkMode;
    /// <summary>0..1 progress of an enemy capturing this unit (0 = not being captured).</summary>
    public float CaptureFrac;
    /// <summary>Adjacency bonuses (fractions, see EconState): +mass production, +energy production, -energy use.</summary>
    public float AdjMassProd, AdjEnergyProd, AdjEnergyUse;
    /// <summary>Aircraft flight fuel 0..1 (-1 = the unit uses no fuel). See Transport/TransportSystem.</summary>
    public float Fuel;
    /// <summary>Active stealth/cloak/stealth-field and EMP stun state (see <see cref="UnitStatus"/>).</summary>
    public UnitStatus Status;
    /// <summary>Fire state (Combat/FireStates).</summary>
    public FireState FireState;
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

/// <summary>
/// A reclaimable prop (unit wreck). Props don't move; track them across snapshots by <see cref="Id"/>.
/// To reclaim one, issue <c>Order.ReclaimAt(UnitHandle.None, Pos)</c> (the sim picks the nearest prop to Pos).
/// </summary>
public struct PropView
{
    /// <summary>Unique, never reused id.</summary>
    public int Id;
    /// <summary>Blueprint of the unit that left this wreck (draw its mesh darkened/broken), -1 = generic prop.</summary>
    public int Bp;
    /// <summary>World position (y = terrain height; may be under water).</summary>
    public Vector3 Pos;
    public float Heading;
    /// <summary>Footprint radius (structures: half footprint, units: unit radius).</summary>
    public float Radius;
    /// <summary>Remaining reclaimable mass / energy and the initial amounts (Mass/MassMax = how much is left).</summary>
    public float Mass, MassMax;
    public float Energy, EnergyMax;
}

public struct ArmyView
{
    public int Index;
    public int Team;
    public uint Color;
    /// <summary>Faction id (BlueprintDb.Factions).</summary>
    public string Faction;
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

    /// <summary>Reclaimable props / wrecks, [0..PropCount).</summary>
    public PropView[] Props = new PropView[64];
    public int PropCount;

    public ArmyView[] Armies = [];
    public readonly List<EffectEvent> Effects = new();

    // ---- Fog of war (intel module): see SimRunner.ViewArmy and Intel/IntelSnapshotWriter.cs ----
    /// <summary>Army whose intel filtered this snapshot; -1 = no fog (everything visible, Fog empty). With a view
    /// army, Units only holds units it can see, Projectiles/Effects only what is in its vision (plus allied shots).</summary>
    public int ViewArmy = -1;
    /// <summary>Per fog cell (row-major FogCols x FogRows, cell = FogCellSize world units): 0 unexplored,
    /// 128 explored but not in vision, 255 in vision.</summary>
    public byte[] Fog = [];
    public int FogCols, FogRows;
    public float FogCellSize;
    /// <summary>Changes whenever the sim recomputed intel (re-upload the fog texture).</summary>
    public int FogVersion;
    /// <summary>Radar/sonar contacts (enemy units detected but not visible), [0..BlipCount).</summary>
    public BlipView[] Blips = new BlipView[64];
    public int BlipCount;
    /// <summary>Last-seen enemy structures not currently visible ("ghosts"), [0..GhostCount).</summary>
    public GhostView[] Ghosts = new GhostView[64];
    public int GhostCount;

    // ---- Strategic module (shields, silos, launches): see Strategic/StrategicSnapshotWriter.cs ----
    /// <summary>Shield domes of units in this snapshot, [0..ShieldCount).</summary>
    public ShieldView[] Shields = new ShieldView[16];
    public int ShieldCount;
    /// <summary>Missile silos of the view army's team, [0..SiloCount).</summary>
    public SiloView[] Silos = new SiloView[8];
    public int SiloCount;
    /// <summary>Recent strategic launches (global, newest last) and the total launch count.</summary>
    public readonly List<LaunchView> Launches = new();
    public int LaunchSerial;

    // ---- Transport module (cargo, ferry routes): see Transport/TransportSnapshotWriter.cs ----
    /// <summary>Carriers among Units (transports, air staging, Atlantis) with their cargo, [0..CargoViewCount).</summary>
    public CargoView[] CargoViews = new CargoView[8];
    public int CargoViewCount;
    /// <summary>Units inside carriers (ranges given by CargoView.Start/Count), [0..CargoUnitCount).</summary>
    public CargoUnitView[] CargoUnits = new CargoUnitView[16];
    public int CargoUnitCount;
    /// <summary>Ferry routes of the view army's team (all armies without fog), [0..FerryCount).</summary>
    public FerryView[] Ferries = new FerryView[4];
    public int FerryCount;
    /// <summary>Sandbox mode settings (cheat toggles, AI per army); null outside sandbox. Immutable, see SandboxSystem.</summary>
    public Systems.SandboxStatus? Sandbox;

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

using System.Numerics;
using Rts.Sim.Core;

namespace Rts.Sim.World;

[Flags]
public enum UnitFlags : ushort
{
    None = 0,
    /// <summary>Killed this tick; slot freed by CleanupSystem at end of tick. Treat as gone.</summary>
    Dead = 1 << 0,
    /// <summary>Structure/unit still being built (BuildFraction &lt; 1). Inactive: no weapons, no production.</summary>
    UnderConstruction = 1 << 1,
    Moving = 1 << 2,
    Firing = 1 << 3,
    Building = 1 << 4,
    /// <summary>Production/upkeep switched off (e.g. energy stall or player toggle).</summary>
    Paused = 1 << 5,
    /// <summary>Visible to enemies via radar only (set by intel; informational).</summary>
    RadarOnly = 1 << 6,
}

/// <summary>Movement module state (owned by MovementSystem / PathSystem).</summary>
public struct MoveState
{
    public Vector2 Velocity;
    public float Speed;
    /// <summary>Final destination of the current move request.</summary>
    public Vector2 Goal;
    public bool HasGoal;
    /// <summary>Distance at which the goal counts as reached.</summary>
    public float ArriveRadius;
    /// <summary>Path handle from PathService (-1 = none / direct steering).</summary>
    public int PathId;
    /// <summary>Index of the next waypoint in the path.</summary>
    public int PathIndex;
    /// <summary>Goal for which PathId was computed (to detect goal changes).</summary>
    public Vector2 PathGoal;
    public float StuckTime;
    /// <summary>Set by OrderSystem: true when the unit has arrived at Goal this tick.</summary>
    public bool Arrived;
}

/// <summary>Per-weapon state; units have up to UnitStore.MaxWeapons weapons (index = unit * MaxWeapons + w).</summary>
public struct WeaponState
{
    public UnitHandle Target;
    /// <summary>Seconds until the weapon can fire again.</summary>
    public float Cooldown;
    /// <summary>World-space turret yaw (radians).</summary>
    public float TurretHeading;
    public float TurretPitch;
    /// <summary>Ground-attack position when Target is None and HasGroundTarget.</summary>
    public Vector2 GroundTarget;
    public bool HasGroundTarget;
    /// <summary>Ticks until next target re-acquisition scan (staggered to spread cost).</summary>
    public int RetargetTimer;
}

public enum BuilderMode : byte { None, Build, Repair, Reclaim, Assist, Capture }

/// <summary>Engineer/commander/factory build-power state (owned by ConstructionSystem).</summary>
public struct BuilderState
{
    public BuilderMode Mode;
    /// <summary>Unit being built/repaired/reclaimed/assisted.</summary>
    public UnitHandle Target;
    /// <summary>Build power actually applied last tick (after economy efficiency).</summary>
    public float AppliedRate;
}

/// <summary>Factory production state (owned by FactorySystem).</summary>
public struct FactoryState
{
    /// <summary>Queued blueprint indices, front = currently building. Allocated lazily for factories.</summary>
    public List<int>? Queue;
    /// <summary>Unit currently under construction inside the factory.</summary>
    public UnitHandle Current;
    public bool Repeat;
}

/// <summary>
/// Economy requests for this tick (owned by EconomySystem). Systems that consume resources fill the
/// requests during the tick; EconomySystem computes efficiency; consumers multiply their progress by it
/// on the next tick (one-tick lag, like SupCom).
/// </summary>
public struct EconState
{
    /// <summary>Mass per second this unit wants to spend this tick.</summary>
    public float MassRequest;
    public float EnergyRequest;
    /// <summary>Per-second production this tick (0 when under construction / paused).</summary>
    public float MassProduction;
    public float EnergyProduction;
}

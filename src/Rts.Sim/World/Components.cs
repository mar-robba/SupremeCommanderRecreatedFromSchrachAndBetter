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
    /// <summary>Set by MovementSystem: true when the unit arrived at Goal (or at the nearest reachable point of an
    /// unreachable Goal) during the last movement step. OrderSystem completes Move orders on it.</summary>
    public bool Arrived;

    // ---- Pathfinding / movement extensions (owned by PathSystem + MovementSystem) ----
    /// <summary>Formation group: units ordered together share a pace (slowest member). 0 = no group.
    /// Set by CommandSystem via <see cref="Systems.MovementSystem.AssignGroup"/>.</summary>
    public int GroupId;
    /// <summary>Tick at which the unit started waiting for a path (0 = not waiting). Oldest requests are served first.</summary>
    public int PathRequestTick;
    /// <summary>Set by MovementSystem when the current path must be recomputed (blocked by terrain/structures).</summary>
    public bool RepathRequested;
    /// <summary>The path covers only the first part of a long route; PathSystem extends it near its end.</summary>
    public bool PathPartial;
    /// <summary>Goal unreachable: the path ends at the nearest reachable point and arriving there counts as arrival.</summary>
    public bool PathToNearest;
    /// <summary>Reusing a group leader's path failed: solve this unit's next path individually.</summary>
    public bool PathNoShare;
    /// <summary>NavGrid version the path was last validated against (structures placed/destroyed bump it).</summary>
    public int PathNavVersion;
    /// <summary>Seconds the unit has been held back by terrain/structures while trying to move.</summary>
    public float BlockedTime;
    /// <summary>Repaths requested because of being stuck, for the current goal.</summary>
    public int StuckRepaths;
    /// <summary>Air units: center of the idle orbit (last goal) and orbit direction (+1 CCW, -1 CW).</summary>
    public Vector2 OrbitCenter;
    public float OrbitDir;
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

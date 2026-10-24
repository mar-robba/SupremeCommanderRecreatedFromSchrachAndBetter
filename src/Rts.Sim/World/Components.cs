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
    /// <summary>Submarines (MotionLayer.Sub): currently surfaced (Water layer: visible like a ship, hit by surface
    /// weapons). Cleared = submerged (the default). Toggled by <see cref="Orders.SetSubmergedCommand"/>.</summary>
    Surfaced = 1 << 7,
    /// <summary>Inside a carrier (transport / air staging / Atlantis): off the map. Not in AliveList (so not in the
    /// spatial grid, untargetable, no weapons/intel/economy); moved with the carrier by Transport.TransportSystem.</summary>
    Carried = 1 << 10,
    /// <summary>EMP-stunned (Combat/EmpStun): no moving, firing, building or producing until the stun ends.</summary>
    Stunned = 1 << 11,
    /// <summary>ACU Overcharge armed (toggled by <see cref="Orders.SetOverchargeCommand"/>; Combat/OverchargeSystem).</summary>
    Overcharge = 1 << 12,
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
    /// <summary>Idle air units chasing a target their weapons engaged (<see cref="Systems.IdleAirEngage"/>); they
    /// return to <see cref="IdleHome"/> (the orbit center when the chase began) afterwards.</summary>
    public bool IdleChase;
    public Vector2 IdleHome;
    /// <summary>Surface units just released by a factory: seconds left to drive (through the factory's own
    /// footprint) to <see cref="FactoryExit"/>; 0 = not leaving (<see cref="Systems.FactoryExitMotion"/>).</summary>
    public float LeaveTime;
    public Vector2 FactoryExit;
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

/// <summary>Upgrade: in-place structure upgrade, the target is the upgrading structure (see UpgradeSystem).</summary>
public enum BuilderMode : byte { None, Build, Repair, Reclaim, Assist, Capture, Upgrade }

/// <summary>Engineer/commander/factory build-power state (owned by ConstructionSystem).</summary>
public struct BuilderState
{
    public BuilderMode Mode;
    /// <summary>Unit being built/repaired/reclaimed/assisted.</summary>
    public UnitHandle Target;
    /// <summary>Build power actually applied last tick (after economy efficiency).</summary>
    public float AppliedRate;
    /// <summary>
    /// Work requested last tick on <see cref="Target"/> (fraction of the target's BuildTime for Build/Assist, fraction
    /// of its MaxHealth for Repair), before efficiency. Resources for it are paid by EconomySystem at the start of the
    /// next tick and the work is then applied scaled by <see cref="EconState.Efficiency"/> (pay first, then progress).
    /// </summary>
    public float PendingWork;
    /// <summary>As a TARGET: work already requested on this unit this tick by all builders (avoids overshoot).</summary>
    public float ReservedWork;
    /// <summary>Tick in which <see cref="ReservedWork"/> was accumulated (stale values are ignored).</summary>
    public int ReservedTick;
    /// <summary>As a TARGET: 0..1 progress of an enemy capture (Economy/CaptureSystem); reset when nobody captured
    /// it for a while (see <see cref="CaptureTick"/>).</summary>
    public float CaptureProgress;
    /// <summary>As a TARGET: last tick capture work was requested on this unit (the progress resets when nobody
    /// worked on it for <see cref="Systems.CaptureSystem.ResetTicks"/>).</summary>
    public int CaptureTick;
    /// <summary>As a TARGET: capture work reserved by captors during <see cref="CaptureTick"/> (no overshoot).</summary>
    public float CaptureReserved;
    /// <summary>As a TARGET: army index + 1 of the last captor (0 = never captured); see Combat/FireStates.</summary>
    public int CaptureArmy;
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
    /// <summary>
    /// Fraction (0..1) of this unit's requests (build costs + upkeep) granted this tick by EconomySystem:
    /// min of the army efficiencies of the resources it requested, 1 if it requested nothing. Consumers (builders,
    /// upkeep users such as radar/shields) scale their work by it.
    /// </summary>
    public float Efficiency;
    /// <summary>Resources gained by reclaiming this tick (per second); credited as income by EconomySystem next tick.</summary>
    public float ReclaimMass;
    public float ReclaimEnergy;
    /// <summary>Adjacency bonuses (Economy/AdjacencySystem, recomputed when structures are built/die/change owner):
    /// fractional mass/energy production bonus (+0.25 = +25%) and energy-use reduction (0.25 = -25% on every energy
    /// request and upkeep of this unit).</summary>
    public float AdjMassProd;
    public float AdjEnergyProd;
    public float AdjEnergyUse;
}

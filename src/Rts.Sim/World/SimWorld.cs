using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Systems;

namespace Rts.Sim.World;

/// <summary>
/// The complete simulation state. Owned by the simulation thread: the client must NEVER read or write it
/// while the sim is running (use snapshots and commands). Exceptions: Terrain and Blueprints are immutable.
/// </summary>
public sealed class SimWorld
{
    public const int TicksPerSecond = 10;
    public const float Dt = 1f / TicksPerSecond;

    public int Tick;
    public readonly ulong Seed;
    public readonly BlueprintDb Blueprints;
    public readonly Terrain Terrain;
    public readonly Army[] Armies;
    public readonly UnitStore Units;
    public readonly ProjectileStore Projectiles;
    public readonly SpatialGrid Grid;
    public readonly OccupancyGrid Occupancy;
    /// <summary>Reclaimable wrecks/props (economy/construction module).</summary>
    public readonly PropStore Props;
    public readonly SimEvents Events = new();
    /// <summary>Units killed this tick, freed by CleanupSystem.</summary>
    public readonly List<int> PendingDeaths = new();
    public readonly PathService Paths;
    public readonly IntelService Intel;
    public bool CheatsEnabled = true;
    /// <summary>Set when only one team remains.</summary>
    public int WinningTeam = -1;

    public float GameTime => Tick * Dt;

    public SimWorld(BlueprintDb blueprints, Terrain terrain, Army[] armies, ulong seed, int unitCapacity = 16384, int projectileCapacity = 65536)
    {
        Blueprints = blueprints;
        Terrain = terrain;
        Armies = armies;
        for (int i = 0; i < armies.Length; i++) armies[i].Index = i;
        Seed = seed;
        Units = new UnitStore(unitCapacity);
        Projectiles = new ProjectileStore(projectileCapacity);
        Grid = new SpatialGrid(terrain.Width, terrain.Height, 8f, unitCapacity);
        Occupancy = new OccupancyGrid(terrain.Width, terrain.Height);
        Props = new PropStore(Math.Max(4096, unitCapacity));
        Paths = new PathService(this);
        Intel = new IntelService(this);
    }

    public bool IsEnemy(int armyA, int armyB) => armyA != armyB && Armies[armyA].Team != Armies[armyB].Team;
    public bool IsAlly(int armyA, int armyB) => !IsEnemy(armyA, armyB);

    /// <summary>Deterministic per-unit random stream for this tick (safe in parallel systems).</summary>
    public DetRandom RandomFor(int index, int salt = 0) => DetRandom.For(Seed, Tick, index, salt);

    public UnitBlueprint BpOf(int unitIndex) => Blueprints[Units.Bp[unitIndex]];

    /// <summary>
    /// Creates a unit. SEQUENTIAL PHASES ONLY. complete=false spawns it as a construction site
    /// (BuildFraction 0, 1 HP, UnderConstruction). Structures are snapped to the grid and stamped into Occupancy.
    /// Returns UnitHandle.None if the unit store is full.
    /// </summary>
    public UnitHandle SpawnUnit(int bpIndex, int army, Vector2 pos, float heading, bool complete = true)
    {
        var bp = Blueprints[bpIndex];
        int i = Units.Allocate();
        if (i < 0) return UnitHandle.None;
        if (bp.IsStructure) pos = OccupancyGrid.SnapStructure(bp, pos);
        pos = Terrain.Clamp(pos);
        Units.Bp[i] = bpIndex;
        Units.Army[i] = army;
        Units.Pos[i] = pos;
        Units.Heading[i] = heading;
        Units.BuildFraction[i] = complete ? 1f : 0f;
        Units.Health[i] = complete ? bp.MaxHealth : 1f;
        if (!complete) Units.Flags[i] |= UnitFlags.UnderConstruction;
        Units.Elevation[i] = bp.Movement.Layer == MotionLayer.Air && complete
            ? Terrain.GetSurfaceHeight(pos) + bp.Movement.Altitude
            : bp.Movement.Layer is MotionLayer.Land or MotionLayer.Amphibious or MotionLayer.None && !bp.HasAny(UnitCategory.Naval)
                ? Terrain.GetHeight(pos)
                : Terrain.GetSurfaceHeight(pos);
        if (bp.IsStructure) Occupancy.Stamp(bp, pos, i);
        UnitLifecycle.OnSpawn(this, i, bp);
        Armies[army].UnitCount++;
        // Note: the new unit joins Units.AliveList (and the spatial grid) at the start of the next tick.
        return Units.HandleOf(i);
    }

    /// <summary>Marks a unit dead (freed at end of tick by CleanupSystem). Safe in sequential phases.</summary>
    public void KillUnit(int index, UnitHandle killer)
    {
        if (!Units.IsActive(index)) return;
        Units.Flags[index] |= UnitFlags.Dead;
        Units.Health[index] = 0;
        PendingDeaths.Add(index);
        var bp = BpOf(index);
        if (Units.IsActive(killer)) Units.Kills[killer.Index]++;
        Events.Effects.Add(new EffectEvent
        {
            Kind = EffectKind.UnitDeath,
            Pos = DMath.To3(Units.Pos[index], Units.Elevation[index]),
            Size = MathF.Max(bp.Visual.Size.X, bp.Visual.Size.Z),
            Army = Units.Army[index],
            BlueprintIndex = bp.Index,
        });
        UnitLifecycle.OnDeath(this, index, bp);
    }
}

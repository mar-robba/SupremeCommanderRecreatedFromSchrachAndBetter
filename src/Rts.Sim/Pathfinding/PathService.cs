using System.Numerics;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Pathfinding service. STUB: returns the goal directly (straight-line movement).
/// TODO(pathfinding): real implementation - see docs/ROADMAP.md (hierarchical/coarse-grid A* with
/// OccupancyGrid awareness, path cache, requests solved in parallel by PathSystem in deterministic order).
/// </summary>
public sealed class PathService
{
    private readonly SimWorld _w;

    public PathService(SimWorld w) => _w = w;

    /// <summary>Next point the unit should steer towards. Called from parallel MovementSystem: must be thread-safe
    /// for reads and only mutate the given unit's MoveState.</summary>
    public Vector2 CurrentWaypoint(int unit, ref MoveState mv, Vector2 pos) => mv.Goal;
}

/// <summary>Resolves pending path requests each tick. STUB.</summary>
public static class PathSystem
{
    public static void Run(SimWorld w) { }
}

using System.Numerics;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Fog of war / radar. STUB: everything is visible to everyone.
/// TODO(intel): per-army visibility grids (vision, radar, sonar), updated by IntelSystem in parallel per army;
/// targeting must only acquire units visible to (or on radar of) the attacker's army.
/// </summary>
public sealed class IntelService
{
    private readonly SimWorld _w;

    public IntelService(SimWorld w) => _w = w;

    /// <summary>Can `army` see unit `unitIndex` (vision)? Thread-safe read.</summary>
    public bool IsVisible(int army, int unitIndex) => true;

    /// <summary>Is the unit detected on radar by `army` (position known, identity maybe not)?</summary>
    public bool IsDetected(int army, int unitIndex) => true;

    /// <summary>Is the map position currently in vision of `army`?</summary>
    public bool IsPositionVisible(int army, Vector2 pos) => true;
}

public static class IntelSystem
{
    public static void Run(SimWorld w) { }
}

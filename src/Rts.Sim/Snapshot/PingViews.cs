using System.Numerics;
using Rts.Sim.Orders;

namespace Rts.Sim.Snapshot;

/// <summary>(7b) A live map ping (SimSnapshot.Pings, written by Orders/PingSystem.WriteSnapshot).</summary>
public struct MapPingView
{
    public int Id, Army;
    public PingKind Kind;
    /// <summary>Ground point (terrain/water surface height).</summary>
    public Vector3 Pos;
    /// <summary>Tick the ping was created (it lives PingSystem.Lifetime ticks).</summary>
    public int Tick;
}

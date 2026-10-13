using Rts.Sim.Snapshot;

namespace Rts.Client.Rendering;

/// <summary>
/// 3D overlay for the cargo mechanic (inside BeginMode3D): ferry routes + beacons (<see cref="SimSnapshot.Ferries"/>)
/// and lines for selected transports / docked aircraft. Owned by the transport UI.
/// </summary>
public static class TransportOverlay
{
    public static void Draw3D(GameContext ctx, HashSet<long> selected, SimSnapshot cur, SimSnapshot? prev, float alpha)
    {
        // TODO (transport UI agent): ferry route lines + beacons.
    }
}

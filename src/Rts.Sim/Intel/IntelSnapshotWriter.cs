using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Snapshot;

/// <summary>
/// A radar/sonar contact: an enemy unit the view army detects but can't see. Position only (no type, no handle:
/// it can't be selected or targeted as a unit; attack-move / attack-ground at its position instead).
/// </summary>
public struct BlipView
{
    /// <summary>Stable while the contact persists (for client-side animation), not a unit handle.</summary>
    public int Id;
    public int Army;
    /// <summary>World position (y = unit elevation).</summary>
    public Vector3 Pos;
    /// <summary>Ground velocity (x, z) per second, for interpolation: draw at Pos - Vel * (1 - alpha) * tick.</summary>
    public Vector2 Vel;
}

/// <summary>A last-seen enemy structure that is not currently visible (it may have been destroyed since).</summary>
public struct GhostView
{
    public int Bp;
    public int Army;
    public Vector3 Pos;
    public float Heading;
}

/// <summary>
/// Fog-of-war filtering for <see cref="SnapshotWriter"/> (sim thread). With a view army, the snapshot only contains
/// what that army's team knows: units in vision (full info), radar blips, ghosts of last-seen structures, projectiles
/// and effects in vision, and the explored/vision grid for the fog overlay.
/// </summary>
internal static class IntelSnapshotWriter
{
    public const byte Hidden = 0, Visible = 2;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SimWorld, byte[]> Scratch = new();

    /// <summary>
    /// Writes the fog grid, blips and ghosts. Returns the per-slot view state (Visible / Hidden, valid for units in
    /// AliveList) or null when there is no fog (viewArmy &lt; 0 or intel disabled): then everything is visible.
    /// </summary>
    public static byte[]? Begin(SimWorld w, SimSnapshot s, int viewArmy)
    {
        var intel = w.Intel;
        s.BlipCount = 0;
        s.GhostCount = 0;
        s.FogVersion = intel.Version;
        if (!intel.Enabled || (uint)viewArmy >= (uint)w.Armies.Length)
        {
            s.ViewArmy = -1;
            s.FogCols = s.FogRows = 0;
            return null;
        }
        s.ViewArmy = viewArmy;
        s.FogCols = intel.Cols;
        s.FogRows = intel.Rows;
        s.FogCellSize = intel.CellSize;
        int cells = intel.Cols * intel.Rows;
        if (s.Fog.Length != cells) s.Fog = new byte[cells];
        intel.FogOf(viewArmy).CopyTo(s.Fog);

        var u = w.Units;
        var vis = Scratch.GetValue(w, static x => new byte[x.Units.Capacity]);
        int team = intel.TeamOf(viewArmy);
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            if (!u.IsActive(i)) continue;
            if (intel.IsVisible(viewArmy, i)) { vis[i] = Visible; continue; }
            vis[i] = Hidden;
            if (!intel.IsDetected(viewArmy, i)) continue;
            // A structure remembered as a ghost is shown as the ghost, not as an anonymous blip.
            if (intel.TryGetGhost(team, i, out var g) && g.Gen == u.Gen[i]) continue;
            Ensure(ref s.Blips, s.BlipCount + 1);
            s.Blips[s.BlipCount++] = new BlipView
            {
                Id = i, Army = u.Army[i], Pos = DMath.To3(u.Pos[i], u.Elevation[i]), Vel = u.Move[i].Velocity,
            };
        }

        var ti = intel.Teams[team];
        foreach (int slot in ti.GhostSlots)
        {
            ref var g = ref ti.Ghosts[slot];
            if (u.Alive[slot] && u.Gen[slot] == g.Gen && vis[slot] == Visible && u.IsActive(slot)) continue; // shown as a unit
            Ensure(ref s.Ghosts, s.GhostCount + 1);
            s.Ghosts[s.GhostCount++] = new GhostView { Bp = g.Bp, Army = g.Army, Pos = DMath.To3(g.Pos, g.Elevation), Heading = g.Heading };
        }
        return vis;
    }

    /// <summary>Allied projectiles are always shown, enemy ones only in vision.</summary>
    public static bool ProjectileVisible(SimWorld w, int viewArmy, int projectileArmy, Vector3 pos) =>
        w.Intel.TeamOf(projectileArmy) == w.Intel.TeamOf(viewArmy) || w.Intel.IsPositionVisible(viewArmy, new Vector2(pos.X, pos.Z));

    /// <summary>Copies the effects whose position is in the view army's vision.</summary>
    public static void AddVisibleEffects(SimWorld w, SimSnapshot s, int viewArmy)
    {
        foreach (var e in w.Events.Effects)
            if (w.Intel.IsPositionVisible(viewArmy, new Vector2(e.Pos.X, e.Pos.Z))) s.Effects.Add(e);
    }

    private static void Ensure<T>(ref T[] arr, int size)
    {
        if (arr.Length < size) Array.Resize(ref arr, Math.Max(size, arr.Length * 2));
    }
}

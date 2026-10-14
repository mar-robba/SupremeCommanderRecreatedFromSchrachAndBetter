using System.Numerics;

namespace Rts.Client.Input;

/// <summary>
/// Pure layout math of Shift+drag structure lines (SupCom): copies of a footprint laid from the anchor (where the
/// drag started) toward the cursor, exactly one footprint apart so they never overlap. The drag direction is snapped
/// to the nearest of the 8 grid directions (axis or 45-degree diagonal, measured in footprint units) and the line
/// length is the projection of the drag on that direction, rounded to whole footprints. Anchor and cursor are
/// snapped structure centers (<c>OccupancyGrid.SnapStructure</c>); the steps are whole cells, so results stay snapped.
/// </summary>
public static class PlacementLine
{
    /// <summary>Upper bound of ghosts in one line (keeps a long drag cheap and the order queue sane).</summary>
    public const int MaxCount = 64;

    /// <summary>tan(22.5 deg): above this minor/major ratio the drag snaps to a diagonal.</summary>
    private const float DiagonalRatio = 0.41421356f;

    /// <summary>Fills <paramref name="into"/> with the ghost centers, anchor first; returns the count (at least 1).</summary>
    public static int Layout(Vector2 anchor, Vector2 cursor, int footX, int footZ, List<Vector2> into, int max = MaxCount)
    {
        into.Clear();
        footX = Math.Max(1, footX);
        footZ = Math.Max(1, footZ);
        float u = (cursor.X - anchor.X) / footX, v = (cursor.Y - anchor.Y) / footZ;
        float au = MathF.Abs(u), av = MathF.Abs(v);
        int sx = 0, sz = 0;
        float len;
        if (av <= au * DiagonalRatio) { sx = MathF.Sign(u); len = au; }
        else if (au <= av * DiagonalRatio) { sz = MathF.Sign(v); len = av; }
        else { sx = MathF.Sign(u); sz = MathF.Sign(v); len = (au + av) * 0.5f; }
        int count = Math.Clamp((int)MathF.Floor(len + 0.5f) + 1, 1, Math.Max(1, max));
        var step = new Vector2(sx * footX, sz * footZ);
        for (int i = 0; i < count; i++) into.Add(anchor + step * i);
        return count;
    }
}

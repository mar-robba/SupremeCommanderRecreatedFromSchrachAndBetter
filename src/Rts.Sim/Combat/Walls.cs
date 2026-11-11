using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>
/// Wall sections (category <see cref="UnitCategory.Wall"/>, ARCHITECTURE "Missing units &amp; walls (8a)"):
/// <list type="bullet">
/// <item>they are 1x1 structures, so the occupancy grid already blocks paths through them (A* never cuts a corner,
///   which closes diagonal lines too);</item>
/// <item>an enemy wall on the straight segment of a direct shot absorbs it: projectiles test walls as a box (their
///   footprint cell, <c>Visual.Size.Y</c> tall) instead of a sphere, so a diagonal line has no gaps
///   (ProjectileSystem's hit visitor), and beams walk the cells under their segment (<see cref="FirstOnSegment"/>);
///   arcing (ballistic) shots only test their target, so they fly over;</item>
/// <item>targeting never auto-acquires a wall (TargetingSystem); an explicit Attack order on one still works.</item>
/// </list>
/// Derived from blueprints only: no sim state.
/// </summary>
public static class Walls
{
    public static bool IsWall(UnitBlueprint bp) => bp.Has(UnitCategory.Wall);

    /// <summary>First parameter in [0, 1] where segment a→b enters wall j's box, or -1.</summary>
    public static float SegmentHit(SimWorld w, int j, Vector3 a, Vector3 b)
    {
        var u = w.Units;
        var bp = w.BpOf(j);
        float hx = bp.FootprintX * 0.5f, hz = bp.FootprintZ * 0.5f;
        var p = u.Pos[j];
        float y0 = u.Elevation[j];
        return SegmentBox(a, b, new Vector3(p.X - hx, y0, p.Y - hz), new Vector3(p.X + hx, y0 + MathF.Max(0.5f, bp.Visual.Size.Y), p.Y + hz));
    }

    /// <summary>Slab test: first parameter in [0, 1] where segment a→b is inside the box (0 if a starts inside), or -1.</summary>
    public static float SegmentBox(Vector3 a, Vector3 b, Vector3 min, Vector3 max)
    {
        float t0 = 0f, t1 = 1f;
        var d = b - a;
        if (!Slab(a.X, d.X, min.X, max.X, ref t0, ref t1)) return -1f;
        if (!Slab(a.Y, d.Y, min.Y, max.Y, ref t0, ref t1)) return -1f;
        if (!Slab(a.Z, d.Z, min.Z, max.Z, ref t0, ref t1)) return -1f;
        return t0;
    }

    private static bool Slab(float a, float d, float min, float max, ref float t0, ref float t1)
    {
        if (MathF.Abs(d) < 1e-7f) return a >= min && a <= max;
        float inv = 1f / d;
        float ta = (min - a) * inv, tb = (max - a) * inv;
        if (ta > tb) (ta, tb) = (tb, ta);
        if (ta > t0) t0 = ta;
        if (tb < t1) t1 = tb;
        return t0 <= t1;
    }

    /// <summary>
    /// First enemy wall (of <paramref name="army"/>, on a layer in <paramref name="mask"/>) whose box segment a→b
    /// enters, walking the occupancy cells under the segment in order (beams). Returns the unit index or -1.
    /// </summary>
    internal static int FirstOnSegment(SimWorld w, CombatState s, int army, LayerMask mask, Vector3 a, Vector3 b, out float t)
    {
        t = 2f;
        var occ = w.Occupancy;
        var u = w.Units;
        float dx = b.X - a.X, dz = b.Z - a.Z;
        int cx = (int)MathF.Floor(a.X), cz = (int)MathF.Floor(a.Z);
        int ex = (int)MathF.Floor(b.X), ez = (int)MathF.Floor(b.Z);
        int sx = dx > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
        float tdx = MathF.Abs(dx) < 1e-7f ? float.MaxValue : MathF.Abs(1f / dx);
        float tdz = MathF.Abs(dz) < 1e-7f ? float.MaxValue : MathF.Abs(1f / dz);
        float tmx = MathF.Abs(dx) < 1e-7f ? float.MaxValue : ((dx > 0 ? cx + 1 - a.X : a.X - cx) * tdx);
        float tmz = MathF.Abs(dz) < 1e-7f ? float.MaxValue : ((dz > 0 ? cz + 1 - a.Z : a.Z - cz) * tdz);
        int steps = Math.Abs(ex - cx) + Math.Abs(ez - cz) + 1;
        for (int k = 0; k < steps; k++)
        {
            int j = occ.At(cx, cz);
            if (j >= 0 && u.IsActive(j) && s.IsEnemyArmy(army, u.Army[j]) && (s.Layer[j] & mask) != 0 && s.Catalog.Units[u.Bp[j]].IsWall)
            {
                float hit = SegmentHit(w, j, a, b);
                if (hit >= 0f) { t = hit; return j; }
            }
            if (tmx < tmz) { tmx += tdx; cx += sx; }
            else { tmz += tdz; cz += sz; }
        }
        return -1;
    }
}

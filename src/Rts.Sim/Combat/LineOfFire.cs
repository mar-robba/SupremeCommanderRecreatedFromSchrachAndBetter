using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>
/// Terrain line-of-fire checks used by target acquisition (<see cref="Systems.TargetingSystem"/>): a weapon should
/// not lock onto a target it cannot hit from where it stands, otherwise units below a cliff fire into the rock
/// forever and, on attack-move, never walk up the ramp (they stop while they have a target).
/// <list type="bullet">
/// <item>Straight shots and beams need a clear line from the muzzle to the target center over the terrain (with the
/// same grazing tolerance projectiles use).</item>
/// <item>Ballistic shells need an arc solution (a target high on a plateau can be out of reach of the arc).</item>
/// <item>Bombs, missiles and flak are not restricted.</item>
/// </list>
/// Pure function of terrain and positions: thread-safe and deterministic.
/// </summary>
public static class LineOfFire
{
    /// <summary>Horizontal sampling step along the line of fire (world units).</summary>
    public const float Step = 0.75f;
    /// <summary>The line may graze the surface by this much (projectiles use <see cref="ProjectileSystem.StraightGroundTolerance"/>).</summary>
    public const float Tolerance = 0.3f;

    /// <summary>Can weapon <paramref name="winfo"/> of unit <paramref name="shooter"/> hit unit <paramref name="target"/>
    /// from the shooter's current position?</summary>
    internal static bool CanHit(SimWorld w, CombatState s, int shooter, in WeaponInfo winfo, WeaponBlueprint wbp, int target)
    {
        if (winfo.ProximityFuse) return true;
        if (winfo.Kind is not (WeaponKind.Direct or WeaponKind.Beam or WeaponKind.Ballistic)) return true;
        var u = w.Units;
        var p = u.Pos[shooter];
        var muzzle = new Vector3(p.X, u.Elevation[shooter] + s.Catalog.Units[u.Bp[shooter]].MuzzleHeight, p.Y);
        if (Naval.Submarines.WeaponsUnderwater(w, shooter)) muzzle.Y = Naval.Submarines.UnderwaterMuzzleY(w, muzzle.Y, winfo.Kind); // FiresSubmerged gun
        var tc = s.Center(u, target);
        if (winfo.Kind == WeaponKind.Ballistic)
            return Ballistics.SolveArc(muzzle, tc, wbp.MuzzleVelocity, Ballistics.Gravity, winfo.HighArc, out _, out _);
        return IsClear(w.Terrain, muzzle, tc);
    }

    /// <summary>
    /// True if the shell trajectory from <paramref name="from"/> with launch velocity <paramref name="vel"/> stays above
    /// the terrain/water surface (minus <see cref="Tolerance"/>) for its <paramref name="time"/> seconds of flight
    /// (gravity <see cref="Ballistics.Gravity"/>). Sampled every <see cref="Step"/> horizontally; the first step and the
    /// last <see cref="ArcEndSkip"/> units (the target's own ground) are not tested. Pure: thread-safe, deterministic.
    /// </summary>
    public static bool IsArcClear(Terrain t, Vector3 from, Vector3 vel, float time)
    {
        float vh = MathF.Sqrt(vel.X * vel.X + vel.Z * vel.Z);
        float len = vh * time;
        int n = (int)(len / Step);
        int last = n - (int)MathF.Ceiling(ArcEndSkip / Step);
        for (int k = 1; k < last; k++)
        {
            float tk = time * (k / (float)n);
            float y = from.Y + vel.Y * tk - 0.5f * Ballistics.Gravity * tk * tk;
            if (t.GetSurfaceHeight(new Vector2(from.X + vel.X * tk, from.Z + vel.Z * tk)) > y + Tolerance) return false;
        }
        return true;
    }

    /// <summary>Horizontal distance before the aim point that <see cref="IsArcClear"/> does not test.</summary>
    public const float ArcEndSkip = 1.5f;

    /// <summary>True if the straight segment a→b stays above the terrain/water surface (minus the tolerance). The
    /// first and last step are not tested (the shooter's and the target's own ground).</summary>
    public static bool IsClear(Terrain t, Vector3 a, Vector3 b)
    {
        var d = b - a;
        float len = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
        int n = (int)(len / Step);
        for (int k = 1; k < n; k++)
        {
            var q = a + d * (k / (float)n);
            if (t.GetSurfaceHeight(new Vector2(q.X, q.Z)) > q.Y + Tolerance) return false;
        }
        return true;
    }
}

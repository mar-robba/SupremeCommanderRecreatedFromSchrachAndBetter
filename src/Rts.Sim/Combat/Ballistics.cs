using System.Numerics;

namespace Rts.Sim.Combat;

/// <summary>Projectile math: ballistic arcs, target leading, segment/sphere tests. Pure functions.</summary>
public static class Ballistics
{
    /// <summary>Gravity for shells and bombs (world units / s^2).</summary>
    public const float Gravity = 9.8f;

    /// <summary>
    /// Launch velocity to hit <paramref name="to"/> from <paramref name="from"/> at <paramref name="speed"/> under
    /// gravity g (y up). <paramref name="high"/> selects the high (lob) arc. Returns false if the point is out of
    /// reach (then aims at 45°, the max-range angle). <paramref name="time"/> = flight time.
    /// </summary>
    public static bool SolveArc(Vector3 from, Vector3 to, float speed, float g, bool high, out Vector3 velocity, out float time)
    {
        float dx = to.X - from.X, dz = to.Z - from.Z, dy = to.Y - from.Y;
        float d = MathF.Sqrt(dx * dx + dz * dz);
        if (d < 1e-3f)
        {
            velocity = new Vector3(0, speed, 0);
            time = 2f * speed / g;
            return true;
        }
        float v2 = speed * speed;
        float disc = v2 * v2 - g * (g * d * d + 2f * dy * v2);
        bool ok = disc >= 0;
        float tan = ok ? (v2 + (high ? 1f : -1f) * MathF.Sqrt(disc)) / (g * d) : 1f;
        float cos = 1f / MathF.Sqrt(1f + tan * tan);
        float sin = tan * cos;
        float hx = dx / d, hz = dz / d;
        velocity = new Vector3(hx * speed * cos, speed * sin, hz * speed * cos);
        time = d / (speed * cos);
        return ok;
    }

    /// <summary>Max heading change assumed by <see cref="PredictPosition"/> before continuing straight.</summary>
    public const float MaxPredictedTurn = MathF.PI * 0.75f;

    /// <summary>
    /// Position after <paramref name="t"/> seconds of a unit moving at constant speed with velocity
    /// <paramref name="vel"/> while turning at <paramref name="omega"/> rad/s (heading convention: positive = toward
    /// increasing heading). The turn is capped at <see cref="MaxPredictedTurn"/>, after which motion continues straight.
    /// Circling aircraft are therefore predicted on their circle instead of on its tangent.
    /// </summary>
    public static Vector2 PredictPosition(Vector2 pos, Vector2 vel, float omega, float t)
    {
        if (t <= 0) return pos;
        if (MathF.Abs(omega) < 0.02f) return pos + vel * t;
        float a = omega * t;
        float rest = 0;
        if (MathF.Abs(a) > MaxPredictedTurn)
        {
            float capped = MathF.CopySign(MaxPredictedTurn, a);
            rest = t - capped / omega;
            a = capped;
        }
        float sn = MathF.Sin(a), oc = 1f - MathF.Cos(a);
        // Integral of R(omega*tau) * vel over [0, a/omega].
        var disp = new Vector2(vel.X * sn - vel.Y * oc, vel.X * oc + vel.Y * sn) / omega;
        if (rest > 0)
        {
            float cs = 1f - oc;
            disp += new Vector2(vel.X * cs - vel.Y * sn, vel.X * sn + vel.Y * cs) * rest;
        }
        return pos + disp;
    }

    /// <summary>
    /// Intercept point for a straight projectile of the given speed fired from muzzle at a target with ground
    /// velocity <paramref name="targetVel"/> turning at <paramref name="omega"/> rad/s (fixed-point iterations;
    /// converges quickly because projectiles are much faster than units). <paramref name="time"/> = flight time.
    /// </summary>
    public static Vector3 LeadStraight(Vector3 muzzle, Vector3 target, Vector2 targetVel, float omega, float speed, out float time)
    {
        float t = Vector3.Distance(target, muzzle) / speed;
        var aim = target;
        var p0 = new Vector2(target.X, target.Z);
        for (int it = 0; it < 3; it++)
        {
            var p = PredictPosition(p0, targetVel, omega, MathF.Min(t, 6f));
            aim = new Vector3(p.X, target.Y, p.Y);
            t = Vector3.Distance(aim, muzzle) / speed;
        }
        time = t;
        return aim;
    }

    /// <summary>
    /// Parameter in [0,1] where segment a→b first enters the sphere (c, r) (0 if a is inside), or -1 if it misses.
    /// </summary>
    public static float SegmentSphere(Vector3 a, Vector3 b, Vector3 c, float r)
    {
        var d = b - a;
        var f = a - c;
        float cc = Vector3.Dot(f, f) - r * r;
        if (cc <= 0) return 0;
        float aa = Vector3.Dot(d, d);
        if (aa < 1e-9f) return -1;
        float bb = 2f * Vector3.Dot(f, d);
        float disc = bb * bb - 4f * aa * cc;
        if (disc < 0) return -1;
        float t = (-bb - MathF.Sqrt(disc)) / (2f * aa);
        return t >= 0 && t <= 1 ? t : -1;
    }

    /// <summary>Parameter in [0,1] of the point of segment a→b closest to c.</summary>
    public static float ClosestParam(Vector3 a, Vector3 b, Vector3 c)
    {
        var d = b - a;
        float aa = Vector3.Dot(d, d);
        if (aa < 1e-9f) return 0;
        float t = Vector3.Dot(c - a, d) / aa;
        return t < 0 ? 0 : t > 1 ? 1 : t;
    }

    /// <summary>Rotates velocity toward the direction <paramref name="toTarget"/> by at most maxAngle radians, keeping speed.</summary>
    public static Vector3 Steer(Vector3 vel, Vector3 toTarget, float cosMax, float sinMax)
    {
        float speed = vel.Length();
        float dl = toTarget.Length();
        if (speed < 1e-4f || dl < 1e-4f) return vel;
        var cur = vel / speed;
        var des = toTarget / dl;
        float cos = Vector3.Dot(cur, des);
        if (cos >= cosMax) return des * speed;
        var perp = des - cur * cos;
        float pl = perp.Length();
        if (pl < 1e-5f)
        {
            // Target exactly behind: turn upward (deterministic choice).
            perp = Vector3.UnitY - cur * cur.Y;
            pl = perp.Length();
            if (pl < 1e-5f) { perp = Vector3.UnitX; pl = 1f; }
        }
        perp /= pl;
        return (cur * cosMax + perp * sinMax) * speed;
    }
}

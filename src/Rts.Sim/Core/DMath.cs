using System.Numerics;

namespace Rts.Sim.Core;

/// <summary>
/// Math helpers used by the simulation. All trig goes through here so it can later be replaced by a
/// cross-platform deterministic implementation (needed for lockstep multiplayer).
/// Convention: angles in radians; heading h points along (cos h, sin h) in the (x, z) plane.
/// </summary>
public static class DMath
{
    public const float Pi = MathF.PI;
    public const float TwoPi = MathF.PI * 2f;
    public const float Deg2Rad = MathF.PI / 180f;

    public static float Sin(float a) => MathF.Sin(a);
    public static float Cos(float a) => MathF.Cos(a);
    public static float Atan2(float y, float x) => MathF.Atan2(y, x);
    public static float Sqrt(float v) => MathF.Sqrt(v);

    public static Vector2 Dir(float heading) => new(MathF.Cos(heading), MathF.Sin(heading));
    public static float HeadingOf(Vector2 v) => MathF.Atan2(v.Y, v.X);

    /// <summary>Wraps an angle into (-PI, PI].</summary>
    public static float WrapAngle(float a)
    {
        a %= TwoPi;
        if (a <= -Pi) a += TwoPi;
        else if (a > Pi) a -= TwoPi;
        return a;
    }

    /// <summary>Signed shortest difference to - from, in (-PI, PI].</summary>
    public static float AngleDiff(float from, float to) => WrapAngle(to - from);

    public static float RotateTowards(float from, float to, float maxStep)
    {
        float d = AngleDiff(from, to);
        if (MathF.Abs(d) <= maxStep) return WrapAngle(to);
        return WrapAngle(from + MathF.Sign(d) * maxStep);
    }

    public static Vector2 ClampLength(Vector2 v, float max)
    {
        float l2 = v.LengthSquared();
        return l2 > max * max ? v * (max / MathF.Sqrt(l2)) : v;
    }

    public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

    public static Vector3 To3(Vector2 p, float y) => new(p.X, y, p.Y);
}

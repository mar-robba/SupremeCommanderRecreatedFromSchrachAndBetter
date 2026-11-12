using System.Numerics;

namespace Rts.Sim.Core;

/// <summary>
/// Math helpers used by the simulation. Every transcendental function of the sim goes through here and is computed
/// in software (DMath.Trig/Inverse/ExpLog.cs: float + - * / and Sqrt only), so it gives the same bits on every OS,
/// CPU and .NET runtime (lockstep multiplayer, replays). Never call MathF/Math Sin/Cos/Atan2/Pow/... in Rts.Sim
/// (DeterministicMathTests greps for it). Docs: ARCHITECTURE "Deterministic math (cross-OS)".
/// Convention: angles in radians; heading h points along (cos h, sin h) in the (x, z) plane.
/// </summary>
public static partial class DMath
{
    public const float Pi = MathF.PI;
    public const float TwoPi = MathF.PI * 2f;
    public const float Deg2Rad = MathF.PI / 180f;

    /// <summary>Version of the sim's math (bump when any DMath result changes): stored in replays, which only replay
    /// with the same version (1 = software math of 10a-1; 0 = files from before, which used MathF).</summary>
    public const int Version = 1;

    public static float Sqrt(float v) => MathF.Sqrt(v);

    public static Vector2 Dir(float heading)
    {
        var (s, c) = SinCos(heading);
        return new Vector2(c, s);
    }
    public static float HeadingOf(Vector2 v) => Atan2(v.Y, v.X);

    /// <summary>a + (b - a)·t, written out (no Vector Lerp: those may use a fused multiply-add on some CPUs).</summary>
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => new(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t));
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t), Lerp(a.Z, b.Z, t));

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

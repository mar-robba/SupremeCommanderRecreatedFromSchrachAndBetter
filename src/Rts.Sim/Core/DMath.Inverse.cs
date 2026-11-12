namespace Rts.Sim.Core;

// (10a-1) Deterministic inverse trig (same rules as DMath.Trig.cs: float + - * / and Sqrt only).
public static partial class DMath
{
    private const float Tan3PiO8 = 2.414213562373095f, TanPiO8 = 0.4142135623730950f;
    // atan on [-tan(π/8), tan(π/8)] (Cephes atanf).
    private const float A1 = -3.33329491539e-1f, A2 = 1.99777106478e-1f, A3 = -1.38776856032e-1f, A4 = 8.05374449538e-2f;
    // asin on [-1/2, 1/2] (Cephes asinf).
    private const float As1 = 1.6666752422e-1f, As2 = 7.4953002686e-2f, As3 = 4.5470025998e-2f, As4 = 2.4181311049e-2f, As5 = 4.2163199048e-2f;

    private static bool SignBit(float x) => BitConverter.SingleToInt32Bits(x) < 0;

    /// <summary>atan(t) for t ≥ 0 (or +∞).</summary>
    private static float AtanPos(float t) { float hi = AtanPos(t, out float rest); return hi + rest; }

    /// <summary>atan(t) = hi + rest for t ≥ 0 (hi = 0, π/4 or π/2 as float; rest keeps their low bits).</summary>
    private static float AtanPos(float t, out float rest)
    {
        float hi = 0f, lo = 0f;
        if (t > Tan3PiO8) { hi = PiO2Hi; lo = PiO2Lo; t = -1f / t; }
        else if (t > TanPiO8) { hi = PiO4Hi; lo = PiO4Lo; t = (t - 1f) / (t + 1f); }
        float z = t * t;
        float p = (((A4 * z + A3) * z + A2) * z + A1) * z * t;
        rest = (p + lo) + t;
        return hi;
    }

    /// <summary>Deterministic arc tangent, in [-π/2, π/2]. NaN → NaN.</summary>
    public static float Atan(float x)
    {
        if (float.IsNaN(x)) return float.NaN;
        float a = AtanPos(MathF.Abs(x));
        return SignBit(x) ? -a : a;
    }

    /// <summary>Deterministic atan2 (angle of the vector (x, y)), in [-π, π], with the C/IEEE special cases
    /// (signed zeros, infinities); NaN in → NaN.</summary>
    public static float Atan2(float y, float x)
    {
        if (float.IsNaN(x) || float.IsNaN(y)) return float.NaN;
        float ax = MathF.Abs(x), ay = MathF.Abs(y);
        bool xneg = SignBit(x);
        float a;
        if (ay == 0f) a = xneg ? PiHi : 0f;
        else if (ax == 0f) a = PiO2Hi;
        else if (float.IsInfinity(ax))
            a = float.IsInfinity(ay) ? (xneg ? 3f * PiO4Hi : PiO4Hi) : (xneg ? PiHi : 0f);
        else if (float.IsInfinity(ay)) a = PiO2Hi;
        else
        {
            float hi = AtanPos(ay / ax, out float rest);
            a = xneg ? (PiHi - hi) + (PiLo - rest) : hi + rest;
        }
        return SignBit(y) ? -a : a;
    }

    /// <summary>asin(t) for 0 ≤ t ≤ 1/2.</summary>
    private static float AsinSmall(float t)
    {
        float z = t * t;
        return ((((As5 * z + As4) * z + As3) * z + As2) * z + As1) * z * t + t;
    }

    /// <summary>Deterministic arc sine, in [-π/2, π/2]. |x| &gt; 1 or NaN → NaN.</summary>
    public static float Asin(float x)
    {
        float ax = MathF.Abs(x);
        if (!(ax <= 1f)) return float.NaN;
        float a;
        if (ax > 0.5f)
        {
            float s = MathF.Sqrt(0.5f * (1f - ax));
            a = PiO2Hi - (2f * AsinSmall(s) - PiO2Lo);
        }
        else a = AsinSmall(ax);
        return SignBit(x) ? -a : a;
    }

    /// <summary>Deterministic arc cosine, in [0, π]. |x| &gt; 1 or NaN → NaN.</summary>
    public static float Acos(float x)
    {
        if (!(MathF.Abs(x) <= 1f)) return float.NaN;
        if (x < -0.5f) return (PiHi - 2f * AsinSmall(MathF.Sqrt(0.5f * (1f + x)))) + PiLo;
        if (x > 0.5f) return 2f * AsinSmall(MathF.Sqrt(0.5f * (1f - x)));
        float a = AsinSmall(MathF.Abs(x));
        return SignBit(x) ? PiO2Hi + (a + PiO2Lo) : PiO2Hi - (a - PiO2Lo);
    }
}

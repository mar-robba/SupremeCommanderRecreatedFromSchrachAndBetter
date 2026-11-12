using System.Runtime.CompilerServices;

namespace Rts.Sim.Core;

// (10a-1) Deterministic exp/log/pow (same rules as DMath.Trig.cs: float + - * / and Sqrt only; extra precision
// comes from exact float tricks: Cody-Waite splits, Fast2Sum and Dekker's TwoProduct with a Veltkamp split).
public static partial class DMath
{
    // ln 2 with a 12-bit head (n · Ln2C1 exact for |n| < 2^12) and as hi + lo; log2(e) as hi + lo.
    private const float Ln2C1 = 0.693115234375f;              // 0x3F317000
    private const float Ln2C2 = 3.1946183298714459e-5f;       // 0x3805FDF4
    private const float Ln2Hi = 0.69314718246459961f;         // 0x3F317218
    private const float Ln2Lo = -1.9046542121259336e-9f;      // 0xB102E308
    private const float Log2EHi = 1.4426950216293335f;        // 0x3FB8AA3B
    private const float Log2ELo = 1.9259630335000111e-8f;     // 0x32A57060
    private const float SqrtHalf = 0.70710678118654752f;
    private const float ExpMax = 88.72283935546875f;          // above: +∞
    private const float ExpMin = -103.97208404541015625f;     // below: +0

    // e^r on |r| ≤ ln2/2 (Cephes expf): 1 + r + r²·P(r).
    private const float E1 = 5.0000001201e-1f, E2 = 1.6666665459e-1f, E3 = 4.1665795894e-2f,
        E4 = 8.3334519073e-3f, E5 = 1.3981999507e-3f, E6 = 1.9875691500e-4f;
    // ln(1+f) - f + f²/2 = f³·Q(f) on [√½-1, √2-1] (Cephes logf).
    private const float L1 = 3.3333331174e-1f, L2 = -2.4999993993e-1f, L3 = 2.0000714765e-1f, L4 = -1.6668057665e-1f,
        L5 = 1.4249322787e-1f, L6 = -1.2420140846e-1f, L7 = 1.1676998740e-1f, L8 = -1.1514610310e-1f, L9 = 7.0376836292e-2f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float KExp(float r)
    {
        float z = r * r;
        float p = ((((E6 * r + E5) * r + E4) * r + E3) * r + E2) * r + E1;
        return (p * z + r) + 1f;
    }

    /// <summary>v · 2^n for v near 1 and any n (one rounding, also into the denormal range; overflow → ±∞).</summary>
    internal static float ScaleB(float v, int n)
    {
        if (n > 127) { v *= Pow2(127); n -= 127; if (n > 127) { v *= Pow2(127); n -= 127; if (n > 127) n = 127; } }
        else if (n < -126) { v *= Pow2(-126 + 24); n += 126 - 24; if (n < -126) { v *= Pow2(-126 + 24); n += 126 - 24; if (n < -126) n = -126; } }
        return v * Pow2(n);
    }

    /// <summary>Deterministic e^x. NaN → NaN, overflow → +∞.</summary>
    public static float Exp(float x)
    {
        if (float.IsNaN(x)) return float.NaN;
        if (x > ExpMax) return float.PositiveInfinity;
        if (x < ExpMin) return 0f;
        float k = MathF.Round(x * Log2EHi);
        float r = (x - k * Ln2C1) - k * Ln2C2;
        return ScaleB(KExp(r), (int)k);
    }

    /// <summary>x = m·2^e with m in [√½, √2); returns f = m - 1 (exact). x must be finite and &gt; 0.</summary>
    private static float LogReduce(float x, out int e)
    {
        e = 0;
        if (x < 1.17549435e-38f) { x *= Pow2(25); e = -25; } // denormal: normalize (exact)
        uint b = BitConverter.SingleToUInt32Bits(x);
        e += (int)(b >> 23) - 126;
        float m = BitConverter.UInt32BitsToSingle((b & 0x7FFFFFu) | 0x3F000000u); // [0.5, 1)
        if (m < SqrtHalf) { e--; return m + m - 1f; }
        return m - 1f;
    }

    /// <summary>ln(1+f) - f for f from <see cref="LogReduce"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float LogTail(float f)
    {
        float z = f * f;
        return LogPoly(f) * f * z - 0.5f * z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float LogPoly(float f) =>
        (((((((L9 * f + L8) * f + L7) * f + L6) * f + L5) * f + L4) * f + L3) * f + L2) * f + L1;

    /// <summary>Deterministic natural logarithm. x &lt; 0 or NaN → NaN, ±0 → -∞, +∞ → +∞.</summary>
    public static float Log(float x)
    {
        if (!(x > 0f)) return x == 0f ? float.NegativeInfinity : float.NaN;
        if (float.IsPositiveInfinity(x)) return x;
        float f = LogReduce(x, out int e);
        float fe = e;
        float y = LogTail(f) + Ln2C2 * fe;
        return (f + y) + Ln2C1 * fe;
    }

    /// <summary>Deterministic base-2 logarithm (exact for powers of two). Same special cases as <see cref="Log"/>.</summary>
    public static float Log2(float x)
    {
        if (!(x > 0f)) return x == 0f ? float.NegativeInfinity : float.NaN;
        if (float.IsPositiveInfinity(x)) return x;
        float f = LogReduce(x, out int e);
        float t = LogTail(f);
        return e + ((t * Log2EHi + f * Log2ELo) + f * Log2EHi);
    }

    /// <summary>Deterministic x^y with the C/IEEE special cases (pow(x, ±0) = 1, pow(1, y) = 1, negative x only for
    /// integer y, ...). y·log2(x) is carried in float hi + lo, so the error stays a few ulp while |y·log2 x| is
    /// moderate (it grows slowly beyond, see the tests).</summary>
    public static float Pow(float x, float y)
    {
        if (y == 0f || x == 1f) return 1f;
        if (float.IsNaN(x) || float.IsNaN(y)) return float.NaN;
        float ay = MathF.Abs(y), ax = MathF.Abs(x);
        bool yInt = MathF.Floor(y) == y;
        bool yOdd = yInt && ay < 16777216f && ((long)ay & 1) == 1;
        if (float.IsInfinity(y))
        {
            if (ax == 1f) return 1f;
            return (ax < 1f) == (y < 0f) ? float.PositiveInfinity : 0f;
        }
        bool neg = SignBit(x);
        float sign = neg && yOdd ? -1f : 1f;
        if (ax == 0f) return y < 0f ? sign * float.PositiveInfinity : sign * 0f;
        if (neg && !yInt && float.IsFinite(x)) return float.NaN;
        if (ax == 1f) return sign; // (-1)^integer
        if (float.IsInfinity(ax)) return y < 0f ? sign * 0f : sign * float.PositiveInfinity;
        // |y| > 2^64 with x ≠ 1: |y·log2 x| > 2^40, the result is 0 or ∞.
        if (ay > 1.8446744e19f) return (ax < 1f) == (y < 0f) ? float.PositiveInfinity : 0f;

        // log2|x| = e + (f + t)·log2(e) as hi + lo (Fast2Sum / TwoProduct keep the low bits).
        float f = LogReduce(ax, out int e);
        // ln m = f - f²/2 + f³·Q(f): f² exactly as z + zErr, so the big -f²/2 term carries no rounding error.
        float z = f * f, zErr = TwoProdErr(f, f, z);
        float h = -0.5f * z;
        float s0 = f + h, e0 = h - (s0 - f);                // Fast2Sum: |f| ≥ |h|
        float c = LogPoly(f) * f * z;
        float s1 = s0 + c, e1 = (c - (s1 - s0)) + (e0 - 0.5f * zErr); // Fast2Sum: |s0| ≥ |c|; ln m = s1 + e1
        float u = s1 * Log2EHi, uErr = TwoProdErr(s1, Log2EHi, u);
        float lHi = e + u;                                  // |e| ≥ 1 > |u| or e = 0: Fast2Sum is exact
        float lLo = (u - (lHi - e)) + (uErr + (s1 * Log2ELo + e1 * Log2EHi));
        float pHi = y * lHi, pErr = TwoProdErr(y, lHi, pHi);
        if (pHi > 129f) return sign * float.PositiveInfinity;
        if (pHi < -152f) return sign * 0f;
        float n = MathF.Round(pHi);
        float g = (pHi - n) + (pErr + y * lLo);             // pHi - n is exact; |g| ≲ 1/2
        float r = g * Ln2Hi + g * Ln2Lo;
        return sign * ScaleB(KExp(r), (int)n);
    }

    /// <summary>Exact error of the float product a·b = p (Dekker TwoProduct, Veltkamp split; no FMA).</summary>
    private static float TwoProdErr(float a, float b, float p)
    {
        const float Split = 4097f; // 2^12 + 1
        float ca = Split * a, ah = ca - (ca - a), al = a - ah;
        float cb = Split * b, bh = cb - (cb - b), bl = b - bh;
        return ((ah * bh - p) + ah * bl + al * bh) + al * bl;
    }
}

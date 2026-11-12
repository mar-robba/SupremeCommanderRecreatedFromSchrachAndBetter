using System.Runtime.CompilerServices;

namespace Rts.Sim.Core;

// (10a-1) Deterministic sine/cosine: the same bits on every OS, CPU and .NET runtime.
// Only IEEE-754 float + - * /, exact operations (Round, Abs, sign/bit manipulation, integer math) and constants
// written in this file; no MathF/Math transcendental, no FMA, no double. Docs: ARCHITECTURE "Deterministic math".
public static partial class DMath
{
    // π/2 in four parts. The first three have 12 significant bits, so k * part is exact for |k| < 2^12 (Cody-Waite).
    private const float PiO2_1 = 1.5703125f;                 // 0x3FC90000
    private const float PiO2_2 = 4.8375129699707031e-4f;     // 0x39FDA000
    private const float PiO2_3 = 7.5495336204767227e-8f;     // 0x33A22000
    private const float PiO2_4 = 2.5633440682570896e-12f;    // 0x2C34611A
    // π/2, π/4, π as float hi + lo (hi = the rounded float, lo = the next 24 bits).
    internal const float PiO2Hi = 1.5707963705062866f;       // 0x3FC90FDB
    internal const float PiO2Lo = -4.3711388286737929e-8f;   // 0xB33BBD2E
    internal const float PiO4Hi = 0.78539818525314331f;      // 0x3F490FDB
    internal const float PiO4Lo = -2.1855694143368964e-8f;   // 0xB2BBBD2E
    internal const float PiHi = 3.1415927410125732f;         // 0x40490FDB
    internal const float PiLo = -8.7422776573475858e-8f;     // 0xB3BBBD2E
    private const float TwoOverPi = 0.63661974668502808f;    // 0x3F22F983

    /// <summary>Below this |x| the Cody-Waite reduction is exact (|k| = |round(x·2/π)| &lt; 2^12); above, Payne-Hanek.</summary>
    private const float CodyWaiteLimit = 6000f;
    /// <summary>2^-12: below it sin x = x and cos x = 1 to float precision.</summary>
    private const float TinyAngle = 2.44140625e-4f;

    /// <summary>Bits of 2/π: word i holds the fraction bits 32i+1 .. 32i+32 (most significant first). Generated
    /// with exact integer arithmetic (Machin's formula), never by the runtime.</summary>
    private static ReadOnlySpan<uint> TwoOverPiBits =>
    [
        0xA2F9836E, 0x4E441529, 0xFC2757D1, 0xF534DDC0, 0xDB629599, 0x3C439041, 0xFE5163AB, 0xDEBBC561, 0xB7246E3A, 0x424DD2E0,
    ];

    // Minimax polynomials on [-π/4, π/4] (Cephes sinf/cosf).
    private const float S1 = -1.6666654611e-1f, S2 = 8.3321608736e-3f, S3 = -1.9515295891e-4f;
    private const float C1 = 4.166664568298827e-2f, C2 = -1.388731625493765e-3f, C3 = 2.443315711809948e-5f;

    /// <summary>Deterministic sine (radians). NaN or ±∞ → NaN.</summary>
    public static float Sin(float x)
    {
        if (MathF.Abs(x) < TinyAngle) return x; // sin x = x in float (keeps -0 and denormals)
        float r = Reduce(x, out int q);
        if (float.IsNaN(r)) return float.NaN;
        return (q & 3) switch
        {
            0 => KSin(r),
            1 => KCos(r),
            2 => -KSin(r),
            _ => -KCos(r),
        };
    }

    /// <summary>Deterministic cosine (radians). NaN or ±∞ → NaN.</summary>
    public static float Cos(float x)
    {
        float r = Reduce(x, out int q);
        if (float.IsNaN(r)) return float.NaN;
        return (q & 3) switch
        {
            0 => KCos(r),
            1 => -KSin(r),
            2 => -KCos(r),
            _ => KSin(r),
        };
    }

    /// <summary>Sine and cosine with one range reduction (same bits as <see cref="Sin"/> and <see cref="Cos"/>).</summary>
    public static (float Sin, float Cos) SinCos(float x)
    {
        if (MathF.Abs(x) < TinyAngle) return (x, 1f);
        float r = Reduce(x, out int q);
        if (float.IsNaN(r)) return (float.NaN, float.NaN);
        float s = KSin(r), c = KCos(r);
        return (q & 3) switch
        {
            0 => (s, c),
            1 => (c, -s),
            2 => (-s, -c),
            _ => (-c, s),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float KSin(float r)
    {
        float z = r * r;
        return r + ((S3 * z + S2) * z + S1) * z * r;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float KCos(float r)
    {
        float z = r * r;
        return 1f - (0.5f * z - ((C3 * z + C2) * z + C1) * z * z);
    }

    /// <summary>x = q·π/2 + r with |r| ≲ π/4. Returns NaN for NaN/±∞.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Reduce(float x, out int q)
    {
        float ax = MathF.Abs(x);
        if (ax <= PiO4Hi) { q = 0; return x; }
        if (ax < CodyWaiteLimit)
        {
            float k = MathF.Round(x * TwoOverPi);
            q = (int)k;
            return (((x - k * PiO2_1) - k * PiO2_2) - k * PiO2_3) - k * PiO2_4;
        }
        if (!float.IsFinite(x)) { q = 0; return float.NaN; }
        float r = ReduceLarge(ax, out q);
        if (x < 0) { r = -r; q = -q; }
        return r;
    }

    /// <summary>Payne-Hanek reduction of a finite |x| ≥ <see cref="CodyWaiteLimit"/> with integer arithmetic:
    /// x·2/π mod 4 as fixed point (≥ 64 fraction bits), then the fraction times π/2 in float hi + lo.</summary>
    private static float ReduceLarge(float ax, out int q)
    {
        uint b = BitConverter.SingleToUInt32Bits(ax);
        int e = (int)(b >> 23) - 150;                     // ax = m · 2^e, m a 24-bit integer
        ulong m = (b & 0x7FFFFFu) | 0x800000u;
        int j0 = Math.Max(1, e - 1);                      // earlier 2/π bits only add multiples of 4
        var bits = TwoOverPiBits;
        int w = j0 - 1, idx = w >> 5, sh = w & 31;
        UInt128 v = ((UInt128)bits[idx] << 96) | ((UInt128)bits[idx + 1] << 64) | ((UInt128)bits[idx + 2] << 32) | bits[idx + 3];
        UInt128 window = (v << sh) >> 32;                 // 96 bits of 2/π starting at bit j0
        UInt128 prod = window * m;                        // < 2^120
        int p = j0 + 95 - e;                              // binary point of prod (94..107)
        q = (int)((ulong)(prod >> p) & 3);
        ulong frac = (ulong)((prod << (128 - p)) >> 64);  // 64 bits below the point
        bool neg = frac >= 0x8000000000000000ul;
        if (neg) { q++; frac = 0ul - frac; }              // round to the nearest quadrant: fraction in [-1/2, 1/2]
        if (frac == 0) return 0f;
        int lz = System.Numerics.BitOperations.LeadingZeroCount(frac);
        ulong n = frac << lz;
        // Exact integer → float conversions (< 2^24), then power-of-two scaling: no runtime-dependent rounding.
        // fraction = n · 2^(-64-lz) = hiInt · 2^(-24-lz) + loInt · 2^(-48-lz) + (dropped bits < 2^(-48-lz))
        float hi = (int)(n >> 40) * Pow2(-24 - lz);
        float lo = (int)((n >> 16) & 0xFFFFFF) * Pow2(-48 - lz);
        float r = hi * PiO2Hi + (hi * PiO2Lo + lo * PiO2Hi);
        return neg ? -r : r;
    }

    /// <summary>2^n as a float for -126 ≤ n ≤ 127 (built from its bits).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Pow2(int n) => BitConverter.Int32BitsToSingle((n + 127) << 23);
}

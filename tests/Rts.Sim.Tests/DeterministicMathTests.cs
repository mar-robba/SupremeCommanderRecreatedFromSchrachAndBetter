using System.Numerics;
using System.Text.RegularExpressions;
using Rts.Sim.Core;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>(10a-1) The software math of <see cref="DMath"/>: accuracy against double <c>Math</c>, golden bit patterns
/// (the same on every OS/CPU), and a source grep that keeps MathF/Math transcendentals out of Rts.Sim.</summary>
public class DeterministicMathTests
{
    private readonly ITestOutputHelper _out;
    public DeterministicMathTests(ITestOutputHelper output) => _out = output;

    /// <summary>Distance in representable floats (0 = same bits; +0 and -0 count as equal).</summary>
    internal static long Ulps(float a, float b)
    {
        if (float.IsNaN(a) || float.IsNaN(b)) return float.IsNaN(a) && float.IsNaN(b) ? 0 : long.MaxValue;
        static long Ord(float f) { int i = BitConverter.SingleToInt32Bits(f); return i < 0 ? -(long)(i & int.MaxValue) : i; }
        return Math.Abs(Ord(a) - Ord(b));
    }

    private static float RandomFloat(Random rng, int minExp, int maxExp)
    {
        int e = rng.Next(minExp, maxExp + 1) + 127;
        uint bits = ((uint)e << 23) | (uint)rng.Next(0, 1 << 23);
        if (rng.Next(2) == 0) bits |= 0x80000000u;
        return BitConverter.UInt32BitsToSingle(bits);
    }

    private long MaxUlps(string name, IEnumerable<float> xs, Func<float, float> f, Func<double, double> reference)
    {
        long worst = 0; float at = 0;
        foreach (float x in xs)
        {
            double rd = reference(x);
            float r = (float)rd;
            if (Math.Abs(rd) < 1.1754944e-38) continue; // denormal results: compared by the golden table only
            long u = Ulps(f(x), r);
            if (u > worst) { worst = u; at = x; }
        }
        _out.WriteLine($"{name}: max {worst} ulp (at {at:R})");
        return worst;
    }

    private static IEnumerable<float> Sweep(float lo, float hi, int n)
    {
        for (int i = 0; i <= n; i++) yield return lo + (hi - lo) * (i / (float)n);
    }

    private static IEnumerable<float> Randoms(int seed, int n, int minExp, int maxExp)
    {
        var rng = new Random(seed);
        for (int i = 0; i < n; i++) yield return RandomFloat(rng, minExp, maxExp);
    }

    [Fact]
    public void TrigIsAccurateToAFewUlp()
    {
        IEnumerable<float> Angles() => Sweep(-20f, 20f, 400_000).Concat(Sweep(-6000f, 6000f, 200_000))
            .Concat(Randoms(1, 200_000, -30, 12)).Concat(Randoms(2, 50_000, 12, 127));
        Assert.True(MaxUlps("Sin", Angles(), DMath.Sin, Math.Sin) <= 3);
        Assert.True(MaxUlps("Cos", Angles(), DMath.Cos, Math.Cos) <= 3);
        foreach (float x in Sweep(-50f, 50f, 20_000).Concat(Randoms(3, 20_000, -30, 127)))
        {
            var (s, c) = DMath.SinCos(x);
            Assert.Equal(BitConverter.SingleToUInt32Bits(DMath.Sin(x)), BitConverter.SingleToUInt32Bits(s));
            Assert.Equal(BitConverter.SingleToUInt32Bits(DMath.Cos(x)), BitConverter.SingleToUInt32Bits(c));
        }
    }

    [Fact]
    public void InverseTrigIsAccurateToAFewUlp()
    {
        Assert.True(MaxUlps("Atan", Randoms(4, 300_000, -40, 40).Concat(Sweep(-5f, 5f, 200_000)), DMath.Atan, Math.Atan) <= 3);
        Assert.True(MaxUlps("Asin", Sweep(-1f, 1f, 400_000).Concat(Randoms(5, 50_000, -40, -1)), DMath.Asin, Math.Asin) <= 3);
        Assert.True(MaxUlps("Acos", Sweep(-1f, 1f, 400_000).Concat(Randoms(6, 50_000, -40, -1)), DMath.Acos, Math.Acos) <= 2);
        var rng = new Random(7);
        long worst = 0;
        for (int i = 0; i < 300_000; i++)
        {
            float y = i % 2 == 0 ? RandomFloat(rng, -20, 20) : (float)(rng.NextDouble() * 400 - 200);
            float x = i % 3 == 0 ? RandomFloat(rng, -20, 20) : (float)(rng.NextDouble() * 400 - 200);
            double rd = Math.Atan2(y, x);
            if (Math.Abs(rd) < 1.1754944e-38) continue;
            worst = Math.Max(worst, Ulps(DMath.Atan2(y, x), (float)rd));
        }
        _out.WriteLine($"Atan2: max {worst} ulp");
        Assert.True(worst <= 3);
    }

    [Fact]
    public void ExpLogPowAreAccurateToAFewUlp()
    {
        Assert.True(MaxUlps("Exp", Sweep(-103f, 88.7f, 400_000).Concat(Randoms(8, 100_000, -40, 6)), DMath.Exp, Math.Exp) <= 2);
        var positives = Randoms(9, 300_000, -126, 127).Select(MathF.Abs).Concat(Sweep(0.5f, 2f, 200_000)).ToList();
        Assert.True(MaxUlps("Log", positives, DMath.Log, Math.Log) <= 2);
        Assert.True(MaxUlps("Log2", positives, DMath.Log2, Math.Log2) <= 3);
        var rng = new Random(10);
        long worst = 0, worstWide = 0;
        float wx = 0, wy = 0;
        for (int i = 0; i < 400_000; i++)
        {
            float x = i % 2 == 0 ? (float)(rng.NextDouble() * 10) : MathF.Abs(RandomFloat(rng, -20, 20));
            float y = i % 3 == 0 ? (float)(rng.NextDouble() * 20 - 10) : RandomFloat(rng, -10, 6);
            double rd = Math.Pow(x, y);
            if (Math.Abs(rd) < 1.1754944e-38 || Math.Abs(rd) > 3.4e38) continue;
            long u = Ulps(DMath.Pow(x, y), (float)rd);
            if (Math.Abs(y * Math.Log2(x)) <= 16) { if (u > worst) { worst = u; wx = x; wy = y; } }
            else worstWide = Math.Max(worstWide, u);
        }
        _out.WriteLine($"Pow: max {worst} ulp for |y·log2 x| <= 16 (at {wx:R}^{wy:R}), {worstWide} beyond");
        Assert.True(worst <= 2);
        Assert.True(worstWide <= 5);
    }

    internal static readonly float[] GoldenInputs =
    [
        0f, -0f, 1e-40f, -1e-40f, 1e-30f, 2.44e-4f, 0.1f, -0.5f, 0.7853982f, 1f, -1f, 1.5707964f, 2f, 3.1415927f, -3.1415927f,
        4.712389f, 6.2831855f, -6.2831855f, 10f, 88.7f, -100f, 1000f, 5999.9f, 6000.1f, -12345.678f, 1e6f, 16777216f, 1e20f,
        -1e30f, 3.4028235e38f, float.PositiveInfinity, float.NegativeInfinity, float.NaN, 0.3f, 0.99999994f, 1.0000001f, -0.75f,
    ];

    /// <summary>Expected bits per input bits: x → Sin, Cos, Atan, Asin, Acos, Exp, Log, Log2 (generated once on Linux
    /// x64; any other machine must give the very same bits — that is the cross-OS check).</summary>
    private static readonly (uint X, uint Sin, uint Cos, uint Atan, uint Asin, uint Acos, uint Exp, uint Log, uint Log2)[] Golden1 =
    [
(0x00000000, 0x00000000, 0x3F800000, 0x00000000, 0x00000000, 0x3FC90FDB, 0x3F800000, 0xFF800000, 0xFF800000),
        (0x80000000, 0x80000000, 0x3F800000, 0x80000000, 0x80000000, 0x3FC90FDB, 0x3F800000, 0xFF800000, 0xFF800000),
        (0x000116C2, 0x000116C2, 0x3F800000, 0x000116C2, 0x000116C2, 0x3FC90FDB, 0x3F800000, 0xC2B834F2, 0xC304E08C),
        (0x800116C2, 0x800116C2, 0x3F800000, 0x800116C2, 0x800116C2, 0x3FC90FDB, 0x3F800000, 0xFFC00000, 0xFFC00000),
        (0x0DA24260, 0x0DA24260, 0x3F800000, 0x0DA24260, 0x0DA24260, 0x3FC90FDB, 0x3F800000, 0xC28A27B5, 0xC2C750D1),
        (0x397FDA40, 0x397FDA40, 0x3F800000, 0x397FDA40, 0x397FDA40, 0x3FC907DC, 0x3F8007FF, 0xC10517EE, 0xC1400368),
        (0x3DCCCCCD, 0x3DCC7577, 0x3F7EB898, 0x3DCC1F15, 0x3DCD2494, 0x3FBC3D91, 0x3F8D763E, 0xC0135D8E, 0xC0549A78),
        (0xBF000000, 0xBEF57744, 0x3F60A940, 0xBEED6338, 0xBF060A92, 0x40060A92, 0x3F1B4598, 0xFFC00000, 0xFFC00000),
        (0x3F490FDB, 0x3F3504F3, 0x3F3504F3, 0x3F2A7026, 0x3F67413C, 0x3F2ADE79, 0x400C5EB4, 0xBE775CAC, 0xBEB26F18),
        (0x3F800000, 0x3F576AA4, 0x3F0A5140, 0x3F490FDB, 0x3FC90FDB, 0x00000000, 0x402DF854, 0x00000000, 0x00000000),
        (0xBF800000, 0xBF576AA4, 0x3F0A5140, 0xBF490FDB, 0xBFC90FDB, 0x40490FDB, 0x3EBC5AB2, 0xFFC00000, 0xFFC00000),
        (0x3FC90FDB, 0x3F800000, 0xB33BBD2E, 0x3F807F4C, 0xFFC00000, 0xFFC00000, 0x4099EF6F, 0x3EE735DA, 0x3F26C874),
        (0x40000000, 0x3F68C7B7, 0xBED51132, 0x3F8DB70D, 0xFFC00000, 0xFFC00000, 0x40EC7326, 0x3F317218, 0x3F800000),
        (0x40490FDB, 0xB3BBBD2E, 0xBF800000, 0x3FA19DC5, 0xFFC00000, 0xFFC00000, 0x41B92024, 0x3F928682, 0x3FD3643A),
        (0xC0490FDB, 0x33BBBD2E, 0xBF800000, 0xBFA19DC5, 0xFFC00000, 0xFFC00000, 0x3D310113, 0xFFC00000, 0xFFC00000),
        (0x4096CBE4, 0xBF800000, 0x324CDE2E, 0x3FAE4BEA, 0xFFC00000, 0xFFC00000, 0x42DEA2B4, 0x3FC66CCA, 0x400F2223),
        (0x40C90FDB, 0x343BBD2E, 0x3F800000, 0x3FB4DC0B, 0xFFC00000, 0xFFC00000, 0x4405DF79, 0x3FEB3F8E, 0x4029B21D),
        (0xC0C90FDB, 0xB43BBD2E, 0x3F800000, 0xBFB4DC0B, 0xFFC00000, 0xFFC00000, 0x3AF4C4F8, 0xFFC00000, 0xFFC00000),
        (0x41200000, 0xBF0B44F7, 0xBF56CD64, 0x3FBC4DE9, 0xFFC00000, 0xFFC00000, 0x46AC14EE, 0x40135D8E, 0x40549A78),
        (0x42B16666, 0x3F2BBF2C, 0x3F3DD6EC, 0x3FC79E72, 0xFFC00000, 0xFFC00000, 0x7F7A37FC, 0x408F8740, 0x40CF114E),
        (0xC2C80000, 0x3F01A12E, 0x3F5CC0EE, 0xBFC7C82F, 0xFFC00000, 0xFFC00000, 0x0000001B, 0xFFC00000, 0xFFC00000),
        (0x447A0000, 0x3F53AE60, 0x3F0FF813, 0x3FC8EF16, 0xFFC00000, 0xFFC00000, 0x7F800000, 0x40DD0C55, 0x411F73DA),
        (0x45BB7F33, 0xBF041279, 0x3F5B4D10, 0x3FC90A65, 0xFFC00000, 0xFFC00000, 0x7F800000, 0x410B3125, 0x4148CFC3),
        (0x45BB80CD, 0xBEABA5E7, 0x3F712F60, 0x3FC90A65, 0xFFC00000, 0xFFC00000, 0x7F800000, 0x410B3148, 0x4148CFF5),
        (0xC640E6B6, 0x3F344B08, 0x3F35BE20, 0xBFC90D33, 0xFFC00000, 0xFFC00000, 0x00000000, 0xFFC00000, 0xFFC00000),
        (0x49742400, 0xBEB3325A, 0x3F6FCEFD, 0x3FC90FD2, 0xFFC00000, 0xFFC00000, 0x7F800000, 0x415D0C55, 0x419F73DA),
        (0x4B800000, 0xBF47917C, 0x3F2056B4, 0x3FC90FDA, 0xFFC00000, 0xFFC00000, 0x7F800000, 0x41851592, 0x41C00000),
        (0x60AD78EC, 0x3F281568, 0x3F411724, 0x3FC90FDB, 0xFFC00000, 0xFFC00000, 0x7F800000, 0x423834F1, 0x4284E08B),
        (0xF149F2CA, 0x3F4A89B0, 0xBF1C9221, 0xBFC90FDB, 0xFFC00000, 0xFFC00000, 0x00000000, 0xFFC00000, 0xFFC00000),
        (0x7F7FFFFF, 0xBF0599B3, 0x3F5A5F96, 0x3FC90FDB, 0xFFC00000, 0xFFC00000, 0x7F800000, 0x42B17218, 0x43000000),
        (0x7F800000, 0xFFC00000, 0xFFC00000, 0x3FC90FDB, 0xFFC00000, 0xFFC00000, 0x7F800000, 0x7F800000, 0x7F800000),
        (0xFF800000, 0xFFC00000, 0xFFC00000, 0xBFC90FDB, 0xFFC00000, 0xFFC00000, 0x00000000, 0xFFC00000, 0xFFC00000),
        (0xFFC00000, 0xFFC00000, 0xFFC00000, 0xFFC00000, 0xFFC00000, 0xFFC00000, 0xFFC00000, 0xFFC00000, 0xFFC00000),
        (0x3E99999A, 0x3E974E6D, 0x3F7490EF, 0x3E9539D4, 0x3E9C00AD, 0x3FA20FB0, 0x3FACC82C, 0xBF9A1BC8, 0xBFDE54E3),
        (0x3F7FFFFF, 0x3F576AA4, 0x3F0A5141, 0x3F490FDA, 0x3FC9048A, 0x39B504F3, 0x402DF854, 0xB3800000, 0xB3B8AA3C),
        (0x3F800001, 0x3F576AA6, 0x3F0A513F, 0x3F490FDC, 0xFFC00000, 0xFFC00000, 0x402DF856, 0x33FFFFFF, 0x3438AA3A),
        (0xBF400000, 0xBF2E7FE1, 0x3F3B4FF6, 0xBF24BC7D, 0xBF591A99, 0x401ACE94, 0x3EF1DA07, 0xFFC00000, 0xFFC00000),
    ];

    /// <summary>(y, x) → Atan2(y, x), Pow(x, y) over every pair of <see cref="GoldenPairs"/>.</summary>
    private static readonly (uint Y, uint X, uint Atan2, uint Pow)[] Golden2 =
    [
        (0x00000000, 0x00000000, 0x00000000, 0x3F800000),
        (0x00000000, 0x80000000, 0x40490FDB, 0x3F800000),
        (0x00000000, 0x000116C2, 0x00000000, 0x3F800000),
        (0x00000000, 0x3F000000, 0x00000000, 0x3F800000),
        (0x00000000, 0xBF800000, 0x40490FDB, 0x3F800000),
        (0x00000000, 0x40000000, 0x00000000, 0x3F800000),
        (0x00000000, 0xC0400000, 0x40490FDB, 0x3F800000),
        (0x00000000, 0x3F19999A, 0x00000000, 0x3F800000),
        (0x00000000, 0x41280000, 0x00000000, 0x3F800000),
        (0x00000000, 0xF149F2CA, 0x40490FDB, 0x3F800000),
        (0x00000000, 0x7F800000, 0x00000000, 0x3F800000),
        (0x00000000, 0xFF800000, 0x40490FDB, 0x3F800000),
        (0x00000000, 0xFFC00000, 0xFFC00000, 0x3F800000),
        (0x80000000, 0x00000000, 0x80000000, 0x3F800000),
        (0x80000000, 0x80000000, 0xC0490FDB, 0x3F800000),
        (0x80000000, 0x000116C2, 0x80000000, 0x3F800000),
        (0x80000000, 0x3F000000, 0x80000000, 0x3F800000),
        (0x80000000, 0xBF800000, 0xC0490FDB, 0x3F800000),
        (0x80000000, 0x40000000, 0x80000000, 0x3F800000),
        (0x80000000, 0xC0400000, 0xC0490FDB, 0x3F800000),
        (0x80000000, 0x3F19999A, 0x80000000, 0x3F800000),
        (0x80000000, 0x41280000, 0x80000000, 0x3F800000),
        (0x80000000, 0xF149F2CA, 0xC0490FDB, 0x3F800000),
        (0x80000000, 0x7F800000, 0x80000000, 0x3F800000),
        (0x80000000, 0xFF800000, 0xC0490FDB, 0x3F800000),
        (0x80000000, 0xFFC00000, 0xFFC00000, 0x3F800000),
        (0x000116C2, 0x00000000, 0x3FC90FDB, 0x00000000),
        (0x000116C2, 0x80000000, 0x3FC90FDB, 0x00000000),
        (0x000116C2, 0x000116C2, 0x3F490FDB, 0x3F800000),
        (0x000116C2, 0x3F000000, 0x00022D84, 0x3F800000),
        (0x000116C2, 0xBF800000, 0x40490FDB, 0xFFC00000),
        (0x000116C2, 0x40000000, 0x00008B61, 0x3F800000),
        (0x000116C2, 0xC0400000, 0x40490FDB, 0xFFC00000),
        (0x000116C2, 0x3F19999A, 0x0001D099, 0x3F800000),
        (0x000116C2, 0x41280000, 0x00001A8C, 0x3F800000),
        (0x000116C2, 0xF149F2CA, 0x40490FDB, 0xFFC00000),
        (0x000116C2, 0x7F800000, 0x00000000, 0x7F800000),
        (0x000116C2, 0xFF800000, 0x40490FDB, 0x7F800000),
        (0x000116C2, 0xFFC00000, 0xFFC00000, 0xFFC00000),
        (0x3F000000, 0x00000000, 0x3FC90FDB, 0x00000000),
        (0x3F000000, 0x80000000, 0x3FC90FDB, 0x00000000),
        (0x3F000000, 0x000116C2, 0x3FC90FDB, 0x1E3CE4E7),
        (0x3F000000, 0x3F000000, 0x3F490FDB, 0x3F3504F3),
        (0x3F000000, 0xBF800000, 0x402B6373, 0xFFC00000),
        (0x3F000000, 0x40000000, 0x3E7ADBAF, 0x3FB504F3),
        (0x3F000000, 0xC0400000, 0x403E7E0F, 0xFFC00000),
        (0x3F000000, 0x3F19999A, 0x3F31DA5E, 0x3F464BF8),
        (0x3F000000, 0x41280000, 0x3D42E680, 0x404F623A),
        (0x3F000000, 0xF149F2CA, 0x40490FDB, 0xFFC00000),
        (0x3F000000, 0x7F800000, 0x00000000, 0x7F800000),
        (0x3F000000, 0xFF800000, 0x40490FDB, 0x7F800000),
        (0x3F000000, 0xFFC00000, 0xFFC00000, 0xFFC00000),
        (0xBF800000, 0x00000000, 0xBFC90FDB, 0x7F800000),
        (0xBF800000, 0x80000000, 0xBFC90FDB, 0xFF800000),
        (0xBF800000, 0x000116C2, 0xBFC90FDB, 0x7F800000),
        (0xBF800000, 0x3F000000, 0xBF8DB70D, 0x40000000),
        (0xBF800000, 0xBF800000, 0xC016CBE4, 0xBF800000),
        (0xBF800000, 0x40000000, 0xBEED6338, 0x3F000000),
        (0xBF800000, 0xC0400000, 0xC034784B, 0xBEAAAAAB),
        (0xBF800000, 0x3F19999A, 0xBF83E363, 0x3FD55555),
        (0xBF800000, 0x41280000, 0xBDC2760B, 0x3DC30C31),
        (0xBF800000, 0xF149F2CA, 0xC0490FDB, 0x8DA24260),
        (0xBF800000, 0x7F800000, 0x80000000, 0x00000000),
        (0xBF800000, 0xFF800000, 0xC0490FDB, 0x80000000),
        (0xBF800000, 0xFFC00000, 0xFFC00000, 0xFFC00000),
        (0x40000000, 0x00000000, 0x3FC90FDB, 0x00000000),
        (0x40000000, 0x80000000, 0x3FC90FDB, 0x00000000),
        (0x40000000, 0x000116C2, 0x3FC90FDB, 0x00000000),
        (0x40000000, 0x3F000000, 0x3FA9B465, 0x3E800000),
        (0x40000000, 0xBF800000, 0x40023454, 0x3F800000),
        (0x40000000, 0x40000000, 0x3F490FDB, 0x40800000),
        (0x40000000, 0xC0400000, 0x40236E05, 0x41100000),
        (0x40000000, 0x3F19999A, 0x3FA3C166, 0x3EB851EC),
        (0x40000000, 0x41280000, 0x3E40BD24, 0x42DC8000),
        (0x40000000, 0xF149F2CA, 0x40490FDB, 0x7F800000),
        (0x40000000, 0x7F800000, 0x00000000, 0x7F800000),
        (0x40000000, 0xFF800000, 0x40490FDB, 0x7F800000),
        (0x40000000, 0xFFC00000, 0xFFC00000, 0xFFC00000),
        (0xC0400000, 0x00000000, 0xBFC90FDB, 0x7F800000),
        (0xC0400000, 0x80000000, 0xBFC90FDB, 0xFF800000),
        (0xC0400000, 0x000116C2, 0xBFC90FDB, 0x7F800000),
        (0xC0400000, 0x3F000000, 0xBFB3EC43, 0x41000000),
        (0xC0400000, 0xBF800000, 0xBFF23EFA, 0xBF800000),
        (0xC0400000, 0x40000000, 0xBF7B985E, 0x3E000000),
        (0xC0400000, 0xC0400000, 0xC016CBE4, 0xBD17B426),
        (0xC0400000, 0x3F19999A, 0xBFAFCB99, 0x409425EC),
        (0xC0400000, 0x41280000, 0xBE8E7D4B, 0x3A627329),
        (0xC0400000, 0xF149F2CA, 0xC0490FDB, 0x80000000),
        (0xC0400000, 0x7F800000, 0x80000000, 0x00000000),
        (0xC0400000, 0xFF800000, 0xC0490FDB, 0x80000000),
        (0xC0400000, 0xFFC00000, 0xFFC00000, 0xFFC00000),
        (0x3F19999A, 0x00000000, 0x3FC90FDB, 0x00000000),
        (0x3F19999A, 0x80000000, 0x3FC90FDB, 0x00000000),
        (0x3F19999A, 0x000116C2, 0x3FC90FDB, 0x179ABDDE),
        (0x3F19999A, 0x3F000000, 0x3F604558, 0x3F28E5A2),
        (0x3F19999A, 0xBF800000, 0x4026799F, 0xFFC00000),
        (0x3F19999A, 0x40000000, 0x3E9539D4, 0x3FC20300),
        (0x3F19999A, 0xC0400000, 0x403C6DBA, 0xFFC00000),
        (0x3F19999A, 0x3F19999A, 0x3F490FDB, 0x3F3C6BEF),
        (0x3F19999A, 0x41280000, 0x3D69CD8B, 0x40832DC4),
        (0x3F19999A, 0xF149F2CA, 0x40490FDB, 0xFFC00000),
        (0x3F19999A, 0x7F800000, 0x00000000, 0x7F800000),
        (0x3F19999A, 0xFF800000, 0x40490FDB, 0x7F800000),
        (0x3F19999A, 0xFFC00000, 0xFFC00000, 0xFFC00000),
        (0x41280000, 0x00000000, 0x3FC90FDB, 0x00000000),
        (0x41280000, 0x80000000, 0x3FC90FDB, 0x00000000),
        (0x41280000, 0x000116C2, 0x3FC90FDB, 0x00000000),
        (0x41280000, 0x3F000000, 0x3FC2F8A7, 0x3A3504F3),
        (0x41280000, 0xBF800000, 0x3FD5373B, 0xFFC00000),
        (0x41280000, 0x40000000, 0x3FB0F836, 0x44B504F3),
        (0x41280000, 0xC0400000, 0x3FECAF2E, 0xFFC00000),
        (0x41280000, 0x3F19999A, 0x3FC1C16E, 0x3B9979A8),
        (0x41280000, 0x41280000, 0x3F490FDB, 0x5144A10C),
        (0x41280000, 0xF149F2CA, 0x40490FDB, 0xFFC00000),
        (0x41280000, 0x7F800000, 0x00000000, 0x7F800000),
        (0x41280000, 0xFF800000, 0x40490FDB, 0x7F800000),
        (0x41280000, 0xFFC00000, 0xFFC00000, 0xFFC00000),
        (0xF149F2CA, 0x00000000, 0xBFC90FDB, 0x7F800000),
        (0xF149F2CA, 0x80000000, 0xBFC90FDB, 0x7F800000),
        (0xF149F2CA, 0x000116C2, 0xBFC90FDB, 0x7F800000),
        (0xF149F2CA, 0x3F000000, 0xBFC90FDB, 0x7F800000),
        (0xF149F2CA, 0xBF800000, 0xBFC90FDB, 0x3F800000),
        (0xF149F2CA, 0x40000000, 0xBFC90FDB, 0x00000000),
        (0xF149F2CA, 0xC0400000, 0xBFC90FDB, 0x00000000),
        (0xF149F2CA, 0x3F19999A, 0xBFC90FDB, 0x7F800000),
        (0xF149F2CA, 0x41280000, 0xBFC90FDB, 0x00000000),
        (0xF149F2CA, 0xF149F2CA, 0xC016CBE4, 0x00000000),
        (0xF149F2CA, 0x7F800000, 0x80000000, 0x00000000),
        (0xF149F2CA, 0xFF800000, 0xC0490FDB, 0x00000000),
        (0xF149F2CA, 0xFFC00000, 0xFFC00000, 0xFFC00000),
        (0x7F800000, 0x00000000, 0x3FC90FDB, 0x00000000),
        (0x7F800000, 0x80000000, 0x3FC90FDB, 0x00000000),
        (0x7F800000, 0x000116C2, 0x3FC90FDB, 0x00000000),
        (0x7F800000, 0x3F000000, 0x3FC90FDB, 0x00000000),
        (0x7F800000, 0xBF800000, 0x3FC90FDB, 0x3F800000),
        (0x7F800000, 0x40000000, 0x3FC90FDB, 0x7F800000),
        (0x7F800000, 0xC0400000, 0x3FC90FDB, 0x7F800000),
        (0x7F800000, 0x3F19999A, 0x3FC90FDB, 0x00000000),
        (0x7F800000, 0x41280000, 0x3FC90FDB, 0x7F800000),
        (0x7F800000, 0xF149F2CA, 0x3FC90FDB, 0x7F800000),
        (0x7F800000, 0x7F800000, 0x3F490FDB, 0x7F800000),
        (0x7F800000, 0xFF800000, 0x4016CBE4, 0x7F800000),
        (0x7F800000, 0xFFC00000, 0xFFC00000, 0xFFC00000),
        (0xFF800000, 0x00000000, 0xBFC90FDB, 0x7F800000),
        (0xFF800000, 0x80000000, 0xBFC90FDB, 0x7F800000),
        (0xFF800000, 0x000116C2, 0xBFC90FDB, 0x7F800000),
        (0xFF800000, 0x3F000000, 0xBFC90FDB, 0x7F800000),
        (0xFF800000, 0xBF800000, 0xBFC90FDB, 0x3F800000),
        (0xFF800000, 0x40000000, 0xBFC90FDB, 0x00000000),
        (0xFF800000, 0xC0400000, 0xBFC90FDB, 0x00000000),
        (0xFF800000, 0x3F19999A, 0xBFC90FDB, 0x7F800000),
        (0xFF800000, 0x41280000, 0xBFC90FDB, 0x00000000),
        (0xFF800000, 0xF149F2CA, 0xBFC90FDB, 0x00000000),
        (0xFF800000, 0x7F800000, 0xBF490FDB, 0x00000000),
        (0xFF800000, 0xFF800000, 0xC016CBE4, 0x00000000),
        (0xFF800000, 0xFFC00000, 0xFFC00000, 0xFFC00000),
        (0xFFC00000, 0x00000000, 0xFFC00000, 0xFFC00000),
        (0xFFC00000, 0x80000000, 0xFFC00000, 0xFFC00000),
        (0xFFC00000, 0x000116C2, 0xFFC00000, 0xFFC00000),
        (0xFFC00000, 0x3F000000, 0xFFC00000, 0xFFC00000),
        (0xFFC00000, 0xBF800000, 0xFFC00000, 0xFFC00000),
        (0xFFC00000, 0x40000000, 0xFFC00000, 0xFFC00000),
        (0xFFC00000, 0xC0400000, 0xFFC00000, 0xFFC00000),
        (0xFFC00000, 0x3F19999A, 0xFFC00000, 0xFFC00000),
        (0xFFC00000, 0x41280000, 0xFFC00000, 0xFFC00000),
        (0xFFC00000, 0xF149F2CA, 0xFFC00000, 0xFFC00000),
        (0xFFC00000, 0x7F800000, 0xFFC00000, 0xFFC00000),
        (0xFFC00000, 0xFF800000, 0xFFC00000, 0xFFC00000),
        (0xFFC00000, 0xFFC00000, 0xFFC00000, 0xFFC00000),
    ];

    [Fact]
    public void GoldenBitPatterns()
    {
        static uint B(float v) => BitConverter.SingleToUInt32Bits(v);
        static float F(uint b) => BitConverter.UInt32BitsToSingle(b);
        Assert.Equal(GoldenInputs.Length, Golden1.Length);
        Assert.Equal(GoldenPairs.Length * GoldenPairs.Length, Golden2.Length);
        var bad = new List<string>();
        void Check(string fn, uint input, uint expected, uint got)
        {
            if (expected != got) bad.Add($"{fn}(0x{input:X8} = {F(input):R}): expected 0x{expected:X8}, got 0x{got:X8}");
        }
        for (int i = 0; i < Golden1.Length; i++)
        {
            var g = Golden1[i];
            float x = F(g.X);
            Assert.Equal(B(GoldenInputs[i]), g.X);
            Check("Sin", g.X, g.Sin, B(DMath.Sin(x)));
            Check("Cos", g.X, g.Cos, B(DMath.Cos(x)));
            var (s, c) = DMath.SinCos(x);
            Check("SinCos.Sin", g.X, g.Sin, B(s));
            Check("SinCos.Cos", g.X, g.Cos, B(c));
            Check("Atan", g.X, g.Atan, B(DMath.Atan(x)));
            Check("Asin", g.X, g.Asin, B(DMath.Asin(x)));
            Check("Acos", g.X, g.Acos, B(DMath.Acos(x)));
            Check("Exp", g.X, g.Exp, B(DMath.Exp(x)));
            Check("Log", g.X, g.Log, B(DMath.Log(x)));
            Check("Log2", g.X, g.Log2, B(DMath.Log2(x)));
        }
        foreach (var g in Golden2)
        {
            Check($"Atan2(y=0x{g.Y:X8}, x)", g.X, g.Atan2, B(DMath.Atan2(F(g.Y), F(g.X))));
            Check($"Pow(x, y=0x{g.Y:X8})", g.X, g.Pow, B(DMath.Pow(F(g.X), F(g.Y))));
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void EdgeCasesFollowIeee()
    {
        Assert.Equal(0x80000000u, BitConverter.SingleToUInt32Bits(DMath.Sin(-0f)));
        Assert.Equal(1f, DMath.Cos(0f));
        Assert.True(float.IsNaN(DMath.Sin(float.PositiveInfinity)) && float.IsNaN(DMath.Cos(float.NaN)));
        Assert.Equal(MathF.PI / 2, DMath.Atan(float.PositiveInfinity));
        Assert.Equal(MathF.PI, DMath.Atan2(0f, -1f));
        Assert.Equal(-MathF.PI, DMath.Atan2(-0f, -0f));
        Assert.Equal(0x80000000u, BitConverter.SingleToUInt32Bits(DMath.Atan2(-0f, 1f)));
        Assert.Equal(MathF.PI / 2, DMath.Atan2(1e30f, -1f)); // π - atan(1e30) keeps the low bits of π/2
        Assert.True(float.IsNaN(DMath.Asin(1.0000001f)) && float.IsNaN(DMath.Acos(-2f)));
        Assert.Equal(float.PositiveInfinity, DMath.Exp(89f));
        Assert.Equal(0f, DMath.Exp(-104f));
        Assert.Equal(float.NegativeInfinity, DMath.Log(0f));
        Assert.True(float.IsNaN(DMath.Log(-1f)));
        for (int e = -149; e <= 127; e++) Assert.Equal(e, DMath.Log2(e >= -126 ? BitConverter.Int32BitsToSingle((e + 127) << 23) : BitConverter.Int32BitsToSingle(1 << (e + 149))));
        Assert.Equal(1f, DMath.Pow(float.NaN, 0f));
        Assert.Equal(1f, DMath.Pow(1f, float.NaN));
        Assert.Equal(1f, DMath.Pow(-1f, float.NegativeInfinity));
        Assert.Equal(-8f, DMath.Pow(-2f, 3f));
        Assert.True(float.IsNaN(DMath.Pow(-2f, 0.5f)));
        Assert.Equal(float.NegativeInfinity, DMath.Pow(-0f, -3f));
        Assert.Equal(0f, DMath.Pow(-0f, 0.5f));
        Assert.Equal(0.25f, DMath.Pow(2f, -2f));
        Assert.Equal(1024f, DMath.Pow(2f, 10f));
    }

    internal static readonly float[] GoldenPairs = [0f, -0f, 1e-40f, 0.5f, -1f, 2f, -3f, 0.6f, 10.5f, -1e30f, float.PositiveInfinity, float.NegativeInfinity, float.NaN];
}

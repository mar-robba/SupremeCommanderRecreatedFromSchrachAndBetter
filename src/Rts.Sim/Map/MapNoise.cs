using Rts.Sim.Core;

namespace Rts.Sim.Map;

/// <summary>
/// Deterministic 2D gradient (Perlin-style) noise for the map generator. Pure functions of (seed, x, y): safe to call
/// from parallel loops. Output of <see cref="Noise"/> is roughly in [-0.7, 0.7].
/// </summary>
internal readonly struct MapNoise
{
    private static readonly float[] GradX = new float[16];
    private static readonly float[] GradY = new float[16];

    static MapNoise()
    {
        for (int i = 0; i < 16; i++)
        {
            float a = i * MathF.PI * 2f / 16f + 0.19f;
            GradX[i] = DMath.Cos(a);
            GradY[i] = DMath.Sin(a);
        }
    }

    private readonly ulong _seed;

    public MapNoise(ulong seed) => _seed = DetRandom.Mix(seed);

    private int Hash(int x, int y) =>
        (int)(DetRandom.Mix(_seed ^ ((ulong)(uint)x * 0x9E3779B1UL) ^ ((ulong)(uint)y << 32)) >> 60);

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private float Grad(int ix, int iy, float dx, float dy)
    {
        int h = Hash(ix, iy);
        return GradX[h] * dx + GradY[h] * dy;
    }

    public float Noise(float x, float y)
    {
        float fx0 = MathF.Floor(x), fy0 = MathF.Floor(y);
        int x0 = (int)fx0, y0 = (int)fy0;
        float dx = x - fx0, dy = y - fy0;
        float u = Fade(dx), v = Fade(dy);
        float n00 = Grad(x0, y0, dx, dy);
        float n10 = Grad(x0 + 1, y0, dx - 1, dy);
        float n01 = Grad(x0, y0 + 1, dx, dy - 1);
        float n11 = Grad(x0 + 1, y0 + 1, dx - 1, dy - 1);
        float a = n00 + (n10 - n00) * u;
        float b = n01 + (n11 - n01) * u;
        return a + (b - a) * v;
    }

    /// <summary>Fractal sum of octaves, normalized by the amplitude sum (roughly [-0.5, 0.5], std ≈ 0.15).</summary>
    public float Fbm(float x, float y, int octaves, float gain = 0.5f)
    {
        float sum = 0, amp = 1, norm = 0, f = 1;
        for (int o = 0; o < octaves; o++)
        {
            // Each octave is offset so lattice points don't line up across octaves.
            sum += amp * Noise(x * f + o * 17.31f, y * f - o * 9.73f);
            norm += amp;
            amp *= gain;
            f *= 2.03f;
        }
        return sum / norm;
    }

    /// <summary>Ridged fractal noise in [0, 1] (sharp crests), used for rough mountain tops.</summary>
    public float Ridged(float x, float y, int octaves)
    {
        float sum = 0, amp = 1, norm = 0, f = 1;
        for (int o = 0; o < octaves; o++)
        {
            float n = 1f - MathF.Abs(Noise(x * f + o * 5.1f, y * f + o * 3.7f)) * 1.6f;
            sum += amp * n * n;
            norm += amp;
            amp *= 0.5f;
            f *= 2.03f;
        }
        return sum / norm;
    }
}

/// <summary>Small math helpers shared by the generator stages.</summary>
internal static class MapMath
{
    public static float SmoothStep(float t)
    {
        t = t < 0 ? 0 : t > 1 ? 1 : t;
        return t * t * (3f - 2f * t);
    }

    /// <summary>Hermite step between edge0 and edge1 (GLSL smoothstep).</summary>
    public static float SmoothStep(float e0, float e1, float x) => SmoothStep((x - e0) / (e1 - e0));

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Smooth maximum (polynomial, blend width k).</summary>
    public static float SMax(float a, float b, float k)
    {
        float h = Math.Clamp(0.5f + 0.5f * (a - b) / k, 0f, 1f);
        return b + (a - b) * h + k * h * (1f - h);
    }
}

using Rts.Sim.Core;
using System.Numerics;

namespace Rts.Sim.Map;

/// <summary>
/// N-fold rotational symmetry about the map center (N = player count). Player k's start is player 0's start
/// rotated by k·2π/N, so anything defined once in player 0's "template" frame and replicated with
/// <see cref="Rotate"/> is fair for everybody. Rotations by multiples of 90° are exact (2 and 4 players map
/// grid vertices onto grid vertices).
/// <para><see cref="Sample"/> makes a continuous symmetric field out of an arbitrary template function: each point is
/// rotated into player 0's sector and evaluated there; within <see cref="Band"/> of a sector border it blends with the
/// neighbor sector (weight 0.5 on the border, so both sides agree), and near the center it fades to the average of all
/// N rotations (avoids a pinwheel singularity).</para>
/// </summary>
internal sealed class MapSymmetry
{
    public readonly int N;
    public readonly Vector2 Center;
    /// <summary>Angle of player 0's start around the center (the middle of sector 0).</summary>
    public readonly float Phi0;
    public readonly float Band;
    public readonly float CenterRadius;
    private readonly float[] _cos;
    private readonly float[] _sin;
    private readonly float _sector;

    public MapSymmetry(int n, Vector2 center, float phi0, float band, float centerRadius)
    {
        N = Math.Max(1, n);
        Center = center;
        Phi0 = phi0;
        Band = band;
        CenterRadius = centerRadius;
        _sector = MathF.PI * 2f / N;
        // Index k in [-N, N): rotation by k·2π/N (negative = clockwise).
        _cos = new float[2 * N];
        _sin = new float[2 * N];
        for (int k = -N; k < N; k++)
        {
            int idx = k + N;
            // Exact values for quarter turns so 2- and 4-player maps are exactly symmetric.
            int quarter4 = k * 4;
            if (quarter4 % N == 0)
            {
                int q = ((quarter4 / N) % 4 + 4) % 4;
                (_cos[idx], _sin[idx]) = q switch { 0 => (1f, 0f), 1 => (0f, 1f), 2 => (-1f, 0f), _ => (0f, -1f) };
            }
            else
            {
                (_sin[idx], _cos[idx]) = DMath.SinCos(k * DMath.TwoPi / N);
            }
        }
    }

    /// <summary>True for 1, 2 and 4 players: every rotation is a quarter turn, which maps grid vertices onto grid
    /// vertices exactly. Other counts rotate by arbitrary angles (rotated copies are resampled).</summary>
    public bool QuarterTurns => 4 % N == 0;

    /// <summary>Rotates p about the center by k·2π/N (k may be negative, |k| &lt; N).</summary>
    public Vector2 Rotate(Vector2 p, int k)
    {
        k %= N;
        int idx = k + N;
        float c = _cos[idx], s = _sin[idx];
        float dx = p.X - Center.X, dz = p.Y - Center.Y;
        return new Vector2(Center.X + dx * c - dz * s, Center.Y + dx * s + dz * c);
    }

    /// <summary>Rotates a direction vector (no translation) by k·2π/N.</summary>
    public Vector2 RotateDir(Vector2 d, int k)
    {
        k %= N;
        int idx = k + N;
        float c = _cos[idx], s = _sin[idx];
        return new Vector2(d.X * c - d.Y * s, d.X * s + d.Y * c);
    }

    /// <summary>Sector of a point (0..N-1) and its signed angle from the sector's middle line.</summary>
    public int SectorOf(Vector2 p, out float localAngle, out float radius)
    {
        float dx = p.X - Center.X, dz = p.Y - Center.Y;
        radius = MathF.Sqrt(dx * dx + dz * dz);
        float ang = DMath.Atan2(dz, dx) - Phi0;
        float s = ang / _sector;
        float fl = MathF.Floor(s + 0.5f);
        localAngle = (s - fl) * _sector;
        int k = (int)fl % N;
        if (k < 0) k += N;
        return k;
    }

    /// <summary>
    /// Evaluates a symmetric version of the template function g (which writes <paramref name="count"/> values) at p.
    /// </summary>
    public void Sample(Vector2 p, int count, Span<float> result, TemplateFn g)
    {
        Span<float> tmp = stackalloc float[8];
        if (N == 1)
        {
            g(p, result);
            return;
        }
        int k = SectorOf(p, out float a, out float r);
        g(Rotate(p, -k), result);

        float distToBorder = r * DMath.Sin(MathF.Max(0f, _sector * 0.5f - MathF.Abs(a)));
        if (distToBorder < Band)
        {
            int k2 = a >= 0 ? k + 1 : k - 1;
            if (k2 >= N) k2 -= N;
            if (k2 < 0) k2 += N;
            g(Rotate(p, -k2), tmp);
            float w = 0.5f * MapMath.SmoothStep(1f - distToBorder / Band);
            for (int i = 0; i < count; i++) result[i] += (tmp[i] - result[i]) * w;
        }

        if (r < CenterRadius)
        {
            Span<float> avg = stackalloc float[8];
            avg.Clear();
            for (int j = 0; j < N; j++)
            {
                g(Rotate(p, -j), tmp);
                for (int i = 0; i < count; i++) avg[i] += tmp[i];
            }
            float t = MapMath.SmoothStep(r / CenterRadius);
            for (int i = 0; i < count; i++) result[i] = avg[i] / N + (result[i] - avg[i] / N) * t;
        }
    }

    public delegate void TemplateFn(Vector2 p, Span<float> output);
}

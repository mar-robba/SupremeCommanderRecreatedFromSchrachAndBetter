using System.Numerics;
using Rts.Sim.Core;

namespace Rts.Sim.Map;

/// <summary>
/// Canyon preset: a valley network carved into high mesas. Player 0's template network — start plaza, a spoke to
/// the central plaza with a chokepoint, a ring valley to the next player with a chokepoint, and two side pockets
/// (expansion spots) — is replicated in all N rotations. Valley walls are steep (impassable cliffs) with noisy edges.
/// </summary>
internal sealed partial class MapBuilder
{
    private readonly record struct Capsule(Vector2 A, Vector2 B, float RA, float RB);

    private void CarveValleys()
    {
        var caps = new List<Capsule>();
        var rng = new DetRandom(_seed ^ 0xCA770UL);
        float k = MathF.Sqrt(_sc);
        int N = _sym.N;
        var s0 = _starts[0];
        var s1 = _sym.Rotate(s0, 1);
        float wide = 9f * k, mid = 6.5f * k, choke = 4.8f;

        // Plazas around the starts and in the center.
        float plaza = _blendR + 2f;
        caps.Add(new Capsule(s0, s0, plaza, plaza));
        float centerR = _n * 0.06f + 6f;
        caps.Add(new Capsule(_center, _center, centerR, centerR));

        // Spoke: start -> center, with a chokepoint around the middle.
        var spoke = Wiggle(s0, _center, 7, 0.06f, ref rng);
        AddPath(caps, spoke, t => t < 0.2f ? wide : t > 0.38f && t < 0.56f ? choke : t > 0.85f ? wide : mid);

        // Ring: start -> next start, bowed outwards through M; chokepoint where neighbors meet.
        float R = Vector2.Distance(s0, _center);
        float rMid = N == 2 ? 0.38f * _n : MathF.Min(0.36f * _n, R * (1f + DMath.Cos(DMath.Pi / N)) * 0.5f);
        var M = _center + DMath.Dir(_sym.Phi0 + DMath.Pi / N) * rMid;
        var ctrl = 2f * M - (s0 + s1) * 0.5f;
        var ring = new List<Vector2>();
        int segs = N <= 3 ? 10 : 6;
        for (int i = 0; i <= segs; i++)
        {
            float t = i / (float)segs;
            var p = (1 - t) * (1 - t) * s0 + 2 * (1 - t) * t * ctrl + t * t * s1;
            if (i > 0 && i < segs) p += new Vector2(rng.Range(-1f, 1f), rng.Range(-1f, 1f)) * (0.02f * _n);
            ring.Add(Vector2.Clamp(p, new Vector2(14f), new Vector2(_n - 14f)));
        }
        AddPath(caps, ring, t => t < 0.12f || t > 0.88f ? wide : t > 0.42f && t < 0.58f ? choke : mid);

        // Side pockets (expansions): off the spoke towards the ring, and off the ring towards the center.
        var b1 = PointAlong(spoke, 0.62f);
        var toRing = Vector2.Normalize(M - b1);
        var e1 = b1 + toRing * (0.10f * _n);
        caps.Add(new Capsule(b1, e1, mid, 11f * k));
        var b2 = PointAlong(ring, 0.28f);
        var e2 = b2 + Vector2.Normalize(_center - b2) * (0.09f * _n);
        caps.Add(new Capsule(b2, e2, mid, 10f * k));

        RasterValleys(caps);

        var floor = _floor!;
        var valley = _valley!;
        Parallel.For(0, _h.Length, v => _h[v] += (floor[v] - _h[v]) * valley[v]);
    }

    private List<Vector2> Wiggle(Vector2 a, Vector2 b, int segs, float amp, ref DetRandom rng)
    {
        var d = b - a;
        float len = d.Length();
        var perp = new Vector2(-d.Y, d.X) / MathF.Max(1e-3f, len);
        var pts = new List<Vector2>();
        for (int i = 0; i <= segs; i++)
        {
            var p = DMath.Lerp(a, b, i / (float)segs);
            if (i > 0 && i < segs) p += perp * rng.Range(-amp, amp) * len;
            pts.Add(p);
        }
        return pts;
    }

    private static Vector2 PointAlong(List<Vector2> pts, float t)
    {
        float f = t * (pts.Count - 1);
        int i = Math.Min((int)f, pts.Count - 2);
        return DMath.Lerp(pts[i], pts[i + 1], f - i);
    }

    private static void AddPath(List<Capsule> caps, List<Vector2> pts, Func<float, float> width)
    {
        for (int i = 0; i < pts.Count - 1; i++)
        {
            float t0 = i / (float)(pts.Count - 1), t1 = (i + 1) / (float)(pts.Count - 1);
            caps.Add(new Capsule(pts[i], pts[i + 1], width(t0), width(t1)));
        }
    }

    private void RasterValleys(List<Capsule> caps)
    {
        const float wall = 3.2f, edgeAmp = 1.6f;
        var valley = _valley!;
        var lakeOk = _lakeOk!;
        float kScale = MathF.Sqrt(_sc);
        foreach (var cap in caps)
        for (int k = 0; k < _sym.N; k++)
        {
            var a = _sym.Rotate(cap.A, k);
            var b = _sym.Rotate(cap.B, k);
            float R = MathF.Max(cap.RA, cap.RB) + wall + edgeAmp + 1f;
            int x0 = Math.Max(0, (int)(MathF.Min(a.X, b.X) - R)), x1 = Math.Min(_n, (int)(MathF.Max(a.X, b.X) + R) + 1);
            int z0 = Math.Max(0, (int)(MathF.Min(a.Y, b.Y) - R)), z1 = Math.Min(_n, (int)(MathF.Max(a.Y, b.Y) + R) + 1);
            if (x1 < x0 || z1 < z0) continue;
            var ab = cap.B - cap.A;
            float ab2 = ab.LengthSquared();
            int kk = k;
            Parallel.For(z0, z1 + 1, z =>
            {
                for (int x = x0; x <= x1; x++)
                {
                    // Evaluate in template space so every copy is identical.
                    var q = _sym.Rotate(new Vector2(x, z), -kk);
                    float t = ab2 > 1e-6f ? Math.Clamp(Vector2.Dot(q - cap.A, ab) / ab2, 0f, 1f) : 0f;
                    float d = Vector2.Distance(q, cap.A + ab * t);
                    float r0 = cap.RA + (cap.RB - cap.RA) * t;
                    // Edge noise: small wiggles everywhere, big bays on wide valleys and plazas.
                    float r = r0 + edgeAmp * 2.4f * _nEdge.Fbm(q.X / 16f, q.Y / 16f, 2)
                              + MathF.Max(0f, r0 - 8f) * 0.9f * _nEdge.Fbm(q.X / 45f + 7.3f, q.Y / 45f - 2.1f, 2);
                    float m = 1f - MapMath.SmoothStep(r, r + wall, d);
                    int v = z * _stride + x;
                    if (m > valley[v]) valley[v] = m;
                    // Lakes only well inside wide parts (plazas, pockets), never blocking a narrow valley.
                    float lk = (1f - MapMath.SmoothStep(r - 7f, r - 3f, d)) * MapMath.SmoothStep(8.5f * kScale, 11f * kScale, r0);
                    if (lk > lakeOk[v]) lakeOk[v] = lk;
                }
            });
        }
    }
}

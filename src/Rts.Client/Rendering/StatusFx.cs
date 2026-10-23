using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;

namespace Rts.Client.Rendering;

/// <summary>Which status visuals a unit gets this frame (see <see cref="StatusFx.VisualsFor"/>).</summary>
[Flags]
public enum StatusVisual : byte
{
    None = 0,
    /// <summary>Translucent shimmer model instead of the opaque one.</summary>
    Cloak = 1,
    /// <summary>Blue-white EMP sparks around the unit.</summary>
    Stun = 2,
    /// <summary>Faint violet terrain-following ring at the stealth field radius.</summary>
    Field = 4,
}

/// <summary>
/// Pure, GPU-free logic of the stealth/cloak/EMP status visuals drawn by <c>UnitRenderer.DrawStatus</c>
/// (UnitRenderer.Status.cs): which effects a <see cref="UnitStatus"/> gets, the field radius, and the deterministic
/// per-unit flicker of the EMP sparks (hash of the unit index and a client-time bucket, so a unit's bolts hold their
/// shape for one bucket and every unit flickers differently). Radar/sonar stealth alone has no visual.
/// </summary>
public static class StatusFx
{
    /// <summary>EMP spark shapes change this many times per second.</summary>
    public const float BucketsPerSecond = 14f;
    /// <summary>Spark bolts per stunned unit and bucket (some buckets skip a bolt, see <see cref="BoltVisible"/>).</summary>
    public const int BoltsPerUnit = 3;
    /// <summary>Points of one jagged bolt.</summary>
    public const int BoltPoints = 5;

    /// <summary>The visuals of a unit with status <paramref name="s"/>. Units under construction get none (they
    /// are drawn as holograms and their stealth isn't active anyway).</summary>
    public static StatusVisual VisualsFor(UnitStatus s, bool underConstruction)
    {
        if (underConstruction) return StatusVisual.None;
        var v = StatusVisual.None;
        if ((s & UnitStatus.Cloaked) != 0) v |= StatusVisual.Cloak;
        if ((s & UnitStatus.Stunned) != 0) v |= StatusVisual.Stun;
        if ((s & UnitStatus.StealthField) != 0) v |= StatusVisual.Field;
        return v;
    }

    /// <summary>Radius of the stealth field the unit projects (0 = none): the blueprint's
    /// <c>Intel.StealthFieldRadius</c>. Commander/SCU enhancements are blueprint variants (EnhancementCatalog), so
    /// the snapshot's <c>UnitView.Bp</c> already carries an enhancement's <c>Effects.StealthFieldRadius</c>.</summary>
    public static float FieldRadius(UnitBlueprint bp, UnitStatus s) =>
        (s & UnitStatus.StealthField) != 0 ? MathF.Max(0f, bp.Intel.StealthFieldRadius) : 0f;

    /// <summary>Client-time bucket of the spark flicker.</summary>
    public static int TimeBucket(float time) => (int)MathF.Floor(time * BucketsPerSecond);

    /// <summary>Stable hash of (unit index, time bucket, bolt): same inputs, same sparks.</summary>
    public static uint FlickerSeed(int unitIndex, int bucket, int bolt)
    {
        uint h = (uint)unitIndex * 0x9E3779B1u;
        h ^= (uint)bucket * 0x85EBCA77u + 0x632BE5ABu;
        h ^= (uint)bolt * 0xC2B2AE3Du;
        return Mix(h);
    }

    /// <summary>Is bolt <paramref name="bolt"/> drawn in this bucket? Bolt 0 always is; the others blink.</summary>
    public static bool BoltVisible(uint seed, int bolt) => bolt == 0 || (seed >> 28) >= 5;

    /// <summary>0.45..1 brightness of a bolt.</summary>
    public static float BoltIntensity(uint seed) => 0.45f + 0.55f * Unit(Mix(seed ^ 0x27D4EB2Fu));

    /// <summary>Fills <paramref name="pts"/> with a jagged arc between two points on a sphere of
    /// <paramref name="radius"/> around <paramref name="center"/>, bowed outwards, with random kinks.</summary>
    public static void Bolt(uint seed, Vector3 center, float radius, Span<Vector3> pts)
    {
        if (pts.Length == 0) return;
        uint s = seed;
        var a = SphereDir(ref s);
        var b = SphereDir(ref s);
        if (Vector3.Dot(a, b) < -0.2f) b = Vector3.Normalize(b + a * 1.2f); // keep arcs short-ish (not through the hull)
        int n = pts.Length;
        for (int k = 0; k < n; k++)
        {
            float t = n == 1 ? 0f : k / (float)(n - 1);
            var d = Vector3.Lerp(a, b, t);
            d = d.LengthSquared() > 1e-6f ? Vector3.Normalize(d) : a;
            float bow = 1f + 0.25f * MathF.Sin(t * MathF.PI);
            var p = center + d * radius * bow;
            if (k > 0 && k < n - 1)
            {
                var j = new Vector3(Unit(Next(ref s)) - 0.5f, Unit(Next(ref s)) - 0.5f, Unit(Next(ref s)) - 0.5f);
                p += j * radius * 0.45f;
            }
            pts[k] = p;
        }
    }

    private static Vector3 SphereDir(ref uint s)
    {
        float z = Unit(Next(ref s)) * 2f - 1f;
        float a = Unit(Next(ref s)) * MathF.Tau;
        float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        // Bias upwards: sparks crawl over the visible top half of the unit more than under it.
        var d = new Vector3(r * MathF.Cos(a), MathF.Abs(z) * 0.8f + 0.2f, r * MathF.Sin(a));
        return Vector3.Normalize(d);
    }

    private static uint Next(ref uint s) { s = Mix(s + 0x6D2B79F5u); return s; }
    private static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

    private static uint Mix(uint h)
    {
        h ^= h >> 16; h *= 0x7FEB352Du;
        h ^= h >> 15; h *= 0x846CA68Bu;
        h ^= h >> 16;
        return h;
    }
}

using System.Numerics;
using Rts.Sim.Data;

namespace Rts.Client.Rendering;

/// <summary>(10b-2) What a range / intel ring shows. Append only (colors and the cap priority are by kind).</summary>
public enum RingKind : byte { Weapon, AntiAir, MinRange, Radar, Sonar, Omni, Shield, StealthField, MissileDefense }

/// <summary>One merged ring: <see cref="Count"/> rings of the same kind and radius whose centres fell in one merge
/// cell, drawn once at their mean centre. <see cref="Source"/> 0 = the selection, 1 = allied structures (Shift).</summary>
public struct RangeRing
{
    public RingKind Kind;
    public byte Source;
    public float Radius;
    public Vector2 Center;
    public int Count;
}

/// <summary>
/// (10b-2) Pure part of <see cref="SelectionRangeOverlay"/> (no raylib; tests in <c>RangeRingTests</c>). Collects the
/// rings of many units, merges equal rings (same kind, radius rounded to 0.5, centre in the same cell of size
/// <see cref="CellSize"/>), keeps at most <see cref="Cap"/> (priority: selection before Shift structures, rare kinds
/// before weapons, big clusters first) and builds the union outline of each (kind, radius) group: a ring segment
/// whose both ends lie inside another ring of the same group is skipped, so 500 tanks show one blob outline instead
/// of 500 circles. All buffers are reused: no allocation per frame once they have grown.
/// </summary>
public sealed class RangeRingSet
{
    public const int DefaultCap = 96;
    /// <summary>Rings above this radius (strategic missiles: the whole map) are not drawn.</summary>
    public const float MaxRadius = 512f;
    /// <summary>Segments per full ring.</summary>
    public const int Segments = 72;

    public int Cap = DefaultCap;
    private RangeRing[] _rings = new RangeRing[128];
    private Vector2[] _sum = new Vector2[128];
    private int _count;
    private readonly Dictionary<long, int> _index = new(256);
    private Vector2[] _seg = new Vector2[1024];
    private int[] _segRing = new int[512];
    private int _segCount;
    private int[] _nbr = new int[256];
    private static readonly Vector2[] Unit = BuildUnit();

    /// <summary>Rings offered since <see cref="Clear"/> (before merging).</summary>
    public int Added { get; private set; }
    /// <summary>Merged rings dropped by the cap in the last <see cref="Finish"/>.</summary>
    public int Dropped { get; private set; }
    public int Count => _count;
    public ReadOnlySpan<RangeRing> Rings => _rings.AsSpan(0, _count);
    /// <summary>Outline segments of the last <see cref="BuildOutline"/>: segment s = points 2s, 2s+1 of ring SegmentRing(s).</summary>
    public int SegmentCount => _segCount;
    public ReadOnlySpan<Vector2> SegmentPoints => _seg.AsSpan(0, _segCount * 2);
    public int SegmentRing(int s) => _segRing[s];

    private static Vector2[] BuildUnit()
    {
        var u = new Vector2[Segments + 1];
        for (int k = 0; k <= Segments; k++)
        {
            float a = k * MathF.Tau / Segments;
            u[k] = new Vector2(MathF.Cos(a), MathF.Sin(a));
        }
        return u;
    }

    /// <summary>Merge cell edge for a ring radius: a quarter of the radius (min 2), so a clustered army merges into a
    /// few rings while units far apart keep their own.</summary>
    public static float CellSize(float radius) => MathF.Max(2f, radius * 0.25f);

    public void Clear()
    {
        _count = 0;
        _segCount = 0;
        _index.Clear();
        Added = 0;
        Dropped = 0;
    }

    public void Add(RingKind kind, float radius, Vector2 pos, byte source = 0)
    {
        if (!(radius > 0.5f) || radius > MaxRadius || !float.IsFinite(pos.X) || !float.IsFinite(pos.Y)) return;
        Added++;
        int rq = (int)MathF.Round(radius * 2f);
        float cs = CellSize(rq * 0.5f);
        int cx = (int)MathF.Floor(pos.X / cs), cz = (int)MathF.Floor(pos.Y / cs);
        long key = ((long)kind << 58) | ((long)source << 56) | ((long)(rq & 0xFFFF) << 40) | ((long)(cx & 0xFFFFF) << 20) | (uint)(cz & 0xFFFFF);
        if (_index.TryGetValue(key, out int k))
        {
            _sum[k] += pos;
            _rings[k].Count++;
            return;
        }
        if (_count == _rings.Length)
        {
            Array.Resize(ref _rings, _count * 2);
            Array.Resize(ref _sum, _count * 2);
        }
        _index[key] = _count;
        _rings[_count] = new RangeRing { Kind = kind, Source = source, Radius = rq * 0.5f, Center = pos, Count = 1 };
        _sum[_count] = pos;
        _count++;
    }

    /// <summary>Every ring of one unit: weapon max ranges (anti-air apart), min ranges, radar / sonar / omni, shield,
    /// stealth field, tactical / strategic missile defence. Enhancement variants carry their own blueprint data.</summary>
    public void AddUnit(UnitBlueprint bp, Vector2 pos, byte source = 0)
    {
        foreach (var w in bp.Weapons)
        {
            if (w.Range <= 0) continue;
            bool aa = w.TargetMask != 0 && (w.TargetMask & ~LayerMask.Air) == 0;
            Add(aa ? RingKind.AntiAir : RingKind.Weapon, w.Range, pos, source);
            if (w.MinRange > 0) Add(RingKind.MinRange, w.MinRange, pos, source);
        }
        var it = bp.Intel;
        Add(RingKind.Radar, it.RadarRadius, pos, source);
        Add(RingKind.Sonar, it.SonarRadius, pos, source);
        Add(RingKind.Omni, it.OmniRadius, pos, source);
        Add(RingKind.StealthField, it.StealthFieldRadius, pos, source);
        if (bp.Shield != null) Add(RingKind.Shield, bp.Shield.Radius, pos, source);
        if (bp.MissileDefense != null) Add(RingKind.MissileDefense, bp.MissileDefense.Radius, pos, source);
    }

    /// <summary>Mean centres, then the cap.</summary>
    public void Finish()
    {
        for (int k = 0; k < _count; k++) _rings[k].Center = _sum[k] / _rings[k].Count;
        Dropped = 0;
        if (_count > Cap)
        {
            Array.Sort(_rings, 0, _count, Priority.Instance);
            Dropped = _count - Math.Max(0, Cap);
            _count = Math.Max(0, Cap);
        }
    }

    /// <summary>Lower = kept first under the cap: intel, shields and defences are rare and matter more than one more
    /// weapon blob.</summary>
    public static int KindRank(RingKind k) => k switch
    {
        RingKind.Shield => 0, RingKind.Radar => 1, RingKind.Sonar => 2, RingKind.Omni => 3, RingKind.StealthField => 4,
        RingKind.MissileDefense => 5, RingKind.AntiAir => 6, RingKind.Weapon => 7, _ => 8,
    };

    private sealed class Priority : IComparer<RangeRing>
    {
        public static readonly Priority Instance = new();
        public int Compare(RangeRing a, RangeRing b)
        {
            int c = a.Source.CompareTo(b.Source);
            if (c == 0) c = KindRank(a.Kind).CompareTo(KindRank(b.Kind));
            if (c == 0) c = b.Count.CompareTo(a.Count);
            if (c == 0) c = b.Radius.CompareTo(a.Radius);
            if (c == 0) c = a.Center.X.CompareTo(b.Center.X);
            if (c == 0) c = a.Center.Y.CompareTo(b.Center.Y);
            return c;
        }
    }

    private static bool SameGroup(in RangeRing a, in RangeRing b) => a.Kind == b.Kind && a.Radius == b.Radius;

    /// <summary>Union outline of the kept rings. <paramref name="visible"/> (optional culling, null = all) is asked
    /// once per ring.</summary>
    public void BuildOutline(Func<Vector2, float, bool>? visible = null)
    {
        _segCount = 0;
        for (int a = 0; a < _count; a++)
        {
            ref var ra = ref _rings[a];
            if (visible != null && !visible(ra.Center, ra.Radius)) continue;
            // Neighbours: same group, overlapping.
            int n = 0;
            for (int b = 0; b < _count; b++)
            {
                if (b == a) continue;
                ref var rb = ref _rings[b];
                if (!SameGroup(ra, rb)) continue;
                float reach = ra.Radius + rb.Radius;
                if (Vector2.DistanceSquared(ra.Center, rb.Center) >= reach * reach) continue;
                if (n == _nbr.Length) Array.Resize(ref _nbr, n * 2);
                _nbr[n++] = b;
            }
            bool prevIn = Inside(ra.Center + Unit[0] * ra.Radius, n);
            for (int k = 0; k < Segments; k++)
            {
                var p0 = ra.Center + Unit[k] * ra.Radius;
                var p1 = ra.Center + Unit[k + 1] * ra.Radius;
                bool in1 = Inside(p1, n);
                if (!(prevIn && in1))
                {
                    if (_segCount == _segRing.Length)
                    {
                        Array.Resize(ref _segRing, _segCount * 2);
                        Array.Resize(ref _seg, _segCount * 4);
                    }
                    _seg[_segCount * 2] = p0;
                    _seg[_segCount * 2 + 1] = p1;
                    _segRing[_segCount++] = a;
                }
                prevIn = in1;
            }
        }
    }

    private bool Inside(Vector2 p, int n)
    {
        for (int j = 0; j < n; j++)
        {
            ref var r = ref _rings[_nbr[j]];
            float rr = r.Radius - 0.05f;
            if (Vector2.DistanceSquared(p, r.Center) < rr * rr) return true;
        }
        return false;
    }
}

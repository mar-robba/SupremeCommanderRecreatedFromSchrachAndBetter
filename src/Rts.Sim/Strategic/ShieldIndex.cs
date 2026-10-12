using System.Numerics;
using Rts.Sim.World;

namespace Rts.Sim.Strategic;

/// <summary>An active (up) shield dome for this tick.</summary>
public struct ShieldEntry
{
    public int Unit;
    public int Army;
    /// <summary>Dome center (x, y-up, z).</summary>
    public Vector3 Center;
    /// <summary>Horizontal radius; the vertical semi-axis is Radius × HeightScale.</summary>
    public float Radius;
    public float InvHeightScale;
}

/// <summary>
/// Coarse uniform grid (32-unit cells) of the shields that are up this tick, rebuilt by <see cref="ShieldSystem"/>
/// after movement (counting sort in registry order = deterministic). Read-only during the parallel projectile phase.
/// Each cell also stores a mask of the armies owning a shield there, so projectiles skip cells without enemy shields.
/// With no shield up, queries return immediately (zero cost for games without shields).
/// </summary>
public sealed class ShieldIndex
{
    public const float CellSize = 32f;
    private const float Inv = 1f / CellSize;
    public readonly int Cols, Rows;
    private readonly int[] _cellStart;
    private readonly ulong[] _cellArmies;
    private int[] _items = new int[64];
    private int[] _cursor;
    public ShieldEntry[] Entries = new ShieldEntry[16];
    public int Count;

    public ShieldIndex(float width, float height)
    {
        Cols = Math.Max(1, (int)MathF.Ceiling(width * Inv));
        Rows = Math.Max(1, (int)MathF.Ceiling(height * Inv));
        _cellStart = new int[Cols * Rows + 1];
        _cursor = new int[Cols * Rows + 1];
        _cellArmies = new ulong[Cols * Rows];
    }

    public void Clear() => Count = 0;

    public void Add(in ShieldEntry e)
    {
        if (Count == Entries.Length) Array.Resize(ref Entries, Count * 2);
        Entries[Count++] = e;
    }

    private void CellRange(Vector2 min, Vector2 max, out int x0, out int x1, out int z0, out int z1)
    {
        x0 = Math.Clamp((int)(min.X * Inv), 0, Cols - 1);
        x1 = Math.Clamp((int)(max.X * Inv), 0, Cols - 1);
        z0 = Math.Clamp((int)(min.Y * Inv), 0, Rows - 1);
        z1 = Math.Clamp((int)(max.Y * Inv), 0, Rows - 1);
    }

    /// <summary>Builds the cell lists from the entries added since <see cref="Clear"/>.</summary>
    public void Build()
    {
        if (Count == 0) return;
        Array.Clear(_cellStart);
        Array.Clear(_cellArmies);
        int total = 0;
        for (int k = 0; k < Count; k++)
        {
            ref var e = ref Entries[k];
            var c = new Vector2(e.Center.X, e.Center.Z);
            CellRange(c - new Vector2(e.Radius), c + new Vector2(e.Radius), out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                _cellStart[z * Cols + x + 1]++;
                _cellArmies[z * Cols + x] |= 1UL << e.Army;
                total++;
            }
        }
        if (_items.Length < total) _items = new int[Math.Max(total, _items.Length * 2)];
        int cells = Cols * Rows;
        for (int c = 0; c < cells; c++) _cellStart[c + 1] += _cellStart[c];
        Array.Copy(_cellStart, _cursor, cells + 1);
        for (int k = 0; k < Count; k++)
        {
            ref var e = ref Entries[k];
            var c = new Vector2(e.Center.X, e.Center.Z);
            CellRange(c - new Vector2(e.Radius), c + new Vector2(e.Radius), out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                _items[_cursor[z * Cols + x]++] = k;
        }
    }

    /// <summary>
    /// Parameter in [0,1] where segment a→b enters the dome from outside, -1 if it doesn't (misses, or a is already
    /// inside: shots fired from inside pass out).
    /// </summary>
    public static float EnterParam(in ShieldEntry e, Vector3 a, Vector3 b)
    {
        // Ellipsoid → unit-sphere space (scale y): the entry parameter is invariant under the scaling.
        var pa = a - e.Center;
        var pb = b - e.Center;
        pa.Y *= e.InvHeightScale;
        pb.Y *= e.InvHeightScale;
        float r2 = e.Radius * e.Radius;
        float cc = pa.LengthSquared() - r2;
        if (cc <= 0) return -1;
        var d = pb - pa;
        float aa = d.LengthSquared();
        if (aa < 1e-9f) return -1;
        float bb = 2f * Vector3.Dot(pa, d);
        if (bb >= 0) return -1; // moving away from the center
        float disc = bb * bb - 4f * aa * cc;
        if (disc < 0) return -1;
        float t = (-bb - MathF.Sqrt(disc)) / (2f * aa);
        return t >= 0 && t <= 1 ? t : -1;
    }

    /// <summary>Is the 3D point inside the dome?</summary>
    public static bool Contains(in ShieldEntry e, Vector3 p)
    {
        var d = p - e.Center;
        d.Y *= e.InvHeightScale;
        return d.LengthSquared() < e.Radius * e.Radius;
    }

    /// <summary>
    /// First shield of an army in <paramref name="enemies"/> (bit mask) whose dome segment a→b enters from outside.
    /// Returns the entry index (-1 = none) and the segment parameter. Deterministic (cell order, entry order).
    /// </summary>
    public int FirstEntry(ulong enemies, Vector3 a, Vector3 b, ShieldState[] states, out float tHit)
    {
        tHit = 2f;
        if (Count == 0 || enemies == 0) return -1;
        int best = -1;
        CellRange(new Vector2(MathF.Min(a.X, b.X), MathF.Min(a.Z, b.Z)), new Vector2(MathF.Max(a.X, b.X), MathF.Max(a.Z, b.Z)),
            out int x0, out int x1, out int z0, out int z1);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            int c = z * Cols + x;
            if ((_cellArmies[c] & enemies) == 0) continue;
            for (int k = _cellStart[c]; k < _cellStart[c + 1]; k++)
            {
                int ei = _items[k];
                ref var e = ref Entries[ei];
                if (((enemies >> e.Army) & 1) == 0 || !states[e.Unit].Up) continue;
                float t = EnterParam(e, a, b);
                if (t < 0 || t > tHit || (t == tHit && ei > best)) continue;
                tHit = t;
                best = ei;
            }
        }
        return best;
    }

    /// <summary>
    /// True if the point <paramref name="victim"/> is inside a dome of an army in <paramref name="enemies"/> whose
    /// footprint does not contain <paramref name="blast"/> (area damage from outside doesn't leak in).
    /// </summary>
    public bool Protects(ulong enemies, Vector3 victim, Vector2 blast, ShieldState[] states)
    {
        if (Count == 0 || enemies == 0) return false;
        int cx = Math.Clamp((int)(victim.X * Inv), 0, Cols - 1);
        int cz = Math.Clamp((int)(victim.Z * Inv), 0, Rows - 1);
        int c = cz * Cols + cx;
        if ((_cellArmies[c] & enemies) == 0) return false;
        for (int k = _cellStart[c]; k < _cellStart[c + 1]; k++)
        {
            ref var e = ref Entries[_items[k]];
            if (((enemies >> e.Army) & 1) == 0 || !states[e.Unit].Up) continue;
            if (!Contains(e, victim)) continue;
            float dx = blast.X - e.Center.X, dz = blast.Y - e.Center.Z;
            if (dx * dx + dz * dz > e.Radius * e.Radius) return true;
        }
        return false;
    }
}

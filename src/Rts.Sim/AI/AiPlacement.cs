using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Structure placement for one AI: spiral search around a preferred point with <see cref="OccupancyGrid.CanPlace"/>,
/// a free margin around each footprint (so units — including the commander — can walk between buildings and leave
/// factories), no blocking of mass/hydro deposits or the rally point, and reservations for sites engineers are still
/// walking to (so two engineers never pick the same spot).
/// </summary>
public sealed class AiPlacement
{
    private readonly AiBrain _b;
    private readonly List<Reservation> _reserved = new();

    public struct Reservation
    {
        public int Bp;
        public Vector2 Pos;
        public int X0, Z0, X1, Z1; // footprint expanded by the margin (cells, inclusive)
        public UnitHandle Builder;
        public int Tick;
        public int Deposit;
    }

    public AiPlacement(AiBrain b) => _b = b;

    public IReadOnlyList<Reservation> Reserved => _reserved;

    /// <summary>Margin (free cells) kept around structures of this blueprint.</summary>
    public static int MarginFor(UnitBlueprint bp) => bp.Has(UnitCategory.Factory) ? 3 : 2;

    public void Reserve(int bp, Vector2 pos, int builder, int deposit = -1)
    {
        var sbp = _b.W.Blueprints[bp];
        var (x0, z0, x1, z1) = OccupancyGrid.Footprint(sbp, pos);
        int m = deposit >= 0 ? 0 : MarginFor(sbp);
        _reserved.Add(new Reservation
        {
            Bp = bp, Pos = pos, X0 = x0 - m, Z0 = z0 - m, X1 = x1 + m, Z1 = z1 + m,
            Builder = _b.W.Units.HandleOf(builder), Tick = _b.W.Tick, Deposit = deposit,
        });
    }

    /// <summary>Drops reservations whose builder died or no longer has the matching Build order (done/failed).</summary>
    public void Cleanup()
    {
        var u = _b.W.Units;
        for (int r = _reserved.Count - 1; r >= 0; r--)
        {
            var res = _reserved[r];
            bool keep = u.IsActive(res.Builder) && _b.W.Tick - res.Tick < 3000 && HasBuildOrder(res.Builder.Index, res.Bp, res.Pos);
            if (!keep) _reserved.RemoveAt(r);
        }
    }

    private bool HasBuildOrder(int unit, int bp, Vector2 pos)
    {
        foreach (var o in _b.W.Units.Orders[unit].AsSpan())
            if (o.Type == Orders.OrderType.Build && o.BlueprintIndex == bp && Vector2.DistanceSquared(o.Pos, pos) < 1.0f
                && (o.Spawned.IsNone || !_b.W.Units.IsActive(o.Spawned) || !_b.W.Units.IsComplete(o.Spawned.Index)))
                return true;
        return false;
    }

    public bool IsDepositReserved(int deposit)
    {
        foreach (var r in _reserved) if (r.Deposit == deposit) return true;
        return false;
    }

    /// <summary>Number of reserved (not yet started or in progress) builds of a blueprint.</summary>
    public int ReservedCount(int bp)
    {
        int n = 0;
        foreach (var r in _reserved) if (r.Bp == bp) n++;
        return n;
    }

    /// <summary>
    /// Finds a free site for the structure: rings from minR to maxR around center, starting in direction preferDir
    /// and alternating sides; a second pass accepts a 1-cell smaller margin. Returns false if nothing fits. With
    /// cooldownTicks &gt; 0 a failed search for this blueprint is not retried for that long (cramped bases).
    /// </summary>
    public bool FindSite(int bp, Vector2 center, Vector2 preferDir, float minR, float maxR, out Vector2 site, int cooldownTicks = 0)
    {
        var w = _b.W;
        site = default;
        if (_failUntil.Length != w.Blueprints.Count) _failUntil = new int[w.Blueprints.Count];
        if (cooldownTicks > 0 && _failUntil[bp] > w.Tick) return false;
        var sbp = w.Blueprints[bp];
        int margin = MarginFor(sbp);
        float a0 = preferDir.LengthSquared() > 1e-4f ? DMath.HeadingOf(preferDir) : 0;
        _b.Stats.SiteSearches++;
        for (int pass = 0; pass < 2; pass++)
        {
            int m = margin - pass;
            for (float r = minR; r <= maxR; r += 1.5f)
            {
                int steps = Math.Max(8, (int)(DMath.TwoPi * r / 2f));
                for (int s = 0; s < steps; s++)
                {
                    int k = (s + 1) / 2 * ((s & 1) == 1 ? 1 : -1);
                    float a = a0 + k * DMath.TwoPi / steps;
                    var p = OccupancyGrid.SnapStructure(sbp, center + DMath.Dir(a) * r);
                    _b.Stats.SiteCandidates++;
                    if (IsGoodSite(sbp, p, m))
                    {
                        site = p;
                        return true;
                    }
                }
            }
        }
        _b.Stats.SiteFailures++;
        if (cooldownTicks > 0) _failUntil[bp] = w.Tick + cooldownTicks;
        if (Environment.GetEnvironmentVariable("AI_DEBUG") == "1" && !_dumped) { _dumped = true; Console.WriteLine($"[ai {_b.Army} t{w.Tick}] no site for {sbp.Id}\n" + Dump(center, (int)maxR + 6)); }
        return false;
    }

    private int[] _failUntil = [];
    private bool _dumped;

    /// <summary>Debug: ASCII map around a point (# structure, ~ unbuildable, M/H deposit, R rally, r reserved, . free).</summary>
    public string Dump(Vector2 c, int r)
    {
        var w = _b.W;
        var sb = new System.Text.StringBuilder();
        for (int z = (int)c.Y + r; z >= (int)c.Y - r; z--)
        {
            for (int x = (int)c.X - r; x <= (int)c.X + r; x++)
            {
                char ch = '.';
                if (!w.Terrain.CellInBounds(x, z)) ch = ' ';
                else if (w.Occupancy.IsBlocked(x, z)) ch = '#';
                else if ((w.Terrain.PassAt(x, z) & PassFlags.Land) == 0) ch = '~';
                foreach (var res in _reserved) if (x >= res.X0 && x <= res.X1 && z >= res.Z0 && z <= res.Z1 && ch == '.') ch = 'r';
                foreach (var d in w.Terrain.Deposits) if ((int)d.Pos.X == x && (int)d.Pos.Y == z) ch = d.Type == DepositType.Mass ? 'M' : 'H';
                if ((int)_b.Rally.X == x && (int)_b.Rally.Y == z) ch = 'R';
                if ((int)_b.BasePos.X == x && (int)_b.BasePos.Y == z) ch = 'B';
                sb.Append(ch);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Placement rules for base structures (not for extractors on deposits).</summary>
    public bool IsGoodSite(UnitBlueprint sbp, Vector2 p, int margin)
    {
        var w = _b.W;
        if (!w.Terrain.InBounds(p)) return false;
        if (!w.Occupancy.CanPlace(sbp, p, w.Terrain)) return false;
        var (x0, z0, x1, z1) = OccupancyGrid.Footprint(sbp, p);
        int ex0 = x0 - margin, ez0 = z0 - margin, ex1 = x1 + margin, ez1 = z1 + margin;
        if (ex0 < 1 || ez0 < 1 || ex1 >= w.Terrain.Width - 1 || ez1 >= w.Terrain.Height - 1) return false;
        // Margin ring must be free of structures (cells inside the footprint were checked by CanPlace).
        var occ = w.Occupancy;
        for (int z = ez0; z <= ez1; z++)
        for (int x = ex0; x <= ex1; x++)
            if (occ.IsBlocked(x, z)) return false;
        // Factories need walkable ground around them so units can leave.
        if (sbp.Has(UnitCategory.Factory))
        {
            int bad = 0, total = 0;
            for (int z = ez0; z <= ez1; z++)
            for (int x = ex0; x <= ex1; x++)
            {
                if (x >= x0 && x <= x1 && z >= z0 && z <= z1) continue;
                total++;
                if ((w.Terrain.PassAt(x, z) & PassFlags.Land) == 0) bad++;
            }
            if (bad * 4 > total) return false;
        }
        // Keep clear of reservations.
        foreach (var r in _reserved)
            if (ex0 <= r.X1 && ex1 >= r.X0 && ez0 <= r.Z1 && ez1 >= r.Z0) return false;
        // Don't cover deposits (they need their own extractor footprint plus a little room).
        float half = MathF.Max(sbp.FootprintX, sbp.FootprintZ) * 0.5f;
        foreach (var d in w.Terrain.Deposits)
        {
            float clear = half + (d.Type == DepositType.Hydrocarbon ? 3.5f : 2.5f) + margin * 0.5f;
            if (Vector2.DistanceSquared(d.Pos, p) < clear * clear) return false;
        }
        // Keep the rally point (where new units gather) open.
        float rr = half + margin + 5f;
        if (Vector2.DistanceSquared(_b.Rally, p) < rr * rr) return false;
        // Must be connected to the base by land (engineers are amphibious, but units must get out of factories).
        var exit = p + DMath.Dir(DMath.HeadingOf(_b.EnemyDir)) * (half + 1.5f);
        if (sbp.Has(UnitCategory.Factory) && !_b.CanReachNear(MotionLayer.Land, 0.6f, _b.BasePos, exit, 1f)) return false;
        return true;
    }

    /// <summary>Snapped extractor site of a deposit if it is free (no structure, placeable), else null.</summary>
    public bool DepositFree(int deposit, int bp, out Vector2 site)
    {
        var w = _b.W;
        var d = w.Terrain.Deposits[deposit];
        var sbp = w.Blueprints[bp];
        site = OccupancyGrid.SnapStructure(sbp, d.Pos);
        return w.Occupancy.CanPlace(sbp, site, w.Terrain);
    }
}

using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Helpers for economy/construction tests.</summary>
internal static class EconomyTestUtil
{
    public static int Bp(string id) => TestUtil.Db.IndexOf(id);

    public static void Ticks(Simulation sim, int n)
    {
        for (int t = 0; t < n; t++) sim.Step();
    }

    public static Vector2 NearestDeposit(SimWorld w, Vector2 p, DepositType type)
    {
        var best = Vector2.Zero;
        float bd = float.MaxValue;
        foreach (var d in w.Terrain.Deposits)
        {
            if (d.Type != type) continue;
            float d2 = Vector2.DistanceSquared(d.Pos, p);
            if (d2 < bd) { bd = d2; best = d.Pos; }
        }
        return best;
    }

    /// <summary>A valid (CanPlace) snapped site for the structure within maxDist of `near`, not touching any unit
    /// or deposit, scanning outward deterministically.</summary>
    public static Vector2 FindSite(SimWorld w, string bpId, Vector2 near, float minDist = 0, float maxDist = 30)
    {
        var bp = TestUtil.Db[bpId];
        float half = MathF.Max(bp.FootprintX, bp.FootprintZ) * 0.5f;
        for (float r = minDist; r <= maxDist; r += 1f)
        for (int a = 0; a < 16; a++)
        {
            var p = OccupancyGrid.SnapStructure(bp, near + DMath.Dir(a * DMath.TwoPi / 16) * r);
            if (Vector2.Distance(p, near) < minDist || Vector2.Distance(p, near) > maxDist) continue;
            if (!w.Occupancy.CanPlace(bp, p, w.Terrain)) continue;
            bool clear = true;
            for (int i = 0; i < w.Units.Capacity && clear; i++)
                if (w.Units.IsActive(i) && Vector2.Distance(w.Units.Pos[i], p) < half + w.BpOf(i).Radius + 1.5f) clear = false;
            foreach (var d in w.Terrain.Deposits)
                if (Vector2.Distance(d.Pos, p) < half + 2.5f) clear = false;
            if (clear) return p;
        }
        throw new InvalidOperationException($"no site for {bpId} near {near}");
    }

    public static List<int> UnitsOf(SimWorld w, int army, string bpId)
    {
        int bp = Bp(bpId);
        var list = new List<int>();
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.IsActive(i) && w.Units.Army[i] == army && w.Units.Bp[i] == bp) list.Add(i);
        return list;
    }

    public static Vector2 CommanderPos(SimWorld w, int army = 0) => w.Units.Pos[w.Armies[army].Commander.Index];
}

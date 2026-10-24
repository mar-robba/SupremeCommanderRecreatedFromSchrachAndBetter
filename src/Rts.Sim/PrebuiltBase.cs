using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim;

/// <summary>
/// (6b) <see cref="GameOptions.Prebuilt"/>: every army starts with a small finished base next to its commander: up to
/// <see cref="Mexes"/> mass extractors on the nearest free mass deposits (within <see cref="MexRange"/>),
/// <see cref="Pgens"/> T1 power generators behind the commander and one T1 land factory toward the map center.
/// Blueprints come from faction roles (a missing role is skipped). Called once by <see cref="GameSetup.CreateSkirmish"/>
/// after the commanders spawn. Deterministic: integer cell searches in a fixed order, no trig, no hash order.
/// </summary>
public static class PrebuiltBase
{
    public const int Mexes = 4, Pgens = 4;
    public const float MexRange = 40f;
    /// <summary>Min distance between a structure's footprint and any commander (walking room).</summary>
    public const float CommanderClearance = 6f;

    public static void Place(SimWorld w)
    {
        var center = new Vector2(w.Terrain.Width * 0.5f, w.Terrain.Height * 0.5f);
        foreach (var army in w.Armies)
        {
            if (!w.Units.IsActive(army.Commander)) continue;
            var sp = w.Units.Pos[army.Commander.Index];
            var toCenter = center - sp;
            toCenter = toCenter.LengthSquared() > 1e-6f ? Vector2.Normalize(toCenter) : new Vector2(1, 0);
            float heading = DMath.HeadingOf(toCenter);
            var f = w.Blueprints.Factions;

            int mex = f.RoleBlueprint(army.Faction, FactionRoles.MexT1);
            if (mex >= 0) PlaceMexes(w, army.Index, mex, sp, heading);
            int fac = f.RoleBlueprint(army.Faction, FactionRoles.LandFactoryT1);
            if (fac >= 0 && FindSite(w, w.Blueprints[fac], sp + toCenter * 14f, margin: 2) is { } fp)
                w.SpawnUnit(fac, army.Index, fp, heading, complete: true);
            int pgen = f.RoleBlueprint(army.Faction, FactionRoles.PgenT1);
            if (pgen >= 0)
                for (int k = 0; k < Pgens; k++)
                    if (FindSite(w, w.Blueprints[pgen], sp - toCenter * 10f, margin: 1) is { } pp)
                        w.SpawnUnit(pgen, army.Index, pp, heading, complete: true);
        }
    }

    private static void PlaceMexes(SimWorld w, int army, int mex, Vector2 sp, float heading)
    {
        var bp = w.Blueprints[mex];
        var deps = w.Terrain.Deposits;
        // Mass deposits in range, nearest first (ties by index: deterministic).
        var cand = new List<(float D2, int Index)>();
        for (int d = 0; d < deps.Length; d++)
        {
            if (deps[d].Type != Map.DepositType.Mass) continue;
            float d2 = Vector2.DistanceSquared(deps[d].Pos, sp);
            if (d2 <= MexRange * MexRange) cand.Add((d2, d));
        }
        cand.Sort((a, b) => a.D2 != b.D2 ? a.D2.CompareTo(b.D2) : a.Index.CompareTo(b.Index));
        int placed = 0;
        foreach (var (_, d) in cand)
        {
            if (placed >= Mexes) break;
            var site = OccupancyGrid.SnapStructure(bp, deps[d].Pos); // the mex snap rule of ConstructionSystem.ResolveSite
            if (!w.Occupancy.CanPlace(bp, site, w.Terrain) || NearCommander(w, bp, site, 3f)) continue;
            if (!w.SpawnUnit(mex, army, site, heading, complete: true).IsNone) placed++;
        }
    }

    /// <summary>
    /// First free site for bp in square rings (cells) around <paramref name="anchor"/>: placeable, with
    /// <paramref name="margin"/> free cells around the footprint, off mass/hydro deposits and clear of commanders.
    /// </summary>
    private static Vector2? FindSite(SimWorld w, UnitBlueprint bp, Vector2 anchor, int margin)
    {
        int ax = (int)MathF.Floor(anchor.X), az = (int)MathF.Floor(anchor.Y);
        for (int r = 0; r <= 24; r++)
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue; // ring r only
            var p = OccupancyGrid.SnapStructure(bp, new Vector2(ax + dx + 0.5f, az + dz + 0.5f));
            if (Fits(w, bp, p, margin)) return p;
        }
        return null;
    }

    private static bool Fits(SimWorld w, UnitBlueprint bp, Vector2 p, int margin)
    {
        if (!w.Occupancy.CanPlace(bp, p, w.Terrain)) return false;
        var (x0, z0, x1, z1) = OccupancyGrid.Footprint(bp, p);
        for (int z = z0 - margin; z <= z1 + margin; z++)
        for (int x = x0 - margin; x <= x1 + margin; x++)
            if (w.Occupancy.At(x, z) >= 0) return false;
        foreach (var d in w.Terrain.Deposits) // keep deposits free for mexes / hydros
            if (d.Pos.X >= x0 - 1 && d.Pos.X <= x1 + 2 && d.Pos.Y >= z0 - 1 && d.Pos.Y <= z1 + 2) return false;
        return !NearCommander(w, bp, p, CommanderClearance);
    }

    /// <summary>Is the footprint of bp at p closer than <paramref name="clearance"/> to any commander?</summary>
    private static bool NearCommander(SimWorld w, UnitBlueprint bp, Vector2 p, float clearance)
    {
        foreach (var a in w.Armies)
        {
            if (!w.Units.IsActive(a.Commander)) continue;
            var c = w.Units.Pos[a.Commander.Index];
            float dx = MathF.Max(MathF.Abs(c.X - p.X) - bp.FootprintX * 0.5f, 0);
            float dz = MathF.Max(MathF.Abs(c.Y - p.Y) - bp.FootprintZ * 0.5f, 0);
            if (dx * dx + dz * dz < clearance * clearance) return true;
        }
        return false;
    }
}

using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Map;

/// <summary>
/// (8b-2) The map generator's guarantees as a check for any terrain (map files, <c>Rts.Bench -- mapexport</c>):
/// starts connected (over land; over land + water surface for naval maps), deposits buildable by every faction's
/// extractor / hydrocarbon plant, flat buildable start areas with a factory site, base resources per start.
/// Same rules as <c>MapGeneratorTests</c>. Faction roles only (no blueprint ids).
/// </summary>
public static class MapChecks
{
    /// <summary>Radius of the flat buildable area every start needs (as the generator's start plateaus).</summary>
    public const float StartAreaRadius = 16f;

    /// <summary>Problems found (empty = the map passes). <paramref name="naval"/>: bases may be separated by water
    /// (Archipelago), then they must be connected over land + water surface (hover/naval reach).</summary>
    public static List<string> Check(Terrain t, BlueprintDb db, bool naval)
    {
        var errors = new List<string>();
        var starts = t.StartPositions;
        if (starts.Length < 2) errors.Add($"{starts.Length} start positions (needs 2 or more)");
        if (starts.Length > 8) errors.Add($"{starts.Length} start positions (at most 8)");
        if (starts.Length == 0) return errors;

        if (!naval && !MapGenerator.StartsConnectedByLand(t)) errors.Add("start positions are not all connected by land");
        else if (naval && !Connected(t, MotionLayer.Hover)) errors.Add("start positions are not all connected over land and water");

        var occ = new OccupancyGrid(t.Width, t.Height);
        foreach (var f in db.Factions.All)
        {
            int mex = db.Factions.RoleBlueprint(f.Id, FactionRoles.MexT1), hydro = db.Factions.RoleBlueprint(f.Id, FactionRoles.Hydro);
            int factory = db.Factions.RoleBlueprint(f.Id, FactionRoles.LandFactoryT1);
            foreach (var d in t.Deposits)
            {
                int bpi = d.Type == DepositType.Mass ? mex : hydro;
                if (bpi < 0) continue;
                var bp = db[bpi];
                if (!occ.CanPlace(bp, OccupancyGrid.SnapStructure(bp, d.Pos), t))
                    errors.Add($"{d.Type} deposit at {d.Pos.X:0.#},{d.Pos.Y:0.#} is not buildable ({f.Id})");
            }
            if (factory >= 0)
                foreach (var sp in starts)
                {
                    var fb = db[factory];
                    if (!occ.CanPlace(fb, OccupancyGrid.SnapStructure(fb, sp + new Vector2(9, 0)), t))
                        errors.Add($"no land factory site next to start {sp.X:0},{sp.Y:0} ({f.Id})");
                }
        }

        foreach (var sp in starts)
        {
            int bad = 0;
            for (int z = (int)(sp.Y - StartAreaRadius); z <= (int)(sp.Y + StartAreaRadius); z++)
            for (int x = (int)(sp.X - StartAreaRadius); x <= (int)(sp.X + StartAreaRadius); x++)
                if (Vector2.Distance(new Vector2(x + 0.5f, z + 0.5f), sp) <= StartAreaRadius && (t.PassAt(x, z) & PassFlags.Land) == 0) bad++;
            if (bad > 0) errors.Add($"start {sp.X:0},{sp.Y:0}: {bad} cells of the start area are not buildable land");
            int mass = t.Deposits.Count(d => d.Type == DepositType.Mass && Vector2.Distance(d.Pos, sp) < 20f);
            int hyd = t.Deposits.Count(d => d.Type == DepositType.Hydrocarbon && Vector2.Distance(d.Pos, sp) < 25f);
            if (mass < 4) errors.Add($"start {sp.X:0},{sp.Y:0}: {mass} base mass deposits (needs 4)");
            if (hyd < 1) errors.Add($"start {sp.X:0},{sp.Y:0}: no hydrocarbon near the base");
        }
        return errors;
    }

    /// <summary>True if every start is reachable from the first one for a motion layer (4-connected flood fill).</summary>
    public static bool Connected(Terrain t, MotionLayer layer)
    {
        int w = t.Width, h = t.Height;
        var seen = new bool[w * h];
        var q = new Queue<int>();
        var s0 = t.StartPositions[0];
        int c0 = Math.Clamp((int)s0.Y, 0, h - 1) * w + Math.Clamp((int)s0.X, 0, w - 1);
        if (!t.CanTraverse(layer, c0 % w, c0 / w)) return false;
        seen[c0] = true;
        q.Enqueue(c0);
        while (q.Count > 0)
        {
            int c = q.Dequeue(), cx = c % w, cz = c / w;
            Span<int> nx = [cx - 1, cx + 1, cx, cx];
            Span<int> nz = [cz, cz, cz - 1, cz + 1];
            for (int k = 0; k < 4; k++)
            {
                if (!t.CellInBounds(nx[k], nz[k])) continue;
                int n = nz[k] * w + nx[k];
                if (seen[n] || !t.CanTraverse(layer, nx[k], nz[k])) continue;
                seen[n] = true;
                q.Enqueue(n);
            }
        }
        foreach (var sp in t.StartPositions)
            if (!seen[Math.Clamp((int)sp.Y, 0, h - 1) * w + Math.Clamp((int)sp.X, 0, w - 1)]) return false;
        return true;
    }
}

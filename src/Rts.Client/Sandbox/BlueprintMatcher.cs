using Rts.Sim.Data;

namespace Rts.Client.Sandbox;

/// <summary>
/// Fuzzy lookup of a unit blueprint from what the player typed (case-insensitive). Rank, best first:
/// exact id, exact name, id ends with "_"+query, id starts with query, id contains query, name contains query;
/// ties go to the shortest id, then id ordinal. '_' and ' ' are equivalent (so "t1 tank", "ma12_striker" and
/// "ma12 striker" all work). Works on whatever is in the BlueprintDb: no ids are hardcoded. Generated enhancement
/// variants (<see cref="UnitBlueprint.IsVariant"/>) only match their exact id.
/// </summary>
public static class BlueprintMatcher
{
    private const int NoMatch = int.MaxValue;

    public static UnitBlueprint? Find(BlueprintDb db, string query)
    {
        string q = Normalize(query);
        if (q.Length == 0) return null;
        UnitBlueprint? best = null;
        int bestRank = NoMatch;
        foreach (var bp in db.All)
        {
            int r = Rank(bp, q);
            if (r == NoMatch) continue;
            if (best == null || Better(r, bp, bestRank, best)) { best = bp; bestRank = r; }
        }
        return best;
    }

    /// <summary>Every matching blueprint, best first, at most max.</summary>
    public static List<UnitBlueprint> Candidates(BlueprintDb db, string query, int max)
    {
        var result = new List<UnitBlueprint>();
        string q = Normalize(query);
        if (q.Length == 0 || max <= 0) return result;
        var ranked = new List<(int Rank, UnitBlueprint Bp)>();
        foreach (var bp in db.All)
        {
            int r = Rank(bp, q);
            if (r != NoMatch) ranked.Add((r, bp));
        }
        ranked.Sort((a, b) =>
        {
            int c = a.Rank.CompareTo(b.Rank);
            if (c != 0) return c;
            c = a.Bp.Id.Length.CompareTo(b.Bp.Id.Length);
            return c != 0 ? c : string.CompareOrdinal(a.Bp.Id, b.Bp.Id);
        });
        for (int i = 0; i < ranked.Count && result.Count < max; i++) result.Add(ranked[i].Bp);
        return result;
    }

    private static bool Better(int rank, UnitBlueprint bp, int bestRank, UnitBlueprint best)
    {
        if (rank != bestRank) return rank < bestRank;
        if (bp.Id.Length != best.Id.Length) return bp.Id.Length < best.Id.Length;
        return string.CompareOrdinal(bp.Id, best.Id) < 0;
    }

    /// <summary>Lower-case, trimmed, spaces folded to '_'.</summary>
    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var parts = s.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join('_', parts);
    }

    private static int Rank(UnitBlueprint bp, string q)
    {
        string id = bp.Id.ToLowerInvariant().Replace(' ', '_');
        string name = Normalize(bp.Name);
        if (id == q) return 0;
        if (bp.IsVariant) return NoMatch; // enhancement variants ("uef_commander+X+Y"): exact id only
        if (name.Length > 0 && name == q) return 1;
        if (id.EndsWith("_" + q, StringComparison.Ordinal)) return 2;
        if (id.StartsWith(q, StringComparison.Ordinal)) return 3;
        if (id.Contains(q, StringComparison.Ordinal)) return 4;
        if (name.Contains(q, StringComparison.Ordinal)) return 5;
        return NoMatch;
    }
}

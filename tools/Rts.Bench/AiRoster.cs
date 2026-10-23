using Rts.Sim.World;

/// <summary>
/// AI match diagnostics (env <c>AI_ROSTER=1</c>): what each army fields. Sampled at every status line; prints, per army,
/// the largest number of units of each blueprint alive at a sample (complete or not), e.g. to check that a faction's AI
/// reaches T2/T3, navy, air and experimentals.
/// </summary>
static class AiRoster
{
    public static bool Enabled => Environment.GetEnvironmentVariable("AI_ROSTER") == "1";

    private static int[,]? _max;

    public static void Sample(SimWorld w)
    {
        int armies = w.Armies.Length, bps = w.Blueprints.Count;
        _max ??= new int[armies, bps];
        var now = new int[armies, bps];
        var u = w.Units;
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            int a = u.Army[i];
            if ((uint)a < (uint)armies) now[a, u.Bp[i]]++;
        }
        for (int a = 0; a < armies; a++)
            for (int b = 0; b < bps; b++)
                if (now[a, b] > _max[a, b]) _max[a, b] = now[a, b];
    }

    public static void Print(SimWorld w)
    {
        if (_max == null) return;
        for (int a = 0; a < w.Armies.Length; a++)
        {
            var list = new List<(int n, string id)>();
            for (int b = 0; b < w.Blueprints.Count; b++)
                if (_max[a, b] > 0) list.Add((_max[a, b], w.Blueprints[b].Id));
            list.Sort((x, y) => y.n != x.n ? y.n.CompareTo(x.n) : string.CompareOrdinal(x.id, y.id));
            Console.WriteLine($"  roster {w.Armies[a].Name} ({list.Count} types, max alive): " + string.Join(", ", list.Select(e => $"{e.id}={e.n}")));
        }
    }
}

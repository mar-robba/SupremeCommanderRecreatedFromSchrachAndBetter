using Rts.Sim.World;

/// <summary>Diagnostics for AI matches: kills per killer blueprint for each army (from the veterancy kill counters,
/// scanned every tick), printed at the end as <c>kills by: id n, ...</c> (who kills the attackers).</summary>
public sealed class KillTracker
{
    private readonly Dictionary<string, int>[] _byBp;
    private int[] _gen = [], _kills = [];

    public KillTracker(int armies)
    {
        _byBp = new Dictionary<string, int>[armies];
        for (int a = 0; a < armies; a++) _byBp[a] = new Dictionary<string, int>();
    }

    public void Scan(SimWorld w)
    {
        var u = w.Units;
        if (_gen.Length != u.Capacity) { _gen = new int[u.Capacity]; Array.Fill(_gen, -1); _kills = new int[u.Capacity]; }
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            if (_gen[i] != u.Gen[i]) { _gen[i] = u.Gen[i]; _kills[i] = 0; }
            int d = u.Kills[i] - _kills[i];
            if (d <= 0) continue;
            _kills[i] = u.Kills[i];
            int a = u.Army[i];
            if ((uint)a >= (uint)_byBp.Length) continue;
            string id = w.BpOf(i).Id;
            _byBp[a][id] = _byBp[a].GetValueOrDefault(id) + d;
        }
    }

    public string Format(int army, int top = 6) =>
        string.Join(", ", _byBp[army].OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(top).Select(kv => $"{kv.Key} {kv.Value}"));
}

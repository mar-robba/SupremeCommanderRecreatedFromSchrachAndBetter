using Rts.Sim;
using Rts.Sim.Core;

/// <summary>Per-stage tick cost (from <see cref="Simulation.Profile"/>) and managed allocations per tick, for the bench output.</summary>
public sealed class StageStats
{
    private readonly double[] _sum = new double[SimProfile.Count];
    private readonly double[] _max = new double[SimProfile.Count];
    private readonly long[] _alloc = new long[SimProfile.Count];
    private bool _trackAllocs;
    private int _ticks;
    private long _alloc0 = -1;

    /// <summary>Call once before the measured ticks (starts the allocation counter).</summary>
    public void Start(Simulation sim)
    {
        // BENCH_ALLOC=1: allocations per stage (slower ticks; for finding allocations, not for timings).
        _trackAllocs = sim.Profile.TrackAllocations = Environment.GetEnvironmentVariable("BENCH_ALLOC") == "1";
        _alloc0 = GC.GetTotalAllocatedBytes(precise: true);
    }

    /// <summary>Call after each measured <see cref="Simulation.Step"/>.</summary>
    public void Add(Simulation sim)
    {
        var ms = sim.Profile.LastMs;
        for (int i = 0; i < SimProfile.Count; i++)
        {
            _sum[i] += ms[i];
            if (ms[i] > _max[i]) _max[i] = ms[i];
            _alloc[i] += sim.Profile.LastAlloc[i];
        }
        _ticks++;
    }

    /// <summary>Stages of the last tick costing at least 0.5 ms (e.g. the first tick: navigation build, JIT).</summary>
    public static string LastTick(Simulation sim) => string.Join(" ", Enumerable.Range(0, SimProfile.Count)
        .Where(i => sim.Profile.LastMs[i] >= 0.5).Select(i => $"{SimProfile.Names[i]}={sim.Profile.LastMs[i]:F1}"));

    /// <summary>"stage avg/max" for the stages costing at least 0.01 ms on average, largest first, plus KB allocated per tick
    /// (all threads; includes whatever the caller allocates between ticks).</summary>
    public string Format()
    {
        int n = Math.Max(1, _ticks);
        var idx = Enumerable.Range(0, SimProfile.Count).OrderByDescending(i => _sum[i]).Where(i => _sum[i] / n >= 0.01);
        string stages = string.Join(" ", idx.Select(i => $"{SimProfile.Names[i]}={_sum[i] / n:F2}/{_max[i]:F1}"));
        string alloc = _alloc0 >= 0 ? $" | alloc {(GC.GetTotalAllocatedBytes(precise: true) - _alloc0) / 1024.0 / n:F1} KB/tick, gc0 {GC.CollectionCount(0)} gc2 {GC.CollectionCount(2)}" : "";
        if (_trackAllocs)
            alloc += "\n  alloc KB/tick by stage: " + string.Join(" ", Enumerable.Range(0, SimProfile.Count).OrderByDescending(i => _alloc[i])
                .Where(i => _alloc[i] > 0).Select(i => $"{SimProfile.Names[i]}={_alloc[i] / 1024.0 / n:F1}"));
        return "stages avg/max ms: " + stages + alloc;
    }
}

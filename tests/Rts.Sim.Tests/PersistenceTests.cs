using Rts.Sim.Persistence;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

public class PersistenceTests
{
    private readonly ITestOutputHelper _out;
    public PersistenceTests(ITestOutputHelper output) => _out = output;

    private static Simulation Restore(byte[] state, Simulation like) =>
        WorldState.Restore(state, like.World.Blueprints, like.World.Terrain);

    [Fact]
    public void SmallWorldRoundTripsToIdenticalBytes()
    {
        var sim = TestUtil.NewSkirmish(seed: 3);
        TestUtil.Run(sim, 50);
        var bytes = WorldState.Capture(sim);
        var loaded = Restore(bytes, sim);
        Assert.Equal(sim.World.Tick, loaded.World.Tick);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        var again = WorldState.Capture(loaded);
        Assert.True(bytes.AsSpan().SequenceEqual(again), $"differs at {FirstDiff(bytes, again)}: {WorldState.Locate(sim, FirstDiff(bytes, again))}");
    }

    /// <summary>
    /// The key guarantee: save at tick N, load, continue to M == the uninterrupted game at M, both by StateHash and by
    /// the complete serialized state (every saved field of every object). Rich scenario: 4 AIs incl. Hard, sandbox
    /// spawn of every blueprint (strategic, naval, shields, silos, upgrades), launches and a nuke after the load.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void SaveLoadContinueMatchesUninterruptedGame(int threads)
    {
        PersistenceTestUtil.WithThreads(threads, () =>
        {
            const int saveAt = 1100, endAt = 2200;
            var a = PersistenceTestUtil.NewRich();
            PersistenceTestUtil.RunTo(a, saveAt);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var saved = WorldState.Capture(a);
            double captureMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var b = Restore(saved, a);
            double restoreMs = sw.Elapsed.TotalMilliseconds;
            _out.WriteLine($"threads {threads}: units {a.World.Units.AliveCount}, state {saved.Length / 1024} KB, capture {captureMs:F1} ms, restore {restoreMs:F1} ms");
            var again = WorldState.Capture(b);
            Assert.True(saved.AsSpan().SequenceEqual(again), $"reload differs at {FirstDiff(saved, again)}: {WorldState.Locate(a, FirstDiff(saved, again))}");

            for (int t = saveAt + 100; t <= endAt; t += 100)
            {
                PersistenceTestUtil.RunTo(a, t);
                PersistenceTestUtil.RunTo(b, t);
                Assert.True(a.StateHash() == b.StateHash(), $"StateHash differs at tick {t}");
            }
            var endA = WorldState.Capture(a);
            var endB = WorldState.Capture(b);
            Assert.True(endA.AsSpan().SequenceEqual(endB), $"serialized state differs at tick {endAt}: {WorldState.Locate(a, FirstDiff(endA, endB))}");
            return 0;
        });
    }

    private static int FirstDiff(byte[] x, byte[] y)
    {
        int n = Math.Min(x.Length, y.Length);
        for (int i = 0; i < n; i++) if (x[i] != y[i]) return i;
        return n;
    }
}

public class PersistenceSizeProbe
{
    private readonly ITestOutputHelper _out;
    public PersistenceSizeProbe(ITestOutputHelper output) => _out = output;

    /// <summary>Diagnostic: which types take the space in a capture (run explicitly with a filter).</summary>
    [Fact]
    public void PrintStateSizeByType()
    {
        var sim = PersistenceTestUtil.NewRich();
        PersistenceTestUtil.RunTo(sim, 300);
        var map = new List<(int Offset, Type Type)>();
        var o = new ByteWriter(1 << 20);
        WorldState.Capture(sim, o, map);
        var sizes = new Dictionary<string, (long bytes, int count)>();
        for (int i = 0; i < map.Count; i++)
        {
            int end = i + 1 < map.Count ? map[i + 1].Offset : o.Length;
            string k = map[i].Type.ToString();
            var s = sizes.GetValueOrDefault(k);
            sizes[k] = (s.bytes + end - map[i].Offset, s.count + 1);
        }
        _out.WriteLine($"total {o.Length / 1024} KB, objects {map.Count}");
        foreach (var kv in sizes.OrderByDescending(k => k.Value.bytes).Take(25))
            _out.WriteLine($"{kv.Value.bytes / 1024,8} KB {kv.Value.count,7}x {kv.Key}");
    }
}

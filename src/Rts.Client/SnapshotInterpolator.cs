using System.Diagnostics;
using Rts.Sim;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client;

/// <summary>
/// Holds the previous and current sim snapshots and computes the interpolation factor so units move smoothly
/// at any framerate even though the sim ticks at 10 Hz (rendering is one tick behind the sim).
/// </summary>
public sealed class SnapshotInterpolator
{
    private readonly SimRunner _runner;
    public SimSnapshot? Prev { get; private set; }
    public SimSnapshot Current { get; private set; }
    /// <summary>Effects that arrived this frame (from all snapshots consumed since last frame).</summary>
    public readonly List<EffectEvent> NewEffects = new();

    public SnapshotInterpolator(SimRunner runner)
    {
        _runner = runner;
        Current = runner.TakeLatest() ?? throw new InvalidOperationException("runner has no initial snapshot");
    }

    public void Update()
    {
        NewEffects.Clear();
        var s = _runner.TakeLatest();
        if (s == null) return;
        NewEffects.AddRange(s.Effects);
        if (Prev != null) _runner.Return(Prev);
        Prev = Current;
        Current = s;
    }

    /// <summary>0..1 between Prev and Current.</summary>
    public float Alpha
    {
        get
        {
            if (Prev == null) return 1f;
            double tickSec = 1.0 / (SimWorld.TicksPerSecond * Math.Max(0.05f, _runner.Speed));
            double elapsed = (Stopwatch.GetTimestamp() - Current.PublishedAt) / (double)Stopwatch.Frequency;
            return _runner.Paused ? 1f : (float)Math.Clamp(elapsed / tickSec, 0, 1);
        }
    }
}

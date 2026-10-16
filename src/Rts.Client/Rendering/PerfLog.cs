using System.Diagnostics;
using Rts.Sim.Snapshot;

namespace Rts.Client.Rendering;

/// <summary><c>--perf-log</c>: once per second prints fps, the frame stage breakdown, allocations and the sim tick time.</summary>
public static class PerfLog
{
    public static bool Enabled;
    private static long _last;
    private static int _frames;

    public static void Tick(FrameProfiler prof, PerfOverlay perf, SimSnapshot cur)
    {
        if (!Enabled) return;
        _frames++;
        long now = Stopwatch.GetTimestamp();
        if (_last == 0) { _last = now; _frames = 0; return; }
        double s = (now - _last) / (double)Stopwatch.Frequency;
        if (s < 1.0) return;
        Console.WriteLine($"perf-log: {_frames / s:F0} fps, units {cur.UnitCount}, sim {cur.TickDurationMs:F2} ms, gc0 {GC.CollectionCount(0)} gc2 {GC.CollectionCount(2)} | {prof.TakeWindow()}");
        _last = now;
        _frames = 0;
    }
}

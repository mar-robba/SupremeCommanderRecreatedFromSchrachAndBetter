using System.Diagnostics;
using System.Text;

namespace Rts.Client.Rendering;

/// <summary>
/// CPU time per stage of the in-game frame (laps between <see cref="Begin"/> and successive <see cref="Lap"/> calls),
/// averaged after a warm-up, plus managed allocations per frame. Printed with the screenshot perf line and by
/// <c>--perf-log</c> (once per second). Costs a few timestamp reads per frame. With <see cref="GpuEnabled"/>
/// (screenshot / --perf-log runs) each stage also gets its GPU time from <see cref="GpuTimer"/> timestamps.
/// </summary>
public sealed class FrameProfiler
{
    /// <summary>Set before the session is created (screenshot and --perf-log runs).</summary>
    public static bool GpuEnabled;
    private readonly GpuTimer? _gpu = GpuEnabled ? new GpuTimer() : null;
    private readonly int[][] _gpuNames = Enumerable.Range(0, GpuTimer.Frames).Select(_ => new int[GpuTimer.MaxMarks]).ToArray();
    private readonly double[] _gpuStage = new double[GpuTimer.MaxMarks];
    private readonly List<double> _gpuSum = new();
    private readonly List<double> _gpuWindow = new();
    private int _gpuFrames, _gpuWindowFrames, _gpuMark, _gpuSlot;
    private const int Warmup = 30;
    private readonly List<string> _names = new();
    private readonly List<double> _sum = new();
    private readonly List<double> _max = new();
    private readonly List<double> _window = new();
    private readonly List<double> _allocStage = new(); // (10b-2) bytes per stage in the current window
    private long _t, _alloc0, _allocLap;
    private int _frames, _windowFrames;
    private double _allocSum, _allocWindow;

    /// <summary>The profiler of the frame being drawn (null outside a profiled frame).</summary>
    public static FrameProfiler? Active;

    /// <summary>Sub-stage lap from inside a module (e.g. "fx:particles"): splits the enclosing stage. No-op when
    /// no frame is being profiled.</summary>
    public static void Sub(string name) => Active?.Lap(name);

    public void Begin()
    {
        Active = this;
        if (_gpu != null)
        {
            int n = _gpu.BeginFrame(_gpuStage);
            _gpuSlot = (_gpuSlot + 1) % GpuTimer.Frames; // same rotation as GpuTimer (its slot = frame % Frames)
            if (n > 0 && _frames >= Warmup)
            {
                var names = _gpuNames[_gpuSlot];
                for (int i = 0; i < n; i++)
                {
                    int k = names[i + 1];
                    while (_gpuSum.Count <= k) { _gpuSum.Add(0); _gpuWindow.Add(0); }
                    _gpuSum[k] += _gpuStage[i];
                    _gpuWindow[k] += _gpuStage[i];
                }
                _gpuFrames++;
                _gpuWindowFrames++;
            }
            _gpuMark = 0;
            _gpuNames[_gpuSlot][_gpuMark++] = -1;
            _gpu.Mark();
        }
        _t = Stopwatch.GetTimestamp();
        _alloc0 = GC.GetAllocatedBytesForCurrentThread();
        _allocLap = _alloc0;
    }

    /// <summary>Ends the current stage (time since Begin or the previous Lap) under <paramref name="name"/>.</summary>
    public void Lap(string name)
    {
        long now = Stopwatch.GetTimestamp();
        double ms = (now - _t) * 1000.0 / Stopwatch.Frequency;
        _t = now;
        int i = _names.IndexOf(name);
        if (i < 0) { _names.Add(name); _sum.Add(0); _max.Add(0); _window.Add(0); _allocStage.Add(0); i = _names.Count - 1; }
        long al = GC.GetAllocatedBytesForCurrentThread();
        if (_frames >= Warmup) _allocStage[i] += al - _allocLap;
        _allocLap = al;
        if (_gpu != null && _gpuMark < GpuTimer.MaxMarks)
        {
            _gpuNames[_gpuSlot][_gpuMark++] = i;
            _gpu.Mark();
            _t = Stopwatch.GetTimestamp(); // the batch flush belongs to the stage that queued the draws
            ms = (_t - now) * 1000.0 / Stopwatch.Frequency + ms;
        }
        if (_frames < Warmup) return;
        _sum[i] += ms;
        _window[i] += ms;
        if (ms > _max[i]) _max[i] = ms;
    }

    /// <summary>Call once at the end of the frame.</summary>
    public void End()
    {
        Active = null;
        if (_frames >= Warmup)
        {
            double a = GC.GetAllocatedBytesForCurrentThread() - _alloc0;
            _allocSum += a;
            _allocWindow += a;
            _windowFrames++;
        }
        _frames++;
    }

    private int Measured => Math.Max(1, _frames - Warmup);

    /// <summary>Average ms per stage over the whole run (largest first) and allocations per frame.</summary>
    public string Summary() => Format(_sum, Measured, _allocSum, withMax: true) + FormatGpu(_gpuSum, _gpuFrames);

    /// <summary>Averages since the previous call (for --perf-log), then resets the window.</summary>
    public string TakeWindow()
    {
        var s = Format(_window, Math.Max(1, _windowFrames), _allocWindow, withMax: false) + FormatGpu(_gpuWindow, _gpuWindowFrames);
        for (int i = 0; i < _window.Count; i++) { _window[i] = 0; _allocStage[i] = 0; }
        for (int i = 0; i < _gpuWindow.Count; i++) _gpuWindow[i] = 0;
        _gpuWindowFrames = 0;
        _windowFrames = 0;
        _allocWindow = 0;
        return s;
    }

    private string FormatGpu(List<double> sums, int frames)
    {
        if (_gpu == null) return "";
        if (!_gpu.Ok || frames == 0) return "\ngpu: timer queries unavailable";
        double total = sums.Sum() / frames;
        var sb = new StringBuilder($"\ngpu ms: total={total:F2}");
        foreach (int i in Enumerable.Range(0, sums.Count).OrderByDescending(i => sums[i]))
            if (sums[i] / frames >= 0.01) sb.Append(' ').Append(_names[i]).Append('=').Append((sums[i] / frames).ToString("F2"));
        return sb.ToString();
    }

    private string Format(List<double> sums, int frames, double alloc, bool withMax)
    {
        var idx = Enumerable.Range(0, _names.Count).OrderByDescending(i => sums[i]).ToArray();
        var sb = new StringBuilder("stages ms:");
        foreach (int i in idx)
        {
            sb.Append(' ').Append(_names[i]).Append('=').Append((sums[i] / frames).ToString("F2"));
            if (withMax) sb.Append('/').Append(_max[i].ToString("F1"));
        }
        sb.Append($" | alloc {alloc / frames / 1024.0:F1} KB/frame (main thread)");
        if (!withMax) // (10b-2) --perf-log: the stages that allocate >= 1 KB/frame, largest first
            foreach (int i in Enumerable.Range(0, _allocStage.Count).OrderByDescending(i => _allocStage[i]).Take(4))
                if (_allocStage[i] / frames >= 1024) sb.Append(' ').Append(_names[i]).Append('=').Append((_allocStage[i] / frames / 1024.0).ToString("F1")).Append("KB");
        return sb.ToString();
    }
}

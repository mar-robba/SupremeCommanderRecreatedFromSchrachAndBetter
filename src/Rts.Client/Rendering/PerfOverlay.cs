using System.Diagnostics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>
/// Frame-time statistics shown under the HUD top bar (frame time incl. vsync wait, CPU time spent building
/// the frame, render counters). F8 toggles it. <see cref="Summary"/> is printed in screenshot mode.
/// </summary>
public sealed class PerfOverlay
{
    private readonly float[] _frame = new float[120];
    private readonly float[] _cpu = new float[120];
    private int _n, _i;
    private long _start;
    private double _sumFrame, _sumCpu;
    private int _measured, _skipped;
    private float _maxFrame;
    public bool Visible = true;

    public void BeginFrame()
    {
        // Record the previous frame (raylib measures the full frame time, including the vsync wait).
        if (_start != 0)
        {
            float frame = Raylib.GetFrameTime() * 1000f;
            _frame[_i] = frame;
            _i = (_i + 1) % _frame.Length;
            _n = Math.Min(_n + 1, _frame.Length);
            if (_skipped < 30) _skipped++;
            else { _sumFrame += frame; _measured++; _maxFrame = MathF.Max(_maxFrame, frame); }
        }
        _start = Stopwatch.GetTimestamp();
        if (Input.Hotkeys.Perf.Pressed()) Visible = !Visible;
    }

    private void MarkCpu()
    {
        float cpu = (float)((Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency);
        _cpu[(_i + _frame.Length - 1) % _frame.Length] = cpu;
        if (_measured > 0) _sumCpu += cpu;
    }

    public void Draw(UnitRenderer units, EffectsRenderer fx, int y = 34)
    {
        MarkCpu();
        if (!Visible || _n == 0) return;
        float avg = 0, max = 0, cpu = 0;
        for (int k = 0; k < _n; k++) { avg += _frame[k]; max = MathF.Max(max, _frame[k]); cpu += _cpu[k]; }
        avg /= _n;
        cpu /= _n;
        string s = $"frame {avg:F1} ms ({1000f / MathF.Max(0.01f, avg):F0} fps, max {max:F1})  cpu {cpu:F1} ms  |  units drawn {units.DrawnUnits}  draws {units.DrawCalls}  |  particles {fx.ParticleCount}  proj {fx.ProjectileCount}  terrain {TerrainRenderer.TrianglesDrawn / 1000}k tris";
        int sw = Raylib.GetScreenWidth();
        int w = Raylib.MeasureText(s, 14);
        Raylib.DrawRectangle(sw - w - 16, y, w + 12, 20, new Color(15, 20, 28, 170));
        Raylib.DrawText(s, sw - w - 10, y + 3, 14, avg > 20f ? new Color(255, 170, 90, 255) : new Color(170, 230, 170, 255));
    }

    public string Summary() =>
        _measured == 0 ? "perf: not enough frames"
            : $"perf: avg frame {_sumFrame / _measured:F2} ms ({1000.0 / (_sumFrame / _measured):F1} fps), max {_maxFrame:F1} ms, avg cpu {_sumCpu / _measured:F2} ms over {_measured} frames";
}

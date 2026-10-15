using System.Runtime.InteropServices;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>
/// GPU timestamps (GL_ARB_timer_query, core in GL 3.3) for <see cref="FrameProfiler"/> stages. The GL entry points
/// come from the glad function pointers exported by raylib's native library. Results are read
/// <see cref="Frames"/> frames later, so the queries never stall the pipeline. Profiling only: each mark flushes
/// raylib's 2D batch so the batched draws are attributed to the stage that issued them.
/// </summary>
public sealed unsafe class GpuTimer
{
    private const uint GlTimestamp = 0x8E28, GlQueryResult = 0x8866, GlQueryResultAvailable = 0x8867;
    public const int Frames = 4, MaxMarks = 32;

    private readonly delegate* unmanaged<int, uint*, void> _gen;
    private readonly delegate* unmanaged<uint, uint, void> _counter;
    private readonly delegate* unmanaged<uint, uint, ulong*, void> _getU64;
    private readonly delegate* unmanaged<uint, uint, int*, void> _getI;
    private readonly uint[] _ids = new uint[Frames * MaxMarks];
    private readonly int[] _marks = new int[Frames];
    private int _frame;
    public bool Ok { get; }

    public GpuTimer()
    {
        try
        {
            if (!NativeLibrary.TryLoad("raylib", typeof(Raylib).Assembly, null, out var lib)) return;
            nint Fn(string name) => NativeLibrary.TryGetExport(lib, "glad_" + name, out var v) ? *(nint*)v : 0;
            nint gen = Fn("glGenQueries"), counter = Fn("glQueryCounter"), u64 = Fn("glGetQueryObjectui64v"), gi = Fn("glGetQueryObjectiv");
            if (gen == 0 || counter == 0 || u64 == 0 || gi == 0) return;
            _gen = (delegate* unmanaged<int, uint*, void>)gen;
            _counter = (delegate* unmanaged<uint, uint, void>)counter;
            _getU64 = (delegate* unmanaged<uint, uint, ulong*, void>)u64;
            _getI = (delegate* unmanaged<uint, uint, int*, void>)gi;
            fixed (uint* p = _ids) _gen(_ids.Length, p);
            Ok = _ids[0] != 0;
        }
        catch (Exception) { Ok = false; }
    }

    /// <summary>Starts a new frame. If the results of the frame issued <see cref="Frames"/>-1 frames ago are ready,
    /// fills <paramref name="stageMs"/> (index = mark - 1) and returns the number of stages, else -1.</summary>
    public int BeginFrame(double[] stageMs)
    {
        if (!Ok) return -1;
        _frame++;
        int slot = _frame % Frames;
        int n = _marks[slot];
        _marks[slot] = 0;
        if (n < 2) return -1;
        int avail = 0;
        _getI(_ids[slot * MaxMarks + n - 1], GlQueryResultAvailable, &avail);
        if (avail == 0) return -1;
        ulong prev = 0;
        for (int i = 0; i < n; i++)
        {
            ulong t;
            _getU64(_ids[slot * MaxMarks + i], GlQueryResult, &t);
            if (i > 0) stageMs[i - 1] = (t - prev) / 1e6;
            prev = t;
        }
        return n - 1;
    }

    /// <summary>Records a GPU timestamp after flushing raylib's pending batch.</summary>
    public void Mark()
    {
        if (!Ok) return;
        int slot = _frame % Frames;
        if (_marks[slot] >= MaxMarks) return;
        Rlgl.DrawRenderBatchActive();
        _counter(_ids[slot * MaxMarks + _marks[slot]++], GlTimestamp);
    }
}

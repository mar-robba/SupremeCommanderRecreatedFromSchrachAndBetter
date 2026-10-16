using System.Diagnostics;
using Rts.Sim.Core;

/// <summary>
/// <c>Rts.Bench parbench [threads] [calls]</c>: overhead of one <see cref="Par.For"/> call (µs) and the bytes it
/// allocates, for a few (count, chunkSize) shapes like the sim's call sites, back to back and with a 50 µs serial gap
/// between calls (as between the systems of a tick).
/// </summary>
public static class ParBench
{
    private static long[] _sink = new long[64 * 1024];

    public static void Run(string[] args)
    {
        if (args.Length > 0 && int.Parse(args[0]) > 0) Par.MaxThreads = int.Parse(args[0]);
        int calls = args.Length > 1 ? int.Parse(args[1]) : 20000;
        Console.WriteLine($"Par.For overhead, threads={Par.MaxThreads}, calls={calls}");
        Action<int, int, int> body = Body;
        foreach (var (count, chunk, work) in new[] { (64, 1, 0), (4096, 256, 0), (4096, 256, 20), (16384, 256, 5), (512, 16, 50) })
        foreach (double gapUs in new[] { 0.0, 50.0 })
        {
            for (int i = 0; i < calls / 10; i++) Par.For(count, chunk, body); // warm-up
            _work = work;
            long alloc0 = GC.GetTotalAllocatedBytes(precise: true);
            double total = 0, max = 0;
            for (int i = 0; i < calls; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                Par.For(count, chunk, body);
                double us = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
                total += us;
                if (us > max) max = us;
                if (gapUs > 0) { long g = Stopwatch.GetTimestamp(); while (Stopwatch.GetElapsedTime(g).TotalMicroseconds < gapUs) { } }
            }
            long alloc = GC.GetTotalAllocatedBytes(precise: true) - alloc0;
            Console.WriteLine($"  count={count,6} chunk={chunk,4} work/item={work,3} gap={gapUs,3}us: avg {total / calls,7:F2} us  max {max,8:F1} us  alloc {alloc / (double)calls,7:F1} B/call");
        }
    }

    private static int _work;

    private static void Body(int c, int start, int end)
    {
        long s = 0;
        for (int i = start; i < end; i++)
            for (int k = 0; k < _work; k++) s += i * k;
        _sink[c & (_sink.Length - 1)] = s;
    }
}

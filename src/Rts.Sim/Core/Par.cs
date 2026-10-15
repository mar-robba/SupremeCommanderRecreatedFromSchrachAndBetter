namespace Rts.Sim.Core;

/// <summary>
/// Deterministic data-parallel helpers for simulation systems.
/// Work is split into fixed-size chunks whose boundaries depend only on (count, chunkSize), never on the
/// number of threads. Systems that produce side effects write them into a per-chunk buffer
/// (<see cref="ChunkedBuffer{T}"/>) which is then merged in chunk order: results are identical with 1 or 16 threads.
/// <para>
/// Runs on <see cref="ParPool"/> (persistent worker threads, the caller participates, no allocations per call).
/// A call made while the pool is busy (a nested call from inside a body, or a call from a second thread, e.g. a
/// loading thread while the sim thread steps) runs inline on the calling thread: same chunks, same results.
/// Exceptions thrown by bodies on the pool are rethrown to the caller as an <see cref="AggregateException"/>
/// (like <c>Parallel.For</c>); inline runs throw them directly.
/// </para>
/// Call sites should pass a cached delegate (<c>[NoSave] Action&lt;int, int, int&gt;? _f; ... _f ??= Method</c>):
/// a capturing lambda allocates a closure and a delegate per call.
/// </summary>
public static class Par
{
    [Persistence.NoSave] private static int _maxThreads = Math.Max(1, Environment.ProcessorCount - 1);
    [Persistence.NoSave] private static volatile ParPool? _pool;
    [Persistence.NoSave] private static readonly Lock PoolLock = new();

    /// <summary>Max threads used by the simulation (the caller included). 1 = fully serial (useful for debugging/tests).</summary>
    public static int MaxThreads
    {
        get => _maxThreads;
        set
        {
            int v = Math.Max(1, value);
            lock (PoolLock)
            {
                if (v == _maxThreads) return;
                _maxThreads = v;
                var old = _pool;
                _pool = null; // a new pool is created lazily with the new size; the old one finishes its job and exits
                old?.Shutdown();
            }
        }
    }

    /// <summary>How long idle pool workers spin waiting for the next call before they sleep (µs). Calls within one tick
    /// follow each other closely, so a short spin saves the wake-up latency; between ticks the workers sleep.</summary>
    public static int WorkerSpinMicroseconds
    {
        get => ParPool.SpinMicroseconds;
        set => ParPool.SpinMicroseconds = Math.Max(0, value);
    }

    /// <summary>Benchmark A/B switch: true = run on <c>Parallel.For</c> (the old implementation) instead of the pool.</summary>
    [Persistence.NoSave] public static bool UseTaskParallel;

    public static int ChunkCount(int count, int chunkSize) => count <= 0 ? 0 : (count + chunkSize - 1) / chunkSize;

    /// <summary>Runs body(chunkIndex, start, endExclusive) for every chunk of [0, count).</summary>
    public static void For(int count, int chunkSize, Action<int, int, int> body)
    {
        int chunks = ChunkCount(count, chunkSize);
        if (chunks == 0) return;
        if (chunks > 1 && _maxThreads > 1)
        {
            if (UseTaskParallel)
            {
                Parallel.For(0, chunks, new ParallelOptions { MaxDegreeOfParallelism = _maxThreads }, c => body(c, c * chunkSize, Math.Min(count, (c + 1) * chunkSize)));
                return;
            }
            if (Pool().TryRun(body, null, count, chunkSize, chunks)) return;
        }
        for (int c = 0; c < chunks; c++) body(c, c * chunkSize, Math.Min(count, (c + 1) * chunkSize));
    }

    /// <summary>Runs body(i) for i in [0, count) in parallel; use only when each i writes only its own data.</summary>
    public static void ForEach(int count, Action<int> body)
    {
        if (count <= 0) return;
        if (count > 1 && _maxThreads > 1)
        {
            if (UseTaskParallel)
            {
                Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = _maxThreads }, body);
                return;
            }
            if (Pool().TryRun(null, body, count, 1, count)) return;
        }
        for (int i = 0; i < count; i++) body(i);
    }

    private static ParPool Pool()
    {
        var p = _pool;
        if (p != null) return p;
        lock (PoolLock) return _pool ??= new ParPool(_maxThreads - 1);
    }
}

/// <summary>One list per chunk; parallel chunks append without locks, then <see cref="DrainTo"/> merges in chunk order.</summary>
public sealed class ChunkedBuffer<T>
{
    private List<T>[] _chunks = [];

    public int ChunkCount { get; private set; }

    public void Reset(int chunkCount)
    {
        if (_chunks.Length < chunkCount)
        {
            int old = _chunks.Length;
            Array.Resize(ref _chunks, chunkCount);
            for (int i = old; i < chunkCount; i++) _chunks[i] = new List<T>();
        }
        for (int i = 0; i < _chunks.Length; i++) _chunks[i].Clear();
        ChunkCount = chunkCount;
    }

    public List<T> this[int chunk] => _chunks[chunk];

    public void DrainTo(List<T> output)
    {
        for (int c = 0; c < ChunkCount; c++)
        {
            output.AddRange(_chunks[c]);
            _chunks[c].Clear();
        }
    }
}

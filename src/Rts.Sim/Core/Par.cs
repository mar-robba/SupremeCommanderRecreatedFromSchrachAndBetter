namespace Rts.Sim.Core;

/// <summary>
/// Deterministic data-parallel helpers for simulation systems.
/// Work is split into fixed-size chunks whose boundaries depend only on (count, chunkSize), never on the
/// number of threads. Systems that produce side effects write them into a per-chunk buffer
/// (<see cref="ChunkedBuffer{T}"/>) which is then merged in chunk order: results are identical with 1 or 16 threads.
/// </summary>
public static class Par
{
    private static ParallelOptions _options = new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };

    /// <summary>Max worker threads used by the simulation. 1 = fully serial (useful for debugging/tests).</summary>
    public static int MaxThreads
    {
        get => _options.MaxDegreeOfParallelism;
        set => _options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, value) };
    }

    public static int ChunkCount(int count, int chunkSize) => count <= 0 ? 0 : (count + chunkSize - 1) / chunkSize;

    /// <summary>Runs body(chunkIndex, start, endExclusive) for every chunk of [0, count).</summary>
    public static void For(int count, int chunkSize, Action<int, int, int> body)
    {
        int chunks = ChunkCount(count, chunkSize);
        if (chunks == 0) return;
        if (chunks == 1 || _options.MaxDegreeOfParallelism <= 1)
        {
            for (int c = 0; c < chunks; c++) body(c, c * chunkSize, Math.Min(count, (c + 1) * chunkSize));
            return;
        }
        Parallel.For(0, chunks, _options, c => body(c, c * chunkSize, Math.Min(count, (c + 1) * chunkSize)));
    }

    /// <summary>Runs body(i) for i in [0, count) in parallel; use only when each i writes only its own data.</summary>
    public static void ForEach(int count, Action<int> body)
    {
        if (count <= 0) return;
        if (count == 1 || _options.MaxDegreeOfParallelism <= 1)
        {
            for (int i = 0; i < count; i++) body(i);
            return;
        }
        Parallel.For(0, count, _options, body);
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

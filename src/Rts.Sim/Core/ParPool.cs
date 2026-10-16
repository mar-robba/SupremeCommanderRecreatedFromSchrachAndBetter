using System.Diagnostics;

namespace Rts.Sim.Core;

/// <summary>
/// The worker pool behind <see cref="Par"/>: persistent background threads, one job at a time, the caller participates.
/// Zero allocations per job (no tasks, no closures).
/// <list type="bullet">
/// <item>A job = a body and (count, chunkSize, chunks). Chunk indices are handed out with <c>Interlocked.Increment</c>;
/// chunk boundaries depend only on (count, chunkSize), so which thread runs which chunk never changes results.</item>
/// <item>Idle workers spin for <see cref="SpinMicroseconds"/> (the systems of one tick follow each other closely), then
/// sleep on their own semaphore; the caller wakes only as many sleepers as the job has extra chunks.</item>
/// <item>Join protocol (Dekker-style, full fences on both sides): a worker increments <c>_inFlight</c> and then checks
/// <c>_open</c>; the caller closes the job and then waits for <c>_inFlight == 0</c>. So when the caller returns, no
/// worker is still inside the job and the job fields may be reused for the next one.</item>
/// <item><see cref="TryRun"/> returns false when another job is running (a nested call from a body, or a call from a
/// second thread): <see cref="Par"/> then runs that call inline.</item>
/// </list>
/// </summary>
internal sealed class ParPool
{
    /// <summary>How long an idle worker spins for the next job before it sleeps (µs; <see cref="Par.WorkerSpinMicroseconds"/>).</summary>
    [Persistence.NoSave] public static int SpinMicroseconds = 100;

    private readonly Worker[] _workers;
    private volatile bool _shutdown;
    private int _busy; // 1 while a caller owns the pool

    // The current job: written by the owner while _open == 0, read by workers after they saw _open == 1.
    private ParBody _body;
    private int _count, _chunkSize, _chunks;
    private int _next;
    private int _open;
    private int _jobId;
    private int _inFlight;
    private List<Exception>? _errors;
    private readonly Lock _errorLock = new();

    private sealed class Worker
    {
        public readonly SemaphoreSlim Wake = new(0);
        /// <summary>1 = asleep (or about to sleep) on <see cref="Wake"/>; whoever CASes it back to 0 owns the wake-up.</summary>
        public int Sleeping;
        public Thread Thread = null!;
    }

    public ParPool(int workers)
    {
        _workers = new Worker[Math.Max(0, workers)];
        for (int i = 0; i < _workers.Length; i++)
        {
            var wk = _workers[i] = new Worker();
            wk.Thread = new Thread(() => Loop(wk)) { IsBackground = true, Name = $"Par worker {i}" };
            wk.Thread.Start();
        }
    }

    public int Workers => _workers.Length;

    /// <summary>Runs every chunk (on the workers and the calling thread); false (nothing run) if the pool is busy.</summary>
    public bool TryRun(in ParBody body, int count, int chunkSize, int chunks)
    {
        if (_shutdown || _workers.Length == 0 || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;
        _body = body;
        _count = count;
        _chunkSize = chunkSize;
        _chunks = chunks;
        _next = 0;
        _errors = null;
        Volatile.Write(ref _open, 1);
        Interlocked.Increment(ref _jobId); // full fence: publishes the job to spinning workers

        // Wake sleepers until (chunks - 1) workers are awake (spinning workers join by themselves).
        int needed = Math.Min(chunks - 1, _workers.Length);
        for (int i = 0; i < _workers.Length && needed > 0; i++)
        {
            var wk = _workers[i];
            if (Volatile.Read(ref wk.Sleeping) == 0) { needed--; continue; }
            if (Interlocked.CompareExchange(ref wk.Sleeping, 0, 1) == 1) { wk.Wake.Release(); needed--; }
        }

        RunChunks();

        Interlocked.Exchange(ref _open, 0); // full fence before reading _inFlight
        var spin = new SpinWait();
        while (Volatile.Read(ref _inFlight) != 0) spin.SpinOnce(sleep1Threshold: -1);
        var errors = _errors;
        _body = default;
        Volatile.Write(ref _busy, 0);
        if (errors != null) throw new AggregateException(errors);
        return true;
    }

    private void RunChunks()
    {
        var body = _body;
        int count = _count, size = _chunkSize, chunks = _chunks;
        int c;
        while ((c = Interlocked.Increment(ref _next) - 1) < chunks)
        {
            try
            {
                body.Invoke(c, c * size, Math.Min(count, (c + 1) * size));
            }
            catch (Exception e)
            {
                lock (_errorLock) (_errors ??= new List<Exception>()).Add(e);
                Volatile.Write(ref _next, chunks); // no new chunks after an error (like Parallel.For)
            }
        }
    }

    private void Loop(Worker wk)
    {
        int seen = Volatile.Read(ref _jobId);
        while (true)
        {
            // Wait for a new job: spin a little, then sleep until a caller (or Shutdown) wakes us.
            if (Volatile.Read(ref _jobId) == seen)
            {
                long t0 = Stopwatch.GetTimestamp();
                long limit = SpinMicroseconds * Stopwatch.Frequency / 1_000_000;
                int k = 0;
                while (Volatile.Read(ref _jobId) == seen && !_shutdown && Stopwatch.GetTimestamp() - t0 < limit)
                    Thread.SpinWait(++k < 10 ? 20 : 50);
                if (Volatile.Read(ref _jobId) == seen && !_shutdown)
                {
                    Interlocked.Exchange(ref wk.Sleeping, 1); // full fence before re-checking the job id
                    if (Volatile.Read(ref _jobId) == seen && !_shutdown) wk.Wake.Wait();
                    else if (Interlocked.CompareExchange(ref wk.Sleeping, 0, 1) != 1) wk.Wake.Wait(); // a caller owns our wake-up: consume it
                }
            }
            if (_shutdown) return;
            seen = Volatile.Read(ref _jobId);
            Interlocked.Increment(ref _inFlight); // full fence before reading _open
            if (Volatile.Read(ref _open) == 1) RunChunks();
            Interlocked.Decrement(ref _inFlight);
        }
    }

    /// <summary>Workers exit once idle (a running job still completes; later jobs run on a new pool).</summary>
    public void Shutdown()
    {
        _shutdown = true;
        foreach (var wk in _workers)
            if (Interlocked.CompareExchange(ref wk.Sleeping, 0, 1) == 1) wk.Wake.Release();
    }
}

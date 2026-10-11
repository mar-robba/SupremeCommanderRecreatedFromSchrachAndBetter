using System.Collections.Concurrent;
using System.Diagnostics;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;

namespace Rts.Sim;

/// <summary>
/// Runs a Simulation on a dedicated thread at SimWorld.TicksPerSecond * Speed.
/// Client thread API: <see cref="Enqueue"/> commands, <see cref="TakeLatest"/> / <see cref="Return"/> snapshots.
/// </summary>
public sealed class SimRunner : IDisposable
{
    private readonly Simulation _sim;
    private readonly ConcurrentQueue<Command> _incoming = new();
    private readonly ConcurrentBag<SimSnapshot> _pool = new();
    private readonly List<Command> _tickCommands = new();
    private SimSnapshot? _latest;
    private Thread? _thread;
    private volatile bool _running;
    private long _sequence;

    /// <summary>Game speed multiplier (SupCom: -10..+10 mapped to 0.25x..4x).</summary>
    public volatile float Speed = 1f;
    public volatile bool Paused;
    /// <summary>Fog of war: snapshots only contain what this army knows (-1 = everything visible / observer).</summary>
    public volatile int ViewArmy = -1;
    /// <summary>Set if the sim thread crashed; the client should display it.</summary>
    public Exception? Fault { get; private set; }
    public double LastTickMs => _sim.LastTickMs;
    public int Tick => _sim.World.Tick;

    /// <summary>Optional hook run on the sim thread before each tick (e.g. AI players issuing commands).</summary>
    public Action<Simulation>? BeforeTick;

    public SimRunner(Simulation sim)
    {
        _sim = sim;
        var first = new SimSnapshot();
        _sim.World.Units.RebuildAliveList();
        _sim.World.Projectiles.RebuildAliveList();
        _sim.WriteSnapshot(first);
        first.PublishedAt = Stopwatch.GetTimestamp();
        _latest = first;
    }

    public void Enqueue(Command c)
    {
        c.Sequence = Interlocked.Increment(ref _sequence);
        _incoming.Enqueue(c);
    }

    /// <summary>Takes the newest unconsumed snapshot (or null if none since last call). Return it when done.</summary>
    public SimSnapshot? TakeLatest() => Interlocked.Exchange(ref _latest, null);

    public void Return(SimSnapshot s) => _pool.Add(s);

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Loop) { Name = "Simulation", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join();
        _thread = null;
    }

    public void Dispose() => Stop();

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        double next = 0;
        try
        {
            while (_running)
            {
                double now = clock.Elapsed.TotalMilliseconds;
                double interval = 1000.0 / (Rts.Sim.World.SimWorld.TicksPerSecond * Math.Max(0.05f, Speed));
                if (Paused) { next = now + interval; Thread.Sleep(5); continue; }
                if (now < next)
                {
                    double wait = next - now;
                    if (wait > 2) Thread.Sleep((int)(wait - 1));
                    else Thread.SpinWait(200);
                    continue;
                }
                // Avoid spiral of death: never schedule more than a few ticks behind.
                next = Math.Max(next + interval, now - interval * 3);
                RunOneTick();
            }
        }
        catch (Exception e)
        {
            Fault = e;
            _running = false;
        }
    }

    /// <summary>Runs one tick synchronously (also usable without the thread, e.g. tests/headless).</summary>
    public void RunOneTick()
    {
        BeforeTick?.Invoke(_sim);
        _tickCommands.Clear();
        while (_incoming.TryDequeue(out var c))
        {
            c.ExecuteTick = _sim.World.Tick + 1;
            _tickCommands.Add(c);
        }
        _tickCommands.Sort((a, b) => a.Army != b.Army ? a.Army.CompareTo(b.Army) : a.Sequence.CompareTo(b.Sequence));
        _sim.Step(_tickCommands);

        if (!_pool.TryTake(out var snap)) snap = new SimSnapshot();
        _sim.WriteSnapshot(snap, ViewArmy);
        snap.PublishedAt = Stopwatch.GetTimestamp();
        var old = Interlocked.Exchange(ref _latest, snap);
        if (old != null) _pool.Add(old);
    }
}

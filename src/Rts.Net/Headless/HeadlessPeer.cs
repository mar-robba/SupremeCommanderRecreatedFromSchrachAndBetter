using System.Diagnostics;
using System.Numerics;
using Rts.Net.Lockstep;
using Rts.Net.Session;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;

namespace Rts.Net.Headless;

/// <summary>
/// A multiplayer peer without a client (tests, <c>Rts.Bench -- mp</c>): builds the game's simulation, records a replay,
/// runs the lockstep on its own thread as fast as the turns allow and issues random commands for its army (moves,
/// structures for the builders, factory queues). <see cref="Hashes"/> = its StateHash every 10 ticks.
/// </summary>
public sealed class HeadlessPeer : IDisposable
{
    public readonly NetSession Session;
    public readonly NetGame Game;
    public readonly Simulation Sim;
    public readonly SimRunner Runner;
    public readonly ReplayRecorder Recorder;
    public readonly LockstepHooks Hooks;
    /// <summary>(tick, StateHash) every <see cref="LockstepHooks.HashInterval"/> ticks (sim thread writes; read after <see cref="Wait"/>).</summary>
    public readonly List<(int tick, ulong hash)> Hashes = new();
    public long CommandsIssued;
    /// <summary>Wall-clock ms spent waiting for turns.</summary>
    public double HeldMs;
    public Exception? Fault { get; private set; }
    private Thread? _thread;
    private volatile bool _stop;
    private readonly Random _rng;

    /// <summary>Builds the simulation of <paramref name="session"/>'s started game and reports it loaded.</summary>
    public HeadlessPeer(NetSession session, BlueprintDb db, int seed)
    {
        Session = session;
        Game = session.Game ?? throw new InvalidOperationException("no game started");
        Sim = NetGameSetup.CreateSimulation(db, Game.Start, session.MapDir); // (8b-3) data/maps or the session folder
        ulong hash = Sim.StateHash();
        Runner = new SimRunner(Sim);
        Recorder = new ReplayRecorder();
        Hooks = Game.CreateHooks(Recorder);
        Hooks.HashComputed = (t, h) => Hashes.Add((t, h));
        Runner.Hooks = Hooks;
        _rng = new Random(seed);
        Game.ReportLoaded(hash);
    }

    /// <summary>Runs until the sim reaches <paramref name="ticks"/> (on a new thread; <see cref="Wait"/> for it).</summary>
    public void Run(int ticks, double commandsPerTick, Action<HeadlessPeer, int>? perTick = null)
    {
        _thread = new Thread(() =>
        {
            var held = new Stopwatch();
            try
            {
                while (!_stop && Sim.World.Tick < ticks)
                {
                    if (Hooks.Hold(Sim) || Game.Paused)
                    {
                        held.Start();
                        Thread.Sleep(1);
                        held.Stop();
                        continue;
                    }
                    if (Game.LocalArmy >= 0 && _rng.NextDouble() < commandsPerTick) IssueRandom();
                    Runner.RunOneTick();
                    perTick?.Invoke(this, Sim.World.Tick);
                }
            }
            catch (Exception e) { Fault = e; }
            HeldMs = held.Elapsed.TotalMilliseconds;
        }) { Name = "Peer " + Session.LocalName, IsBackground = true };
        _thread.Start();
    }

    public bool Wait(int timeoutMs) => _thread == null || _thread.Join(timeoutMs);

    /// <summary>Random command for a random own unit (sim thread = this peer's thread).</summary>
    private void IssueRandom()
    {
        var w = Sim.World;
        var u = w.Units;
        int army = Game.LocalArmy;
        var mine = new List<int>();
        foreach (int i in u.AliveList)
            if (u.Army[i] == army) mine.Add(i);
        if (mine.Count == 0) return;
        int k = mine[_rng.Next(mine.Count)];
        var h = u.HandleOf(k);
        var bp = w.Blueprints[u.Bp[k]];
        var pos = u.Pos[k];
        var near = pos + new Vector2(_rng.Next(-30, 31), _rng.Next(-30, 31));
        near = Vector2.Clamp(near, new Vector2(4), new Vector2(w.Terrain.Width - 4, w.Terrain.Height - 4));
        Command c;
        if (bp.IsStructure && bp.BuildIndices.Length > 0)
            c = new FactoryEnqueueCommand(army, [h], bp.BuildIndices[_rng.Next(bp.BuildIndices.Length)], 1 + _rng.Next(3));
        else if (bp.BuildIndices.Length > 0 && _rng.Next(3) > 0)
        {
            var site = new Vector2(MathF.Round(near.X) + 0.5f, MathF.Round(near.Y) + 0.5f);
            c = new IssueOrderCommand(army, [h], Order.BuildAt(bp.BuildIndices[_rng.Next(bp.BuildIndices.Length)], site), _rng.Next(2) == 0);
        }
        else if (bp.IsMobile)
            c = new IssueOrderCommand(army, [h], _rng.Next(2) == 0 ? Order.MoveTo(near) : Order.AttackMoveTo(near), _rng.Next(3) == 0);
        else return;
        Runner.Enqueue(c);
        CommandsIssued++;
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join(5000);
    }

    /// <summary>The replay recorded by this peer so far (call after <see cref="Wait"/>).</summary>
    public ReplayLog Replay() => Recorder.Snapshot();
}

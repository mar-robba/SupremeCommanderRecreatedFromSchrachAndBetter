using Rts.Sim;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;

namespace Rts.Net.Lockstep;

/// <summary>Where the local player's turns and hashes go (the host broadcasts them, a client sends them to the host).</summary>
public interface ITurnSink
{
    /// <summary>The local player's turn for <paramref name="tick"/> (sim thread; must not block).</summary>
    void SendTurn(int turnPlayer, int tick, byte[] payload);
    /// <summary>The local StateHash after <paramref name="tick"/> (sim thread; must not block).</summary>
    void SendHash(int tick, ulong hash);
    /// <summary>(10a-2) The local player stopped playing after <paramref name="lastTick"/> (sim thread, after its turns up to it were sent).</summary>
    void SendResign(int lastTick) { }
}

/// <summary>
/// Deterministic lockstep as the runner's <see cref="ITickHooks"/> (wraps the replay recorder, so every MP game is a
/// normal replay).
/// <list type="bullet">
/// <item>Every turn player sends exactly one turn per tick, for consecutive ticks: when stepping to tick T+1 the
///   local commands of that frame go into the turn for T+1+<see cref="Delay"/> (the gap after a delay increase is
///   filled with empty turns; after a decrease the player sends nothing until the ticks catch up). The delay is a
///   sender-side choice (the host broadcasts the adaptive value): turns carry their tick, so peers never disagree.</item>
/// <item><see cref="Hold"/>: the sim steps to T+1 only when every non-dropped player's turn for T+1 is in.</item>
/// <item>The commands of a tick = the turns in turn-player order (= army order); commands for another army than the
///   sender's are dropped (never trust peers); Sequence/ExecuteTick are renumbered so every peer hands the sim the
///   identical list.</item>
/// <item>A dropped player (<see cref="Drop"/>): its turns after its last tick count as empty and a
///   <see cref="PlayerLeftCommand"/> is added at the tick after it, on every peer (the host decides the last tick and
///   relays the turns up to it before the drop message, so no peer has simulated past it).</item>
/// <item>Every <see cref="HashInterval"/> ticks the StateHash goes to the sink (desync detection by the host).</item>
/// </list>
/// Threads: Hold/BeforeStep/AfterStep on the sim thread; AddTurn/Drop/Start on network threads (locked).
/// </summary>
public sealed class LockstepHooks : ITickHooks
{
    public const int HashInterval = ReplayLog.HashInterval;
    public const int MaxDelay = 30;

    private readonly object _lock = new();
    private readonly ITickHooks? _inner;
    private readonly ITurnSink _sink;
    private readonly int[] _turnArmies;
    private readonly Dictionary<int, Command[]>[] _turns;
    /// <summary>Highest tick whose turn is in, per turn player (turns arrive in order).</summary>
    private readonly int[] _received;
    /// <summary>Last tick with real turns of a dropped player (int.MaxValue = still playing).</summary>
    private readonly int[] _lastTick;
    private readonly DropAction[] _dropAction;
    private readonly List<Command> _tickCommands = new();
    private readonly TurnEncoder _encoder = new();
    /// <summary>One decoder per sender (stateful type tables): used by that sender's network thread (own turns: under the lock).</summary>
    private readonly TurnDecoder[] _decoders;
    private readonly List<Command> _pending = new();
    /// <summary>Local turns to send once the lock is released (the sink may take the session's lock: never call it under ours).</summary>
    private readonly List<(int tick, byte[] payload)> _outbox = new();
    private int _nextSend;
    private volatile bool _go;
    private volatile int _delay;
    private volatile int _waitingFor = -1;
    private volatile bool _resignRequested;

    /// <summary>Local turn player index (-1 = observer: sends nothing).</summary>
    public readonly int LocalTurn;
    public int Players => _turnArmies.Length;
    /// <summary>Input delay in ticks used for the local player's next turns (set from the host's Delay messages).</summary>
    public int Delay { get => _delay; set => _delay = Math.Clamp(value, 1, MaxDelay); }
    public bool Started => _go;
    /// <summary>Turn player the sim is waiting for (-1 = none). Display only.</summary>
    public int WaitingFor => _waitingFor;
    /// <summary>Total commands sent by the local player / ticks stepped (stats).</summary>
    public long CommandsSent, TicksStepped;
    /// <summary>(10a-2) Last tick of the local player's turns after <see cref="RequestResign"/> took effect (-1 = still playing).</summary>
    public int ResignedAt { get; private set; } = -1;
    /// <summary>Optional hook: called on the sim thread after each hash tick with (tick, hash).</summary>
    public Action<int, ulong>? HashComputed;

    /// <param name="turnArmies">Army of each turn player (ascending).</param>
    /// <param name="startTick">The simulation's tick before the first step (0 for a new game).</param>
    public LockstepHooks(int[] turnArmies, int localTurn, int delay, ITurnSink sink, ITickHooks? inner, int startTick = 0)
    {
        _turnArmies = turnArmies;
        LocalTurn = localTurn;
        Delay = delay;
        _sink = sink;
        _inner = inner;
        int n = turnArmies.Length;
        _turns = new Dictionary<int, Command[]>[n];
        _received = new int[n];
        _lastTick = new int[n];
        _dropAction = new DropAction[n];
        _decoders = new TurnDecoder[n];
        for (int p = 0; p < n; p++)
        {
            _turns[p] = new Dictionary<int, Command[]>();
            _decoders[p] = new TurnDecoder();
            _received[p] = startTick;
            _lastTick[p] = int.MaxValue;
        }
        _nextSend = startTick + 1;
    }

    /// <summary>Everybody loaded: pre-sends the local empty turns for the first <see cref="Delay"/> ticks and lets the sim run.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_go) return;
            if (LocalTurn >= 0)
            {
                int target = _nextSend - 1 + Delay;
                while (_nextSend <= target) SendLocal(_nextSend++, TurnCodec.Empty);
            }
        }
        FlushOutbox();
        _go = true;
    }

    /// <summary>A turn of another player arrived (network thread). Out-of-order or duplicate turns are a protocol error.</summary>
    public void AddTurn(int turnPlayer, int tick, byte[] payload)
    {
        if ((uint)turnPlayer >= (uint)Players) throw new Wire.ProtocolException($"bad turn player {turnPlayer}");
        Command[] cmds;
        lock (_decoders[turnPlayer]) cmds = _decoders[turnPlayer].Decode(payload);
        lock (_lock)
        {
            if (_lastTick[turnPlayer] != int.MaxValue) return; // dropped: late turns are ignored
            if (tick != _received[turnPlayer] + 1)
                throw new Wire.ProtocolException($"turn {tick} of player {turnPlayer} out of order (expected {_received[turnPlayer] + 1})");
            _turns[turnPlayer][tick] = cmds;
            _received[turnPlayer] = tick;
        }
    }

    /// <summary>Highest tick whose turn of <paramref name="turnPlayer"/> is in.</summary>
    public int Received(int turnPlayer)
    {
        lock (_lock) return _received[turnPlayer];
    }

    public bool IsDropped(int turnPlayer)
    {
        lock (_lock) return _lastTick[turnPlayer] != int.MaxValue;
    }

    /// <summary>
    /// Drops a player: its turns after <paramref name="lastTick"/> are empty, and its army gets a
    /// <see cref="PlayerLeftCommand"/> at lastTick + 1. Must be called before the sim reaches lastTick + 1 (guaranteed
    /// when lastTick = the last turn received from that player, as the host does). Returns false if already dropped.
    /// </summary>
    public bool Drop(int turnPlayer, int lastTick, DropAction action)
    {
        lock (_lock)
        {
            if ((uint)turnPlayer >= (uint)Players || _lastTick[turnPlayer] != int.MaxValue) return false;
            if (lastTick > _received[turnPlayer]) lastTick = _received[turnPlayer];
            _lastTick[turnPlayer] = lastTick;
            _dropAction[turnPlayer] = action;
            foreach (var t in _turns[turnPlayer].Keys.Where(t => t > lastTick).ToList()) _turns[turnPlayer].Remove(t);
            return true;
        }
    }

    /// <summary>(10a-2) The local player (defeated) stays as an observer: at the next step it stops sending turns after
    /// the last one already produced, drops itself locally (<see cref="DropAction.Defeat"/>) and tells the sink.</summary>
    public void RequestResign()
    {
        if (LocalTurn >= 0) _resignRequested = true;
    }

    /// <summary>Under the lock: the local player's turns end at the last one produced.</summary>
    private int ResignLocal()
    {
        _resignRequested = false;
        if (_lastTick[LocalTurn] != int.MaxValue) return -1;
        int last = _nextSend - 1;
        _lastTick[LocalTurn] = last;
        _dropAction[LocalTurn] = DropAction.Defeat;
        _pending.Clear();
        ResignedAt = last;
        return last;
    }

    // ------------------------------------------------------------------ ITickHooks (sim thread)

    public bool Hold(Simulation sim)
    {
        if (!_go) return true;
        int t = sim.World.Tick + 1;
        lock (_lock)
        {
            for (int p = 0; p < Players; p++)
            {
                if (t > _lastTick[p]) continue;
                if (_received[p] < t)
                {
                    _waitingFor = p;
                    return true;
                }
            }
        }
        _waitingFor = -1;
        return _inner?.Hold(sim) == true;
    }

    public IReadOnlyList<Command> BeforeStep(Simulation sim, List<Command> incoming)
    {
        int next = sim.World.Tick + 1;
        _tickCommands.Clear();
        int resigned = -1;
        lock (_lock)
        {
            if (_resignRequested) resigned = ResignLocal();
            if (LocalTurn >= 0 && _lastTick[LocalTurn] == int.MaxValue) SendLocalCommands(next, incoming);
            for (int p = 0; p < Players; p++)
            {
                if (next > _lastTick[p])
                {
                    if (next == _lastTick[p] + 1) _tickCommands.Add(new PlayerLeftCommand(_turnArmies[p], _dropAction[p]));
                    continue;
                }
                if (!_turns[p].Remove(next, out var cmds))
                    throw new InvalidOperationException($"lockstep: turn {next} of player {p} missing (Hold was not checked)");
                foreach (var c in cmds)
                    if (c.Army == _turnArmies[p] && c is not PlayerLeftCommand) _tickCommands.Add(c);
            }
        }
        FlushOutbox();
        if (resigned >= 0) _sink.SendResign(resigned);
        for (int i = 0; i < _tickCommands.Count; i++)
        {
            _tickCommands[i].Sequence = i + 1;
            _tickCommands[i].ExecuteTick = next;
        }
        TicksStepped++;
        return _inner != null ? _inner.BeforeStep(sim, _tickCommands) : _tickCommands;
    }

    public void AfterStep(Simulation sim)
    {
        _inner?.AfterStep(sim);
        int t = sim.World.Tick;
        if (t % HashInterval != 0) return;
        ulong h = sim.StateHash();
        _sink.SendHash(t, h);
        HashComputed?.Invoke(t, h);
    }

    /// <summary>Called under the lock when stepping to <paramref name="next"/>: the local commands go into the turn for next + Delay.</summary>
    private void SendLocalCommands(int next, List<Command> incoming)
    {
        int army = _turnArmies[LocalTurn];
        foreach (var c in incoming)
            if (c.Army == army) _pending.Add(c);
        int target = next + Delay;
        if (_nextSend > target) return; // delay decreased: wait until the ticks catch up (commands stay pending)
        // Local commands go out with the first turn; gap ticks (delay increased) get empty turns.
        byte[] first = _encoder.Encode(_pending);
        CommandsSent += _pending.Count;
        _pending.Clear();
        SendLocal(_nextSend++, first);
        while (_nextSend <= target) SendLocal(_nextSend++, TurnCodec.Empty);
    }

    private void SendLocal(int tick, byte[] payload)
    {
        // The local copy is decoded from the same bytes the peers get: every peer steps identical command objects.
        _turns[LocalTurn][tick] = _decoders[LocalTurn].Decode(payload);
        _received[LocalTurn] = tick;
        _outbox.Add((tick, payload));
    }

    /// <summary>Sends the queued local turns (outside the lock; only one thread at a time: Start before the sim runs, then the sim thread).</summary>
    private void FlushOutbox()
    {
        foreach (var (tick, payload) in _outbox) _sink.SendTurn(LocalTurn, tick, payload);
        _outbox.Clear();
    }
}

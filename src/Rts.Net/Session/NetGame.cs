using Rts.Net.Lobby;
using Rts.Net.Lockstep;
using Rts.Sim;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;

namespace Rts.Net.Session;

/// <summary>
/// One multiplayer game of a session (created when the host starts). Flow on every peer:
/// build the simulation (<see cref="NetGameSetup.CreateSimulation"/>) → <see cref="CreateHooks"/> (the runner's hooks,
/// wrapping the replay recorder) → <see cref="ReportLoaded"/> with the fresh state's hash → the host sends Go when every
/// peer reported the same hash → the lockstep runs. Speed/pause: <see cref="RequestSpeed"/> (the host decides and
/// broadcasts; <see cref="AttachRunner"/> applies it to the local runner).
/// </summary>
public sealed class NetGame
{
    private readonly NetSession _session;
    private readonly object _lock = new();
    private readonly List<(int turn, int last, DropAction action)> _pendingDrops = new();
    private bool _goReceived;
    private SimRunner? _runner;
    private volatile int[] _pings;

    public readonly GameStart Start;
    /// <summary>Local turn player index and army (-1 = not playing).</summary>
    public readonly int LocalTurn, LocalArmy;
    public LockstepHooks? Hooks { get; private set; }
    public volatile float Speed = 1f;
    public volatile bool Paused;
    /// <summary>First desynced tick (-1 = in sync) and the host's description.</summary>
    public volatile int DesyncTick = -1;
    public string? DesyncDetail { get; private set; }
    /// <summary>The connection to the host was lost: this peer continues alone (the others' armies were dropped).</summary>
    public volatile bool Offline;
    public bool Running => Hooks?.Started == true;

    internal NetGame(NetSession session, GameStart start, int localTurn)
    {
        _session = session;
        Start = start;
        LocalTurn = localTurn;
        LocalArmy = localTurn >= 0 ? start.TurnArmies[localTurn] : -1;
        _pings = new int[start.TurnPlayers.Length];
    }

    public int TurnPlayers => Start.TurnPlayers.Length;
    public string NameOfTurn(int turn) => (uint)turn < (uint)TurnPlayers ? Start.Lobby.Slots[Start.ArmySlots[Start.TurnArmies[turn]]].Name : "?";
    public int TurnOfArmy(int army) => Array.IndexOf(Start.TurnArmies, army);

    /// <summary>Creates the lockstep hooks around <paramref name="inner"/> (the replay recorder). Call once, before the runner starts.</summary>
    public LockstepHooks CreateHooks(ITickHooks? inner, int startTick = 0)
    {
        lock (_lock)
        {
            if (Hooks != null) throw new InvalidOperationException("hooks already created");
            var h = new LockstepHooks(Start.TurnArmies, LocalTurn, Start.Delay, _session.Sink, inner, startTick);
            foreach (var (t, last, action) in _pendingDrops) h.Drop(t, last, action);
            _pendingDrops.Clear();
            Hooks = h;
            if (_goReceived) h.Start();
            return h;
        }
    }

    /// <summary>Tells the host this peer built its simulation; <paramref name="stateHash"/> = <c>sim.StateHash()</c> of the fresh state.</summary>
    public void ReportLoaded(ulong stateHash) => _session.ReportLoaded(this, stateHash);

    /// <summary>Asks for a new speed / pause state (the host applies it for everybody).</summary>
    public void RequestSpeed(float speed, bool paused) => _session.RequestSpeed(this, Math.Clamp(speed, 0.125f, 8f), paused);

    /// <summary>The local runner whose Speed/Paused follow the negotiated values (set now and at every change).</summary>
    public void AttachRunner(SimRunner runner)
    {
        _runner = runner;
        runner.Speed = Speed;
        runner.Paused = Paused;
    }

    /// <summary>Last ping of each turn player in ms (the host's view; -1 unknown).</summary>
    public int[] Pings => _pings;

    /// <summary>Name of the player the local sim waits for (null = not waiting / not started).</summary>
    public string? WaitingFor
    {
        get
        {
            var h = Hooks;
            if (h == null || !h.Started) return null;
            int w = h.WaitingFor;
            return w < 0 ? null : NameOfTurn(w);
        }
    }

    public List<NetPlayerStatus> PlayerStatus()
    {
        var list = new List<NetPlayerStatus>();
        var pings = _pings;
        for (int t = 0; t < TurnPlayers; t++)
            list.Add(new NetPlayerStatus(t, NameOfTurn(t), Start.TurnArmies[t], t < pings.Length ? pings[t] : -1,
                Hooks?.IsDropped(t) == true || _pendingDrops.Any(d => d.turn == t), t == LocalTurn));
        return list;
    }

    // ------------------------------------------------------------------ called by the session (network threads)

    internal void OnGo()
    {
        lock (_lock)
        {
            _goReceived = true;
            Hooks?.Start();
        }
    }

    internal void ApplyDrop(int turn, int lastTick, DropAction action)
    {
        lock (_lock)
        {
            if (Hooks != null) Hooks.Drop(turn, lastTick, action);
            else if (!_pendingDrops.Any(d => d.turn == turn)) _pendingDrops.Add((turn, lastTick, action));
        }
    }

    /// <summary>Highest tick received from a turn player (0 before the hooks exist).</summary>
    internal int ReceivedOf(int turn)
    {
        lock (_lock) return Hooks?.Received(turn) ?? 0;
    }

    internal bool IsDropped(int turn)
    {
        lock (_lock) return Hooks?.IsDropped(turn) ?? _pendingDrops.Any(d => d.turn == turn);
    }

    internal void AddTurn(int turn, int tick, byte[] payload)
    {
        var h = Hooks ?? throw new Wire.ProtocolException("turn before the game was loaded");
        h.AddTurn(turn, tick, payload);
    }

    internal void SetSpeed(float speed, bool paused)
    {
        Speed = speed;
        Paused = paused;
        if (_runner is { } r)
        {
            r.Speed = speed;
            r.Paused = paused;
        }
    }

    internal void SetDelay(int delay)
    {
        if (Hooks is { } h) h.Delay = delay;
    }

    internal void SetPings(int[] pings) => _pings = pings;

    internal bool MarkDesync(int tick, string detail)
    {
        if (DesyncTick >= 0) return false;
        DesyncDetail = detail;
        DesyncTick = tick;
        return true;
    }
}

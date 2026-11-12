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
public sealed partial class NetGame
{
    private readonly NetSession _session;
    private readonly object _lock = new();
    private readonly List<(int turn, int last, DropAction action)> _pendingDrops = new();
    private bool _goReceived;
    private SimRunner? _runner;
    private volatile int[] _pings;
    /// <summary>(10a-2) Turns that reached an observer before its simulation was built.</summary>
    private readonly List<(int turn, int tick, byte[] payload)> _earlyTurns = new();
    private volatile DropVoteTally? _votes;

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
        // A locked game speed (lobby option) is the starting speed on every peer (same lobby = no broadcast needed).
        Speed = LockedSpeed ?? 1f;
    }

    /// <summary>The speed fixed by the lobby's game options (null = adjustable): speed requests keep it, pause still works.</summary>
    public float? LockedSpeed => Start.Lobby.Options.Game.LockedSpeed;

    /// <summary>The speed a request may set: the requested one, or the locked speed.</summary>
    internal float AllowedSpeed(float requested) => LockedSpeed ?? requested;

    public int TurnPlayers => Start.TurnPlayers.Length;
    public string NameOfTurn(int turn) => (uint)turn < (uint)TurnPlayers ? Start.Lobby.Slots[Start.ArmySlots[Start.TurnArmies[turn]]].Name : "?";
    public int TurnOfArmy(int army) => Array.IndexOf(Start.TurnArmies, army);

    /// <summary>Creates the lockstep hooks around <paramref name="inner"/> (the replay recorder). Call once, before the runner starts.</summary>
    public LockstepHooks CreateHooks(ITickHooks? inner, int startTick = 0)
    {
        lock (_lock)
        {
            if (Hooks != null) throw new InvalidOperationException("hooks already created");
            if (startTick == 0) startTick = Start.StartTick; // (10a-3) a loaded game resumes at the saved tick
            var h = new LockstepHooks(Start.TurnArmies, LocalTurn, Start.Delay, _session.Sink, inner, startTick);
            SetupSaves(h, startTick); // (10a-3) NetGame.Saves.cs
            foreach (var (t, last, action) in _pendingDrops) h.Drop(t, last, action);
            _pendingDrops.Clear();
            foreach (var (t, tick, payload) in _earlyTurns) h.AddTurn(t, tick, payload); // (10a-2) observer
            _earlyTurns.Clear();
            Hooks = h;
            if (_goReceived) h.Start();
            return h;
        }
    }

    /// <summary>Tells the host this peer built its simulation; <paramref name="stateHash"/> = <c>sim.StateHash()</c> of the fresh state.</summary>
    public void ReportLoaded(ulong stateHash) => _session.ReportLoaded(this, stateHash);

    /// <summary>Asks for a new speed / pause state (the host applies it for everybody).</summary>
    public void RequestSpeed(float speed, bool paused) => _session.RequestSpeed(this, AllowedSpeed(Math.Clamp(speed, 0.125f, 8f)), paused);

    /// <summary>The local runner whose Speed/Paused follow the negotiated values (set now and at every change).</summary>
    public void AttachRunner(SimRunner runner)
    {
        _runner = runner;
        runner.Speed = Speed;
        runner.Paused = HoldForPause; // (10a-3) a pending save runs a paused game up to its tick
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
        if (LocalTurn < 0)
            lock (_lock)
                if (Hooks == null) { _earlyTurns.Add((turn, tick, payload)); return; } // (10a-2) observer still loading
        var h = Hooks ?? throw new Wire.ProtocolException("turn before the game was loaded");
        h.AddTurn(turn, tick, payload);
    }

    // ------------------------------------------------------------------ (10a-2) observers, resign, drop votes

    /// <summary>Seconds a peer waits for a player before it may vote to drop it (the banner counts down).</summary>
    public const double DropVoteDelaySeconds = 5;

    /// <summary>This peer only watches: an observer of the lobby (no army, sends no turns).</summary>
    public bool IsObserver => LocalTurn < 0;

    /// <summary>The local (defeated) player stopped sending turns and stays as an observer (see <see cref="Resign"/>).</summary>
    public bool Resigned => Hooks?.ResignedAt >= 0;

    /// <summary>The local player stays as an observer: its turns end, every peer drops its army (defeat), so the game
    /// no longer waits for it. Meant for a defeated player; the others see "left".</summary>
    public void Resign() => Hooks?.RequestResign();

    /// <summary>Turn player the local sim waits for (-1 = none).</summary>
    public int WaitingForTurn => Hooks is { Started: true } h ? h.WaitingFor : -1;

    /// <summary>Turn index of the host (-1 = the host observes).</summary>
    public int HostTurn => Start.TurnIndexOf(0);

    /// <summary>True if the local player may vote to drop <paramref name="turn"/>: a playing peer (not dropped), another
    /// player than itself and not the host (no host migration).</summary>
    public bool CanVoteDrop(int turn) => LocalTurn >= 0 && turn != LocalTurn && turn != HostTurn && (uint)turn < (uint)TurnPlayers
        && !IsDropped(LocalTurn) && !IsDropped(turn);

    /// <summary>Votes (or withdraws the vote) to drop the player everybody waits for; the host drops it when a majority
    /// of the remaining players agree (<see cref="DropVoteTally.Needed"/>).</summary>
    public void VoteDrop(int turn, bool yes = true)
    {
        if (CanVoteDrop(turn)) _session.VoteDrop(this, turn, yes);
    }

    /// <summary>The host's last tally (null = no votes).</summary>
    public DropVoteTally? Votes => _votes;

    internal void SetVotes(DropVoteTally? t) => _votes = t is { Votes: > 0 } ? t : null;

    /// <summary>Votes needed to drop a player: a strict majority of the remaining (not dropped) players other than it.</summary>
    public static int VotesNeeded(int remainingVoters) => remainingVoters / 2 + 1;

    internal void SetSpeed(float speed, bool paused)
    {
        Speed = speed;
        Paused = paused;
        if (_runner is { } r)
        {
            r.Speed = speed;
            r.Paused = HoldForPause;
        }
    }

    internal void SetDelay(int delay)
    {
        if (Hooks is { } h) h.Delay = delay;
    }

    internal void SetPings(int[] pings) => _pings = pings;

    internal bool HasHooks => Hooks != null;

    internal bool MarkDesync(int tick, string detail)
    {
        if (DesyncTick >= 0) return false;
        DesyncDetail = detail;
        DesyncTick = tick;
        return true;
    }
}

/// <summary>(10a-2) Drop vote tally against turn player <paramref name="Target"/>: <paramref name="Voters"/> = turn indices.</summary>
public sealed record DropVoteTally(int Target, int Votes, int Needed, int[] Voters)
{
    public bool HasVoted(int turn) => Array.IndexOf(Voters, turn) >= 0;
}

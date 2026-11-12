using System.Diagnostics;
using Rts.Net.Lobby;
using Rts.Net.Transport;
using Rts.Net.Wire;
using Rts.Sim.Orders;

namespace Rts.Net.Session;

/// <summary>
/// (10a-2) Host side of observers (lobby moves, joins, loading and hash checks), resigning players, drop votes and the
/// ping list. Everything here runs under <c>Gate</c> unless it says otherwise.
/// </summary>
public sealed partial class HostSession
{
    /// <summary>A vote against a player counts only after this long without a turn from it (the clients show the
    /// button after <see cref="NetGame.DropVoteDelaySeconds"/> of waiting).</summary>
    public const double VoteMinStallSeconds = 3;

    /// <summary>Turn player → the turn players voting to drop it (a turn from it clears the votes).</summary>
    private readonly Dictionary<int, SortedSet<int>> _dropVotes = new();
    private long[] _lastTurnAt = [];
    private readonly HashSet<int> _observerDesync = new();

    // ------------------------------------------------------------------ lobby

    /// <summary>Moves a person to the observers (<paramref name="observe"/>) or from them into <paramref name="slot"/>
    /// (-1 = the first open slot). The host may move anybody, itself included.</summary>
    public void SetObserver(int playerId, bool observe, int slot = -1)
    {
        lock (Gate)
            if (Phase == NetPhase.Lobby && MoveObserver(playerId, observe, slot)) Changed();
    }

    public override void Observe(bool on, int slot = -1) => SetObserver(0, on, slot);

    private bool MoveObserver(int playerId, bool observe, int slot)
    {
        if (observe)
        {
            int i = LobbyRef.SlotOf(playerId);
            if (i < 0 || LobbyRef.Observers.Count >= LobbyState.MaxObservers) return false;
            var s = LobbyRef.Slots[i];
            LobbyRef.Observers.Add(NewObserver(s.PlayerId, s.Name, s.MapReady, s.MapProgress));
            s.Kind = NetSlotKind.Open;
            s.PlayerId = -1;
            s.Name = "";
            s.Ready = false;
            s.MapReady = true;
            s.MapProgress = 0;
            return true;
        }
        int k = LobbyRef.ObserverOf(playerId);
        if (k < 0) return false;
        int dst = (uint)slot < (uint)LobbyRef.Slots.Count && LobbyRef.Slots[slot].Kind == NetSlotKind.Open ? slot : LobbyRef.FirstOpenSlot();
        if (dst < 0) return false;
        var o = LobbyRef.Observers[k];
        var d = LobbyRef.Slots[dst];
        d.Kind = NetSlotKind.Human;
        d.PlayerId = o.PlayerId;
        d.Name = o.Name;
        d.Ready = playerId == 0;
        d.MapReady = o.MapReady;
        d.MapProgress = o.MapProgress;
        if (ColorTaken(d.Color, dst)) d.Color = FreeColor(dst);
        LobbyRef.Observers.RemoveAt(k);
        return true;
    }

    private static NetSlot NewObserver(int playerId, string name, bool mapReady, int progress) => new()
    {
        Kind = NetSlotKind.Observer, PlayerId = playerId, Name = name, Ready = true, MapReady = mapReady, MapProgress = progress,
    };

    /// <summary>A connection left the lobby: frees its slot or observer entry. Returns true if anything changed.</summary>
    private bool RemovePerson(int playerId)
    {
        int k = LobbyRef.ObserverOf(playerId);
        if (k >= 0)
        {
            LobbyRef.Observers.RemoveAt(k);
            return true;
        }
        int i = LobbyRef.SlotOf(playerId);
        if (i < 0) return false;
        var s = LobbyRef.Slots[i];
        s.Kind = NetSlotKind.Open;
        s.PlayerId = -1;
        s.Name = "";
        s.Ready = false;
        return true;
    }

    // ------------------------------------------------------------------ loading and hashes of observers

    /// <summary>An observer's Loaded hash: it never holds the game; a different state only warns that observer.</summary>
    private void ObserverLoaded(NetConnection c, ulong hash)
    {
        _loaded[c.PlayerId] = hash;
        if (Phase == NetPhase.Playing && _loaded.TryGetValue(0, out ulong mine) && mine != hash) ObserverDesync(c, 0);
    }

    /// <summary>At Go: warns the observers that built a different starting state.</summary>
    private void CheckObserversLoaded(NetGame g, ulong mine)
    {
        foreach (var c in _conns)
            if (c.PlayerId > 0 && g.Start.TurnIndexOf(c.PlayerId) < 0 && _loaded.TryGetValue(c.PlayerId, out ulong h) && h != mine)
                ObserverDesync(c, 0);
        _lastTurnAt = Enumerable.Repeat(Stopwatch.GetTimestamp(), g.TurnPlayers).ToArray();
    }

    private void CheckObserverHash(NetConnection c, int tick, ulong hash)
    {
        if (_hostHashes.TryGetValue(tick, out ulong mine) && mine != hash) ObserverDesync(c, tick);
    }

    private void ObserverDesync(NetConnection c, int tick)
    {
        if (!_observerDesync.Add(c.PlayerId)) return;
        string detail = $"{c.Name} (observer) differs from the host at tick {tick}";
        c.Send(Msg.Build(MsgType.Desync, w => { w.VarU((ulong)tick); w.Str(detail); }));
    }

    // ------------------------------------------------------------------ resign (a defeated player keeps watching)

    private void OnResign(NetConnection c, int lastTick)
    {
        var g = Game;
        if (g == null || Phase != NetPhase.Playing) return;
        int turn = g.Start.TurnIndexOf(c.PlayerId);
        if (turn < 0 || g.IsDropped(turn)) return;
        // TCP order: every turn up to lastTick arrived before this message, so the peers agree on the last tick.
        Resigned(g, turn, Math.Min(lastTick, g.ReceivedOf(turn)), c.PlayerId);
    }

    void Lockstep.ITurnSink.SendResign(int lastTick)
    {
        lock (Gate)
            if (Game is { } g && g.LocalTurn >= 0 && Phase == NetPhase.Playing) Resigned(g, g.LocalTurn, lastTick, 0);
    }

    private void Resigned(NetGame g, int turn, int last, int playerId)
    {
        const string reason = "observing";
        if (playerId != 0) g.ApplyDrop(turn, last, DropAction.Defeat); // the host's own hooks already dropped it
        var frame = DropFrame(turn, last, DropAction.Defeat, reason);
        foreach (var o in _conns)
            if (o.PlayerId > 0 && o.PlayerId != int.MaxValue && o.PlayerId != playerId) o.Send(frame);
        string name = g.NameOfTurn(turn);
        Raise(new PlayerDroppedEvent(name, g.Start.TurnArmies[turn], DropAction.Defeat, reason));
        SystemChatAll($"{name} is now observing");
        RecountVotes(g);
    }

    // ------------------------------------------------------------------ drop votes

    internal override void VoteDrop(NetGame game, int turn, bool yes)
    {
        lock (Gate)
            if (game == Game && Phase == NetPhase.Playing) CastVote(game, game.LocalTurn, turn, yes);
    }

    private void OnDropVote(NetConnection c, int target, bool yes)
    {
        if (Game is { } g && Phase == NetPhase.Playing) CastVote(g, g.Start.TurnIndexOf(c.PlayerId), target, yes);
    }

    /// <summary>Seconds since the last turn of a turn player arrived.</summary>
    internal double StallSeconds(int turn) =>
        (uint)turn < (uint)_lastTurnAt.Length ? (Stopwatch.GetTimestamp() - _lastTurnAt[turn]) / (double)Stopwatch.Frequency : double.MaxValue;

    /// <summary>Test hook: pretends the last turn of <paramref name="turn"/> arrived <paramref name="secondsAgo"/> ago.</summary>
    public void SetStallForTests(int turn, double secondsAgo)
    {
        lock (Gate)
            if ((uint)turn < (uint)_lastTurnAt.Length) _lastTurnAt[turn] = Stopwatch.GetTimestamp() - (long)(secondsAgo * Stopwatch.Frequency);
    }

    private void CastVote(NetGame g, int voter, int target, bool yes)
    {
        if (voter < 0 || voter == target || (uint)target >= (uint)g.TurnPlayers || target == g.HostTurn) return;
        if (g.IsDropped(voter) || g.IsDropped(target)) return; // a dropped (or resigned) player has no vote
        if (yes && StallSeconds(target) < VoteMinStallSeconds) return;
        if (!_dropVotes.TryGetValue(target, out var set)) _dropVotes[target] = set = new SortedSet<int>();
        if (yes) set.Add(voter);
        else set.Remove(voter);
        Tally(g, target);
    }

    /// <summary>Counts the votes against <paramref name="target"/>: a strict majority of the remaining players (not
    /// dropped, the target excluded) drops it with the lobby's <c>OnDrop</c> action.</summary>
    private void Tally(NetGame g, int target)
    {
        _dropVotes.TryGetValue(target, out var set);
        set?.RemoveWhere(g.IsDropped);
        int remaining = 0;
        for (int t = 0; t < g.TurnPlayers; t++)
            if (t != target && !g.IsDropped(t)) remaining++;
        int needed = NetGame.VotesNeeded(remaining), votes = set?.Count ?? 0;
        if (votes > 0 && votes >= needed)
        {
            _dropVotes.Remove(target);
            SendTally(g, new DropVoteTally(target, 0, needed, []));
            DropByVote(g, target);
            return;
        }
        SendTally(g, new DropVoteTally(target, votes, needed, set?.ToArray() ?? []));
    }

    private void SendTally(NetGame g, DropVoteTally t)
    {
        g.SetVotes(t);
        Broadcast(TallyFrame(t));
    }

    private void DropByVote(NetGame g, int target)
    {
        int pid = g.Start.TurnPlayers[target];
        var conn = _conns.FirstOrDefault(x => x.PlayerId == pid);
        if (conn != null)
        {
            _conns.Remove(conn); // no Drop frame for it: it can't follow the game any more
            conn.Close("dropped by vote", Msg.Text(MsgType.Leave, "The other players voted to drop you from the game"));
        }
        DropPlayer(pid, g.NameOfTurn(target), "dropped by vote");
    }

    /// <summary>After a drop: the remaining votes are counted again (fewer players = fewer votes needed).</summary>
    private void RecountVotes(NetGame g)
    {
        foreach (int target in _dropVotes.Keys.ToList())
        {
            if (g.IsDropped(target)) _dropVotes.Remove(target);
            else Tally(g, target);
        }
    }

    /// <summary>A turn from <paramref name="turn"/> arrived: it is alive, the votes against it lapse.</summary>
    private void TurnArrived(NetGame g, int turn)
    {
        if ((uint)turn < (uint)_lastTurnAt.Length) _lastTurnAt[turn] = Stopwatch.GetTimestamp();
        if (_dropVotes.Remove(turn, out var set) && set.Count > 0) SendTally(g, new DropVoteTally(turn, 0, 0, []));
    }

    // ------------------------------------------------------------------ ping list (lobby and game)

    private void BroadcastPersonPings()
    {
        var d = new Dictionary<int, int> { [0] = 0 };
        foreach (var c in _conns)
            if (c.PlayerId > 0 && c.PlayerId != int.MaxValue) d[c.PlayerId] = c.RttMs < 0 ? -1 : (int)Math.Round(c.RttMs);
        SetPersonPings(d);
        Broadcast(Msg.Build(MsgType.LobbyPings, w =>
        {
            w.VarU((ulong)d.Count);
            foreach (var (pid, ms) in d.OrderBy(kv => kv.Key))
            {
                w.VarI(pid);
                w.VarI(ms);
            }
        }));
    }
}

using Rts.Net.Headless;
using Rts.Net.Lobby;
using Rts.Net.Session;
using Rts.Sim.Persistence;
using static Rts.Net.Tests.NetTestUtil;

namespace Rts.Net.Tests;

/// <summary>(10a-2) Observers, resigning players, drop votes and the ping list.</summary>
public class ObserverTests
{
    [Fact]
    public void LobbyCodecRoundTripsObserversRequestsAndTallies()
    {
        var l = LobbyState.CreateDefault("Host");
        l.Observers.Add(new NetSlot { Kind = NetSlotKind.Observer, PlayerId = 5, Name = "Watcher", Ready = true, MapReady = false, MapProgress = 40 });
        l.Observers.Add(new NetSlot { Kind = NetSlotKind.Observer, PlayerId = 6, Name = "Second" });
        var w = new ByteWriter(256);
        l.Write(w);
        var back = LobbyState.Read(new ByteReader(w.Span.ToArray()));
        Assert.Equal(2, back.Observers.Count);
        Assert.Equal("Watcher", back.Observers[0].Name);
        Assert.Equal(NetSlotKind.Observer, back.Observers[1].Kind);
        Assert.False(back.Observers[0].MapReady);
        Assert.Equal(40, back.Observers[0].MapProgress);
        Assert.Equal(1, back.ObserverOf(6));
        Assert.True(back.IsObserver(5));
        Assert.Equal(-1, back.SlotOf(5));
        back.Slots[1].Kind = NetSlotKind.AI;
        Assert.Contains("Watcher is downloading", back.Problem() ?? "");

        var q = new LobbyRequest { Observe = 1, MoveToSlot = 3 };
        var qw = new ByteWriter(64);
        q.Write(qw);
        var q2 = LobbyRequest.Read(new ByteReader(qw.Span.ToArray()));
        Assert.Equal(1, q2.Observe);
        Assert.Equal(3, q2.MoveToSlot);

        var t = new DropVoteTally(2, 1, 2, [0]);
        var frame = NetSession.TallyFrame(t);
        var r = new ByteReader(frame.AsSpan(5).ToArray()); // 4-byte length + type byte
        var t2 = NetSession.ReadTally(r)!;
        Assert.Equal(t.Target, t2.Target);
        Assert.Equal(t.Needed, t2.Needed);
        Assert.Equal([0], t2.Voters);
        Assert.Equal(1, NetGame.VotesNeeded(1));
        Assert.Equal(2, NetGame.VotesNeeded(2)); // a tie is not a majority
        Assert.Equal(2, NetGame.VotesNeeded(3));
    }

    [Fact]
    public void LobbyMovesToAndFromTheObservers()
    {
        var (host, clients) = MakeLobby(2, 0);
        try
        {
            var c = clients[0];
            c.Observe(true);
            WaitFor(() => host.Lobby.IsObserver(c.PlayerId), "client observing");
            Assert.Equal(-1, host.Lobby.SlotOf(c.PlayerId));
            Assert.Equal(NetSlotKind.Open, host.Lobby.Slots[1].Kind);
            WaitFor(() => c.IsLocalObserver, "client sees itself observing");
            // A full game: a new joiner becomes an observer.
            host.SetSlotKind(1, NetSlotKind.AI);
            var late = ClientSession.Join("127.0.0.1", host.Port, "Late");
            try
            {
                WaitFor(() => host.Lobby.Observers.Count == 2, "late joiner observing");
                Assert.Null(host.Lobby.Problem()); // observers need no Ready
                host.SetSlotKind(1, NetSlotKind.Open);
                c.Observe(false);
                WaitFor(() => host.Lobby.SlotOf(c.PlayerId) == 1, "client back in a slot");
                Assert.False(host.Lobby.Slots[1].Ready);
                late.Leave();
                WaitFor(() => host.Lobby.Observers.Count == 0, "observer left");
                WaitFor(() => c.PingOf(0) == 0 && host.PingOf(c.PlayerId) >= 0, "ping list");
            }
            finally { late.Leave(); }
        }
        finally { Close(host, clients); }
    }

    [Fact]
    public void ObserverGetsEveryTurnNeverHoldsTheGameAndStaysInSync()
    {
        var (host, clients) = MakeLobby(2, 1);
        var obs = ClientSession.Join("127.0.0.1", host.Port, "Watcher", observe: true);
        var peers = new List<HeadlessPeer>();
        HeadlessPeer? watcher = null;
        try
        {
            WaitFor(() => host.Lobby.Observers.Count == 1, "observer in the lobby");
            Assert.Null(host.StartGame());
            WaitFor(() => clients[0].Game != null && obs.Game != null, "Start");
            Assert.True(obs.Game!.IsObserver);
            Assert.Equal(2, obs.Game.TurnPlayers);
            peers.Add(new HeadlessPeer(host, Db, 1));
            peers.Add(new HeadlessPeer(clients[0], Db, 2));
            WaitFor(() => peers.All(p => p.Hooks.Started), "Go without the observer");
            // The players play 150 ticks before the observer even builds its simulation: nobody waits for it.
            foreach (var p in peers) p.Run(300, 0.3);
            WaitFor(() => peers.All(p => p.Sim.World.Tick >= 150), "players at tick 150", 60000);
            watcher = new HeadlessPeer(obs, Db, 9);
            Assert.True(watcher.Hooks.Started);
            watcher.Run(300, 0.5); // random commands are not even issued (no army)
            foreach (var p in peers.Append(watcher)) Assert.True(p.Wait(60000), $"{p.Session.LocalName} did not finish");
            foreach (var p in peers.Append(watcher)) Assert.Null(p.Fault);
            Assert.Equal(0, watcher.CommandsIssued);
            Assert.Equal(peers[0].Hashes, watcher.Hashes);
            Assert.Equal(peers[0].Sim.StateHash(), watcher.Sim.StateHash());
            Assert.Equal(-1, host.Game!.DesyncTick);
            Assert.Equal(-1, obs.Game.DesyncTick);
            // The observer leaves: nobody is dropped.
            obs.Leave();
            WaitFor(() => host.ChatLog.Any(l => l.Text.Contains("(observer) left")), "observer left line");
            Assert.All(host.Game.PlayerStatus(), s => Assert.False(s.Dropped));
        }
        finally
        {
            watcher?.Dispose();
            obs.Leave();
            Close(host, clients, peers);
        }
    }

    [Fact]
    public void DefeatedPlayerResignsAndKeepsWatchingInSync()
    {
        var (host, clients) = MakeLobby(3, 0);
        var peers = Start(host, clients);
        try
        {
            var quitter = peers[2];
            foreach (var p in peers) p.Run(200, 0.2, (p2, tick) => { if (p2 == quitter && tick == 60) p2.Game.Resign(); });
            foreach (var p in peers) Assert.True(p.Wait(60000), $"{p.Session.LocalName} did not finish");
            foreach (var p in peers) Assert.Null(p.Fault);
            Assert.True(quitter.Game.Resigned);
            Assert.True(host.Game!.PlayerStatus()[2].Dropped);
            Assert.True(clients[0].Game!.PlayerStatus()[2].Dropped);
            int army = host.Game.Start.TurnArmies[2];
            foreach (var p in peers) Assert.True(p.Sim.World.Armies[army].Defeated);
            foreach (var p in peers.Skip(1)) Assert.Equal(peers[0].Hashes, p.Hashes); // the resigned peer too
            WaitFor(() => host.ChatLog.Any(l => l.Text.Contains("is now observing")), "resign line");
        }
        finally { Close(host, clients, peers); }
    }

    [Fact]
    public void DropVoteNeedsAStrictMajorityOfTheRemainingPlayers()
    {
        var (host, clients) = MakeLobby(4, 0);
        var peers = Start(host, clients);
        try
        {
            var g = host.Game!;
            // Turn 3 stalls (its peer never runs); turn 1 resigns first (dropped, still connected).
            foreach (var p in peers.Take(3)) p.Run(400, 0, (p2, tick) => { if (p2 == peers[1] && tick == 1) p2.Game.Resign(); });
            WaitFor(() => g.PlayerStatus()[1].Dropped, "turn 1 resigned");
            WaitFor(() => g.WaitingForTurn == 3, "host waits for turn 3");
            // Too early: no vote counts before the stall.
            host.VoteDrop(g, 3, true);
            Assert.Null(g.Votes);
            host.SetStallForTests(3, 10);
            // The dropped (resigned) player's vote is ignored, even sent by hand.
            clients[0].VoteDrop(clients[0].Game!, 3, true);
            Thread.Sleep(300);
            Assert.Null(g.Votes);
            // Remaining voters: host + turn 2 → 2 votes needed; one vote is a tie, not a majority.
            host.VoteDrop(g, 3, true);
            Assert.Equal(1, g.Votes!.Votes);
            Assert.Equal(2, g.Votes.Needed);
            WaitFor(() => clients[1].Game!.Votes?.Votes == 1, "tally on the clients");
            Assert.False(g.PlayerStatus()[3].Dropped);
            clients[1].Game!.VoteDrop(3);
            WaitFor(() => g.PlayerStatus()[3].Dropped, "turn 3 dropped by vote");
            WaitFor(() => clients[1].Game!.PlayerStatus()[3].Dropped, "client sees the drop");
            Assert.Null(g.Votes);
            WaitFor(() => clients[2].CloseReason != null, "dropped player disconnected");
            foreach (var p in peers.Take(3)) Assert.True(p.Wait(60000), $"{p.Session.LocalName} did not finish");
            foreach (var p in peers.Take(3)) Assert.Null(p.Fault);
            Assert.Equal(peers[0].Hashes, peers[2].Hashes);
            Assert.Equal(peers[0].Hashes, peers[1].Hashes);
        }
        finally { Close(host, clients, peers); }
    }
}

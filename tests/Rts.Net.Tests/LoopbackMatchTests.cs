using Rts.Net.Headless;
using Rts.Net.Lobby;
using Rts.Net.Session;
using Rts.Net.Transport;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using static Rts.Net.Tests.NetTestUtil;

namespace Rts.Net.Tests;

/// <summary>Real TCP on 127.0.0.1: host + clients in one process, each peer's sim on its own thread.</summary>
public class LoopbackMatchTests
{
    private static void RunAll(List<HeadlessPeer> peers, int ticks, double rate, Action<HeadlessPeer, int>? perTick = null)
    {
        foreach (var p in peers) p.Run(ticks, rate, perTick);
        foreach (var p in peers) Assert.True(p.Wait(60000), $"{p.Session.LocalName} did not finish (tick {p.Sim.World.Tick}, waiting for {p.Game.WaitingFor})");
        foreach (var p in peers) Assert.Null(p.Fault);
    }

    private static void AssertSameHashes(HeadlessPeer a, HeadlessPeer b)
    {
        Assert.NotEmpty(a.Hashes);
        Assert.Equal(a.Hashes.Count, b.Hashes.Count);
        for (int i = 0; i < a.Hashes.Count; i++)
            Assert.True(a.Hashes[i] == b.Hashes[i], $"{a.Session.LocalName}/{b.Session.LocalName} differ at tick {a.Hashes[i].tick}");
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    public void PeersWithRandomCommandsStayInSync(int humans, int ais)
    {
        var (host, clients) = MakeLobby(humans, ais);
        var peers = Start(host, clients);
        try
        {
            RunAll(peers, 400, 0.4);
            foreach (var p in peers.Skip(1)) AssertSameHashes(peers[0], p);
            Assert.All(peers, p => Assert.True(p.CommandsIssued > 50));
            Assert.All(peers, p => Assert.Equal(-1, p.Game.DesyncTick));
            // Every peer stepped the same commands (every player's): the replays hold the same list.
            int count = peers[0].Replay().CommandCount;
            Assert.True(count > 50 * humans);
            Assert.All(peers, p => Assert.Equal(count, p.Replay().CommandCount));
            // The MP replay is a normal replay: it re-simulates without a desync.
            var log = peers[^1].Replay();
            var info = GameInfo.ForSkirmish(NetGameSetup.ToSkirmish(peers[^1].Game.Start), peers[^1].Sim, peers[^1].Game.LocalArmy, false);
            var player = new ReplayPlayer(info, log, Db, peers[^1].Sim.World.Terrain);
            Assert.True(player.RunToEnd());
            Assert.Equal(-1, player.DesyncTick);
            Assert.Equal(400, player.Tick);
        }
        finally { Close(host, clients, peers); }
    }

    [Theory]
    [InlineData(DropAction.AiTakeover)]
    [InlineData(DropAction.Defeat)]
    public void DroppedPlayerIsHandledIdenticallyByEveryPeer(DropAction action)
    {
        var (host, clients) = MakeLobby(3, 0, o => o.OnDrop = action);
        var peers = Start(host, clients);
        try
        {
            int dropped = clients[1].PlayerId;
            int droppedArmy = peers[2].Game.LocalArmy;
            RunAll(peers, 500, 0.3, (p, tick) =>
            {
                if (p == peers[0] && tick == 100) host.Disconnect(dropped, "test drop");
            });
            AssertSameHashes(peers[0], peers[1]);
            foreach (var p in peers.Take(2))
            {
                var army = p.Sim.World.Armies[droppedArmy];
                if (action == DropAction.AiTakeover) Assert.True(army.IsAI && !army.Defeated);
                else Assert.True(army.Defeated);
            }
            Assert.Contains(Drain(host), e => e is PlayerDroppedEvent d && d.Army == droppedArmy);
            // The dropped client noticed and finished its game offline, the others handed to the AI/defeated.
            var ev = Drain(clients[1]);
            Assert.Contains(ev, e => e is DisconnectedEvent);
            Assert.True(peers[2].Game.Offline);
            Assert.Equal(500, peers[2].Sim.World.Tick);
        }
        finally { Close(host, clients, peers); }
    }

    [Fact]
    public void DesyncIsDetectedAndReported()
    {
        var (host, clients) = MakeLobby(2, 0);
        var peers = Start(host, clients);
        try
        {
            RunAll(peers, 200, 0.2, (p, tick) =>
            {
                if (p == peers[1] && tick == 55) p.Sim.World.Units.Pos[p.Sim.World.Armies[0].Commander.Index].X += 0.5f; // corrupt one peer
            });
            WaitFor(() => host.Game!.DesyncTick >= 0 && clients[0].Game!.DesyncTick >= 0, "desync report");
            Assert.Equal(60, host.Game!.DesyncTick);
            Assert.Contains(Drain(clients[0]), e => e is DesyncEvent { Tick: 60 });
        }
        finally { Close(host, clients, peers); }
    }

    [Fact]
    public void SpeedAndPauseAreNegotiatedByTheHost()
    {
        var (host, clients) = MakeLobby(2, 0);
        var peers = Start(host, clients);
        try
        {
            clients[0].Game!.RequestSpeed(2f, true);
            WaitFor(() => host.Game!.Paused && clients[0].Game!.Paused, "pause everywhere");
            Assert.Equal(2f, host.Game!.Speed);
            host.Game.RequestSpeed(1f, false);
            WaitFor(() => !clients[0].Game!.Paused, "resume on the client");
            Assert.Contains(Drain(clients[0]), e => e is SpeedChangedEvent { Paused: true, By: "P1" });
        }
        finally { Close(host, clients, peers); }
    }

    [Fact]
    public void LobbyRequestsChatAndFullGames()
    {
        var host = HostSession.Start("Host", 0, lanAnnounce: false);
        var c = ClientSession.Join("127.0.0.1", host.Port, "Alice");
        try
        {
            WaitFor(() => c.LocalSlot == 1, "slot");
            c.Request(new LobbyRequest { Team = 2, Color = 5, Faction = "uef" });
            WaitFor(() => host.Lobby.Slots[1].Team == 2, "team change");
            Assert.Equal(5, host.Lobby.Slots[1].Color);
            Assert.Equal("Alice", host.Lobby.Slots[1].Name);
            c.SendChat("hi all");
            WaitFor(() => host.ChatLog.Any(l => l.Text == "hi all" && l.Name == "Alice"), "chat on host");
            host.SendChat("welcome");
            WaitFor(() => c.ChatLog.Any(l => l.Text == "welcome" && l.Name == "Host"), "chat on client");
            Assert.Equal("Alice is not ready", host.StartGame());
            // Only one open slot: the next join is refused.
            var ex = Assert.Throws<NetJoinException>(() => ClientSession.Join("127.0.0.1", host.Port, "Bob"));
            Assert.Contains("full", ex.Message);
            // The host closes the slot: Alice is kicked.
            host.SetSlotKind(1, NetSlotKind.Closed);
            WaitFor(() => c.Phase == NetPhase.Closed, "kick");
            Assert.Contains("removed", c.CloseReason);
        }
        finally
        {
            c.Leave();
            host.Leave();
        }
    }

    [Fact]
    public void LanBrowserHearsTheAnnouncement()
    {
        int port = 47700 + Environment.ProcessId % 200;
        using var browser = new LanBrowser(port);
        Assert.Null(browser.Error);
        using var ann = new LanAnnouncer(() => LanDiscovery.Encode(99, 1234, "Test game", "Host", 1, 2, false, "256 Plains"), port);
        WaitFor(() => browser.Games.Any(g => g.HostId == 99), "LAN announcement", 5000);
        var game = browser.Games.First(g => g.HostId == 99);
        Assert.Equal(1234, game.Port);
        Assert.Equal("Test game", game.Name);
        Assert.True(game.SameBuild);
    }
}

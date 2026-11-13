using Rts.Net.Headless;
using Rts.Net.Lobby;
using Rts.Net.Session;
using Rts.Net.Wire;
using Rts.Sim.Persistence;
using Xunit;
using Xunit.Abstractions;
using static Rts.Net.Tests.NetTestUtil;

namespace Rts.Net.Tests;

/// <summary>(10a-3) Multiplayer save/load: every peer writes the same save (id + state hash) at the same tick; a load
/// lobby seats the players by name, sends the save to a peer that lacks it (or has a changed copy), and the loaded
/// game continues exactly like the uninterrupted one; a missing human can be handed to the AI.</summary>
public class SaveLoadTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _out = output;

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "rts_mpsave_" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(d);
        return d;
    }

    private static void RunAll(List<HeadlessPeer> peers, int ticks, double rate)
    {
        foreach (var p in peers) p.Run(ticks, rate);
        foreach (var p in peers)
            Assert.True(p.Wait(60000), $"{p.Session.LocalName} did not finish (tick {p.Sim.World.Tick}, waiting for {p.Game.WaitingFor})"
                + string.Concat(peers.Where(q => q.Fault != null).Select(q => $"; {q.Session.LocalName} fault: {q.Fault}")));
        foreach (var p in peers) Assert.Null(p.Fault);
    }

    /// <summary>Waits for the host's <see cref="SaveResultEvent"/> (other events are kept in <paramref name="seen"/>).</summary>
    private static SaveResultEvent WaitResult(NetSession s, List<NetEvent> seen)
    {
        SaveResultEvent? r = null;
        WaitFor(() =>
        {
            while (s.TryGetEvent(out var e))
            {
                seen.Add(e);
                if (e is SaveResultEvent x) r = x;
            }
            return r != null;
        }, "save result", 30000);
        return r!;
    }

    /// <summary>A game of the host + <paramref name="humans"/>-1 clients (+ <paramref name="ais"/> AI) run to tick 120
    /// with random commands, saved for everybody as "mp_test" (each peer into its own folder), then run on to
    /// <paramref name="endTick"/> without commands. Returns the save tick, the folders and the host's hashes.</summary>
    private (int tick, string[] dirs, List<(int tick, ulong hash)> hostHashes) PlayAndSave(int humans, int ais, int endTick)
    {
        var (host, clients) = MakeLobby(humans, ais);
        var dirs = new string[humans];
        dirs[0] = host.SaveDir = TempDir();
        for (int i = 0; i < clients.Count; i++) dirs[i + 1] = clients[i].SaveDir = TempDir();
        var peers = Start(host, clients);
        try
        {
            RunAll(peers, 120, 0.3);
            Drain(host);
            Assert.Null(host.OrderSave("mp_test"));
            RunAll(peers, endTick, 0);
            var seen = new List<NetEvent>();
            var res = WaitResult(host, seen);
            _out.WriteLine(res.Text);
            Assert.True(res.Ok, res.Text);
            int tick = seen.OfType<SaveOrderedEvent>().Single().Tick;
            Assert.InRange(tick, 121, 130);
            foreach (var c in clients)
                WaitFor(() => { while (c.TryGetEvent(out var e)) if (e is SaveResultEvent { Ok: true }) return true; return false; }, "client result");
            Assert.All(peers, p => Assert.Equal(-1, p.Game.DesyncTick));
            return (tick, dirs, peers[0].Hashes.ToList());
        }
        finally { Close(host, clients, peers); }
    }

    private static string SavePath(string dir) => Path.Combine(dir, "mp_test" + SaveStore.SaveExt);

    [Fact]
    public void EveryPeerWritesTheSameSaveAtTheSameTick()
    {
        var (tick, dirs, _) = PlayAndSave(3, 1, 200);
        var infos = dirs.Select(d => MultiplayerSaveInfo.ReadStateHash(File.ReadAllBytes(SavePath(d)))).ToList();
        var first = infos[0];
        Assert.NotNull(first.info.Multiplayer);
        foreach (var (info, hash) in infos)
        {
            Assert.Equal(first.info.Multiplayer!.SaveId, info.Multiplayer!.SaveId);
            Assert.Equal(first.stateHash, hash);
            Assert.Equal(hash, info.Multiplayer.StateHash);
            Assert.Equal(tick, info.Ticks);
            Assert.Equal(3, info.Multiplayer.Humans);
        }
        // Per-peer header: who wrote it.
        Assert.Equal(new[] { "Host", "P1", "P2" }, infos.Select(i => i.info.Multiplayer!.SavedBy));
        Assert.Equal(new[] { 0, 1, 2 }, infos.Select(i => i.info.Multiplayer!.SavedBySlot));
        // The saved lobby: three humans + one AI, re-openable as a load lobby.
        var lobby = LobbyState.ForLoad(first.info, first.stateHash, 1000);
        Assert.Equal(new[] { "Host", "P1", "P2" }, lobby.Slots.Where(LobbyState.IsFreeSavedSlot).Select(s => s.SavedName));
        Assert.Single(lobby.Slots, s => s.Kind == NetSlotKind.AI);
        Assert.Equal(tick, lobby.Load!.Tick);
    }

    [Fact]
    public void LoadedGameContinuesLikeTheUninterruptedOneAndTheMissingFileIsSent()
    {
        var (tick, dirs, uninterrupted) = PlayAndSave(2, 1, 400);
        var hostDir = dirs[0];
        var clientDir = TempDir(); // P1 lacks the save: it is sent by the host
        var host = HostSession.Start("Host", 0, lanAnnounce: false);
        host.SaveDir = hostDir;
        host.OpenLoadLobby(SavePath(hostDir));
        Assert.True(host.Lobby.IsLoad);
        Assert.Equal(0, host.LocalSlot); // the host's own copy: its saved slot
        var c = ClientSession.Join("127.0.0.1", host.Port, "P1", saveDir: clientDir);
        var clients = new List<ClientSession> { c };
        List<HeadlessPeer>? peers = null;
        try
        {
            WaitFor(() => c.SavePath != null, "save downloaded");
            Assert.Equal(1, c.LocalSlot); // claimed by name
            Assert.True(File.Exists(Path.Combine(clientDir, "mp_test" + SaveStore.SaveExt)));
            Assert.Equal(MultiplayerSaveInfo.ReadStateHash(File.ReadAllBytes(SavePath(hostDir))).stateHash,
                MultiplayerSaveInfo.ReadStateHash(File.ReadAllBytes(c.SavePath!)).stateHash);
            c.SetReady(true);
            WaitFor(() => host.Lobby.Problem() == null, "load lobby ready");
            peers = Start(host, clients);
            Assert.All(peers, p => Assert.Equal(tick, p.Sim.World.Tick));
            RunAll(peers, 400, 0);
            Assert.Equal(peers[0].Hashes, peers[1].Hashes);
            var expected = uninterrupted.Where(h => h.tick > tick).ToList();
            Assert.NotEmpty(expected);
            Assert.Equal(expected, peers[0].Hashes.Where(h => h.tick > tick).ToList());
            Assert.All(peers, p => Assert.Equal(-1, p.Game.DesyncTick));
        }
        finally { Close(host, clients, peers); }
    }

    [Fact]
    public void ChangedCopyIsReplacedAndTamperedUploadIsRefused()
    {
        var (_, dirs, _) = PlayAndSave(2, 0, 140);
        var clientDir = dirs[1];
        // P1's copy was changed (one byte of the payload): it does not count as the save and is downloaded again.
        var path = SavePath(clientDir);
        var bytes = File.ReadAllBytes(path);
        bytes[^10] ^= 0x5A;
        File.WriteAllBytes(path, bytes);
        var host = HostSession.Start("Host", 0, lanAnnounce: false);
        host.SaveDir = dirs[0];
        host.OpenLoadLobby(SavePath(dirs[0]));
        var c = ClientSession.Join("127.0.0.1", host.Port, "P1", saveDir: clientDir);
        try
        {
            WaitFor(() => c.SavePath != null, "save downloaded again");
            Assert.Equal(Path.GetFullPath(path), c.SavePath);
            Assert.Equal(File.ReadAllBytes(SavePath(dirs[0])).Length, File.ReadAllBytes(path).Length);
            MultiplayerSaveInfo.ReadStateHash(File.ReadAllBytes(path)); // decodes again
            WaitFor(() => host.Lobby.Slots[1].SaveReady, "host sees the save ready");
        }
        finally { Close(host, [c]); }

        // A corrupted upload: the client refuses it and the host can't start.
        var host2 = HostSession.Start("Host", 0, lanAnnounce: false);
        host2.SaveDir = dirs[0];
        host2.TamperSaveUpload = b => b[b.Length / 2] ^= 0xFF;
        host2.OpenLoadLobby(SavePath(dirs[0]));
        var c2 = ClientSession.Join("127.0.0.1", host2.Port, "P1", saveDir: TempDir());
        try
        {
            WaitFor(() => c2.SaveTransfer?.State == MapTransferState.Failed, "client refuses the upload");
            c2.SetReady(true);
            Thread.Sleep(200);
            Assert.False(host2.Lobby.Slots[1].SaveReady);
            Assert.NotNull(host2.StartGame());
        }
        finally { Close(host2, [c2]); }
    }

    [Fact]
    public void PlayersClaimSavedSlotsByNameAndAMissingOneCanBeAi()
    {
        var (tick, dirs, _) = PlayAndSave(3, 0, 140);
        var host = HostSession.Start("Host", 0, lanAnnounce: false);
        host.SaveDir = dirs[0];
        host.OpenLoadLobby(SavePath(dirs[0]));
        var zed = ClientSession.Join("127.0.0.1", host.Port, "Zed", saveDir: dirs[2]);
        var p1 = ClientSession.Join("127.0.0.1", host.Port, "p1", saveDir: dirs[1]); // names match case-insensitively
        var clients = new List<ClientSession> { zed, p1 };
        List<HeadlessPeer>? peers = null;
        try
        {
            WaitFor(() => p1.LocalSlot == 1, "P1 takes its slot");
            WaitFor(() => zed.IsLocalObserver, "an unknown name observes");
            Assert.NotNull(host.Lobby.Problem()); // P2's slot is free
            Assert.Contains("P2", host.Lobby.Problem());
            // Zed may take the free saved slot (keeps the saved faction/team/colour).
            var saved = host.Lobby.Slots[2].Clone();
            zed.Request(new LobbyRequest { Observe = 0, MoveToSlot = 2 });
            WaitFor(() => zed.LocalSlot == 2, "Zed takes P2's slot");
            Assert.Equal((saved.Faction, saved.Team, saved.Color), (host.Lobby.Slots[2].Faction, host.Lobby.Slots[2].Team, host.Lobby.Slots[2].Color));
            // Back to observing; the host hands P2's army to the AI.
            zed.Observe(true);
            WaitFor(() => zed.IsLocalObserver && LobbyState.IsFreeSavedSlot(host.Lobby.Slots[2]), "slot free again");
            host.SetSlotKind(2, NetSlotKind.AI);
            Assert.Equal(NetSlotKind.AI, host.Lobby.Slots[2].Kind);
            host.SetSlot(2, team: 3); // saved settings are fixed
            Assert.Equal(saved.Team, host.Lobby.Slots[2].Team);
            WaitFor(() => zed.SavePath != null && p1.SavePath != null, "both have the save");
            p1.SetReady(true);
            WaitFor(() => host.Lobby.Problem() == null, "ready");
            peers = Start(host, clients);
            Assert.Equal(-1, peers[1].Game.LocalArmy); // Zed observes
            RunAll(peers, tick + 30, 0);
            Assert.All(peers, p => Assert.True(p.Sim.World.Armies[2].IsAI, "the AI took P2's army over"));
            Assert.False(peers[0].Sim.World.Armies[1].IsAI);
            Assert.Equal(peers[0].Hashes, peers[2].Hashes);
            Assert.Equal(peers[0].Hashes, peers[1].Hashes);
        }
        finally { Close(host, clients, peers); }
    }

    [Fact]
    public void SaveRequestOfAClientAndPausedGames()
    {
        var (host, clients) = MakeLobby(2, 0);
        host.SaveDir = TempDir();
        clients[0].SaveDir = TempDir();
        var peers = Start(host, clients);
        try
        {
            RunAll(peers, 60, 0.2);
            // Paused: the save still runs every peer up to its tick, then the game stays paused.
            host.Game!.RequestSpeed(1, paused: true);
            WaitFor(() => peers.All(p => p.Game.Paused), "paused");
            foreach (var p in peers) p.Run(10000, 0);
            clients[0].Game!.RequestSave("by client");
            var seen = new List<NetEvent>();
            var res = WaitResult(host, seen);
            Assert.True(res.Ok, res.Text);
            var order = seen.OfType<SaveOrderedEvent>().Single();
            Assert.Equal("P1", order.By);
            Thread.Sleep(300);
            Assert.All(peers, p => Assert.Equal(order.Tick, p.Sim.World.Tick));
            Assert.True(File.Exists(Path.Combine(clients[0].SaveDir, "by client" + SaveStore.SaveExt)));
            // A second order while one is pending is refused; the host refuses nothing here.
            Assert.Null(host.OrderSave("again"));
            Assert.Equal("A save is already in progress", host.OrderSave("third"));
        }
        finally { Close(host, clients, peers); }
    }
}

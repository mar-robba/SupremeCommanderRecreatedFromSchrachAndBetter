using Rts.Net.Headless;
using Rts.Net.Lobby;
using Rts.Net.Session;
using Rts.Sim.Data;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Rts.Net.Tests;

public static class NetTestUtil
{
    private static BlueprintDb? _db;
    public static BlueprintDb Db => _db ??= BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    public static void WaitFor(Func<bool> cond, string what, int timeoutMs = 10000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("timed out waiting for " + what);
            Thread.Sleep(5);
        }
    }

    /// <summary>A loopback lobby: the host + <paramref name="humans"/>-1 joined clients (ready) + AI slots.</summary>
    public static (HostSession host, List<ClientSession> clients) MakeLobby(int humans, int ais, Action<LobbyOptions>? options = null)
    {
        var host = HostSession.Start("Host", 0, lanAnnounce: false);
        for (int i = 1; i < humans; i++) host.SetSlotKind(i, NetSlotKind.Open);
        for (int j = 0; j < ais; j++) host.SetSlotKind(humans + j, NetSlotKind.AI);
        var o = host.Lobby.Options.Clone();
        o.Seed = 7;
        options?.Invoke(o);
        host.SetOptions(o);
        var clients = new List<ClientSession>();
        for (int i = 1; i < humans; i++)
        {
            var c = ClientSession.Join("127.0.0.1", host.Port, "P" + i);
            clients.Add(c);
            WaitFor(() => c.LocalSlot >= 0, "client slot");
            c.SetReady(true);
        }
        WaitFor(() => host.Lobby.Problem() == null, "everybody ready");
        return (host, clients);
    }

    /// <summary>Starts the game and builds one headless peer per session (host first).</summary>
    public static List<HeadlessPeer> Start(HostSession host, List<ClientSession> clients)
    {
        Assert.Null(host.StartGame());
        foreach (var c in clients) WaitFor(() => c.Game != null, "Start on the client");
        var peers = new List<HeadlessPeer> { new(host, Db, 1) };
        int seed = 2;
        foreach (var c in clients) peers.Add(new HeadlessPeer(c, Db, seed++));
        WaitFor(() => peers.All(p => p.Hooks.Started), "Go");
        return peers;
    }

    public static void Close(HostSession host, List<ClientSession> clients, List<HeadlessPeer>? peers = null)
    {
        if (peers != null) foreach (var p in peers) p.Dispose();
        foreach (var c in clients) c.Leave();
        host.Leave();
    }

    public static List<NetEvent> Drain(NetSession s)
    {
        var list = new List<NetEvent>();
        while (s.TryGetEvent(out var e)) list.Add(e);
        return list;
    }
}

using System.Diagnostics;
using Rts.Net.Headless;
using Rts.Net.Lobby;
using Rts.Net.Session;
using Rts.Sim.Core;
using Rts.Sim.Data;

/// <summary>
/// Multiplayer self-test: a host and N-1 clients over real TCP on 127.0.0.1 in this process, each peer's sim on its own
/// thread running the lockstep as fast as the turns allow with random commands. Checks that every peer has the same
/// StateHash every 10 ticks (exit 1 on a desync/fault). "drop" disconnects the last client halfway: the others must
/// stay in sync with its army handed to the AI.
/// Usage: Rts.Bench mp [peers=4] [ticks=1200] [mapSize=256] [aiSlots=1] [threads=0] [drop]
/// </summary>
static class MpSelfTest
{
    public static int Run(string[] args)
    {
        bool drop = args.Contains("drop");
        args = args.Where(a => a != "drop").ToArray();
        int peersN = args.Length > 0 ? Math.Clamp(int.Parse(args[0]), 2, 8) : 4;
        int ticks = args.Length > 1 ? int.Parse(args[1]) : 1200;
        int mapSize = args.Length > 2 ? int.Parse(args[2]) : 256;
        int ais = args.Length > 3 ? int.Parse(args[3]) : 1;
        if (args.Length > 4 && int.Parse(args[4]) > 0) Par.MaxThreads = int.Parse(args[4]);
        ais = Math.Clamp(ais, 0, LobbyState.MaxSlots - peersN);

        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        var host = HostSession.Start("Host", 0, lanAnnounce: false);
        var clients = new List<ClientSession>();
        var peers = new List<HeadlessPeer>();
        try
        {
            for (int i = 1; i < peersN; i++) host.SetSlotKind(i, NetSlotKind.Open);
            for (int j = 0; j < ais; j++) host.SetSlotKind(peersN + j, NetSlotKind.AI);
            var o = host.Lobby.Options.Clone();
            o.MapSize = mapSize;
            o.Seed = 11;
            host.SetOptions(o);
            for (int i = 1; i < peersN; i++)
            {
                var c = ClientSession.Join("127.0.0.1", host.Port, "Peer" + i);
                clients.Add(c);
                Wait(() => c.LocalSlot >= 0);
                c.SetReady(true);
            }
            Wait(() => host.Lobby.Problem() == null);
            var setup = Stopwatch.StartNew();
            if (host.StartGame() is string problem) throw new InvalidOperationException(problem);
            foreach (var c in clients) Wait(() => c.Game != null);
            peers.Add(new HeadlessPeer(host, db, 1));
            for (int i = 0; i < clients.Count; i++) peers.Add(new HeadlessPeer(clients[i], db, 2 + i));
            Wait(() => peers.All(p => p.Hooks.Started));
            Console.WriteLine($"mp: {peersN} peers + {ais} AI, map {mapSize}, delay {host.Game!.Start.Delay}, setup {setup.ElapsedMilliseconds} ms (port {host.Port})");

            int dropId = clients.Count > 1 && drop ? clients[^1].PlayerId : -1;
            var run = Stopwatch.StartNew();
            foreach (var p in peers)
                p.Run(ticks, 0.4, (peer, tick) =>
                {
                    if (dropId >= 0 && peer == peers[0] && tick == ticks / 2) host.Disconnect(dropId, "self-test drop");
                });
            foreach (var p in peers)
                if (!p.Wait(600_000)) throw new TimeoutException($"{p.Session.LocalName} stuck at tick {p.Sim.World.Tick}");
            double sec = run.Elapsed.TotalSeconds;

            int fails = 0;
            var reference = peers[0].Hashes;
            int compared = dropId >= 0 ? peers.Count - 1 : peers.Count; // the dropped peer continues alone: not compared
            for (int k = 0; k < peers.Count; k++)
            {
                var p = peers[k];
                var (sent, received) = p.Session.Traffic;
                bool same = k >= compared || p.Hashes.SequenceEqual(reference);
                if (!same || p.Fault != null) fails++;
                Console.WriteLine($"  {p.Session.LocalName,-6} tick {p.Sim.World.Tick}  commands {p.CommandsIssued,5}  waited {p.HeldMs,7:F0} ms  " +
                    $"tcp out {sent / 1024.0,7:F1} KB in {received / 1024.0,7:F1} KB  hashes {(k >= compared ? "(dropped: offline)" : same ? "OK" : "DIFFERENT")}" +
                    (p.Fault != null ? "  FAULT " + p.Fault.Message : ""));
            }
            int desync = host.Game.DesyncTick;
            Console.WriteLine($"mp: {ticks} ticks in {sec:F2} s ({ticks / sec:F0} ticks/s = {ticks / sec / 10:F1}x real time), " +
                $"{reference.Count} hash checks, desync {(desync < 0 ? "none" : "at tick " + desync)}, replay commands {peers[0].Replay().CommandCount}");
            if (dropId >= 0)
            {
                int army = peers[^1].Game.LocalArmy;
                Console.WriteLine($"mp: dropped {peers[^1].Session.LocalName} (army {army}) at tick ~{ticks / 2}: host sees IsAI={peers[0].Sim.World.Armies[army].IsAI}");
            }
            bool ok = fails == 0 && desync < 0;
            Console.WriteLine(ok ? "mp: OK" : "mp: FAILED");
            return ok ? 0 : 1;
        }
        finally
        {
            foreach (var p in peers) p.Dispose();
            foreach (var c in clients) c.Leave();
            host.Leave();
        }
    }

    private static void Wait(Func<bool> cond)
    {
        var sw = Stopwatch.StartNew();
        while (!cond())
        {
            if (sw.ElapsedMilliseconds > 20000) throw new TimeoutException("mp self-test: timed out");
            Thread.Sleep(5);
        }
    }
}

using System.Diagnostics;
using Rts.Net.Headless;
using Rts.Net.Session;
using Rts.Sim.Data;

/// <summary>(10a-3) The "save=TICK" step of the multiplayer self-test: save on every peer, then load in a new session.</summary>
static partial class MpSelfTest
{
    private const string SaveName = "mp_selftest";

    private static string NewSaveDir(List<string> all)
    {
        var d = Path.Combine(Path.GetTempPath(), "rts_mpbench_" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(d);
        all.Add(d);
        return d;
    }

    /// <summary>Runs every peer to <paramref name="saveTick"/> with random commands, then the host orders the save.
    /// The run goes on (without commands) in the caller; the result is awaited here after a short run.</summary>
    private static void RunToSave(HostSession host, List<HeadlessPeer> peers, List<HeadlessPeer> obsPeers, int saveTick)
    {
        foreach (var p in peers) p.Run(saveTick, 0.4);
        foreach (var p in obsPeers) p.Run(saveTick, 0);
        foreach (var p in peers.Concat(obsPeers))
            if (!p.Wait(600_000)) throw new TimeoutException($"{p.Session.LocalName} stuck at tick {p.Sim.World.Tick}");
        while (host.TryGetEvent(out _)) { }
        if (host.OrderSave(SaveName) is string refused) throw new InvalidOperationException("save refused: " + refused);
        Console.WriteLine($"mp: save ordered at tick {saveTick}");
    }

    /// <summary>Waits for the host's save result (every peer's state hash at the save tick compared). 0 = saved.</summary>
    private static int WaitSaveResult(HostSession host)
    {
        SaveResultEvent? r = null;
        Wait(() =>
        {
            while (host.TryGetEvent(out var e))
                if (e is SaveResultEvent x) r = x;
                else if (e is SaveOrderedEvent o) Console.WriteLine($"mp: saving at tick {o.Tick}");
            return r != null;
        }, 40000, () => "no save result");
        Console.WriteLine($"mp: save result: {r!.Text}");
        return r.Ok ? 0 : 1;
    }

    /// <summary>After the first run: reads the save result, opens a load lobby on the host's copy, rejoins the clients by
    /// name (the last one with an empty save folder, so the host sends the file), runs the loaded game to the end and
    /// compares its hashes with the uninterrupted run. Returns the number of failures.</summary>
    private static int LoadAndCompare(BlueprintDb db, string savePath, List<string> names, List<string> saveDirs,
        int saveTick, int ticks, List<(int tick, ulong hash)> reference)
    {
        int fails = 0;
        string hostDir = Path.GetDirectoryName(savePath)!;
        var host = HostSession.Start("Host", 0, lanAnnounce: false);
        var clients = new List<ClientSession>();
        var peers = new List<HeadlessPeer>();
        try
        {
            host.SaveDir = hostDir;
            var load = Stopwatch.StartNew();
            host.OpenLoadLobby(savePath);
            int fileTick = host.Lobby.Load!.Tick;
            Console.WriteLine($"mp: load lobby on {Path.GetFileName(savePath)} (tick {fileTick}, {host.Lobby.Load.FileBytes / 1024} KB)");
            for (int i = 0; i < names.Count; i++)
            {
                bool lacks = i == names.Count - 1;
                // the other clients reuse the folder they saved into (each peer wrote its own copy)
                string dir = lacks ? NewSaveDir(saveDirs) : FindCopy(saveDirs, names[i], hostDir);
                var c = ClientSession.Join("127.0.0.1", host.Port, names[i], saveDir: dir);
                clients.Add(c);
                var t = Stopwatch.StartNew();
                Wait(() => c.SavePath != null || c.SaveTransfer?.State == Rts.Net.Session.MapTransferState.Failed, 60000);
                if (c.SavePath == null) throw new InvalidOperationException($"{names[i]}: save transfer failed: {c.SaveTransfer?.Error}");
                Console.WriteLine($"  {names[i],-6} slot {c.LocalSlot}  save {(lacks ? "downloaded" : "present")} in {t.ElapsedMilliseconds} ms");
                c.SetReady(true);
            }
            Wait(() => host.Lobby.Problem() == null, 20000, () => "load lobby: " + host.Lobby.Problem());
            if (host.StartGame() is string problem) throw new InvalidOperationException(problem);
            foreach (var c in clients) Wait(() => c.Game != null);
            peers.Add(new HeadlessPeer(host, db, 1));
            for (int i = 0; i < clients.Count; i++) peers.Add(new HeadlessPeer(clients[i], db, 2 + i));
            Wait(() => peers.All(p => p.Hooks.Started));
            Console.WriteLine($"mp: loaded and started in {load.ElapsedMilliseconds} ms at tick {peers[0].Sim.World.Tick}");
            foreach (var p in peers) p.Run(ticks, 0);
            foreach (var p in peers)
                if (!p.Wait(600_000)) throw new TimeoutException($"{p.Session.LocalName} stuck at tick {p.Sim.World.Tick}");
            var expected = reference.Where(h => h.tick > fileTick).ToList();
            foreach (var p in peers)
            {
                var got = p.Hashes.Where(h => h.tick > fileTick).ToList();
                bool same = got.SequenceEqual(expected) && expected.Count > 0;
                if (!same || p.Fault != null || p.Game.DesyncTick >= 0) fails++;
                Console.WriteLine($"  {p.Session.LocalName,-6} loaded tick {fileTick} -> {p.Sim.World.Tick}  {got.Count} hashes vs the uninterrupted run: " +
                    (same ? "OK" : "DIFFERENT") + (p.Fault != null ? "  FAULT " + p.Fault.Message : ""));
            }
            if (fileTick < saveTick) fails++;
        }
        finally
        {
            foreach (var p in peers) p.Dispose();
            foreach (var c in clients) c.Leave();
            host.Leave();
        }
        return fails;
    }

    /// <summary>The folder among <paramref name="dirs"/> that holds the save written by <paramref name="name"/>.</summary>
    private static string FindCopy(List<string> dirs, string name, string hostDir)
    {
        foreach (var d in dirs)
        {
            if (d == hostDir) continue;
            var f = Path.Combine(d, SaveName + Rts.Sim.Persistence.SaveStore.SaveExt);
            if (!File.Exists(f)) continue;
            var info = Rts.Sim.Persistence.MultiplayerSaveInfo.ReadStateHash(File.ReadAllBytes(f)).info;
            if (string.Equals(info.Multiplayer?.SavedBy, name, StringComparison.OrdinalIgnoreCase)) return d;
        }
        throw new InvalidOperationException($"no copy of the save written by {name}");
    }

    private static void Wait(Func<bool> cond, int timeoutMs, Func<string>? why = null)
    {
        var sw = Stopwatch.StartNew();
        while (!cond())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("mp self-test: timed out" + (why != null ? " (" + why() + ")" : ""));
            Thread.Sleep(5);
        }
    }
}

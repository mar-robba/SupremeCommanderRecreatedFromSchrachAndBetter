using Rts.Net.Lobby;
using Rts.Net.Session;
using Rts.Net.Transport;
using Rts.Net.Wire;
using Rts.Sim.Map;
using Rts.Sim.Persistence;
using Xunit;
using Xunit.Abstractions;
using static Rts.Net.Tests.NetTestUtil;

namespace Rts.Net.Tests;

/// <summary>(8b-3) Map files in the lobby: codec, the pack map present on both sides, a missing map downloaded from the
/// host, a corrupted upload refused, old protocol peers refused with both versions named.</summary>
public class MapTransferTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _out = output;

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "rts_maptest_" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>A map file found nowhere else (unique id and content), in a fresh folder.</summary>
    private static (string dir, string id) UniqueMap(int seed)
    {
        var dir = TempDir();
        string id = "nettest_" + Guid.NewGuid().ToString("N")[..8];
        var t = MapGenerator.Generate(new MapGenSettings { Size = 256, Players = 2, Seed = 1000 + seed + Environment.TickCount % 5000, Preset = MapPreset.Lakes });
        MapFile.Save(Path.Combine(dir, id + MapFile.Extension), t, new MapFileInfo { Name = "Net Test", Players = 2, Biome = Biome.Tundra });
        return (dir, id);
    }

    [Fact]
    public void LobbyCodecRoundTripsMapFields()
    {
        var l = LobbyState.CreateDefault("H");
        l.Slots[1].Kind = NetSlotKind.Human;
        l.Slots[1].MapReady = false;
        l.Slots[1].MapProgress = 42;
        var o = l.Options;
        o.MapFile = "great_lakes";
        o.MapHash = 0xDEADBEEF12345678UL;
        o.MapName = "Great Lakes";
        o.MapPlayers = 8;
        o.MapBytes = 718249;
        o.MapBiome = (byte)Biome.Desert;
        o.Biome = 1 + (byte)Biome.Lava;
        var w = new ByteWriter(256);
        l.Write(w);
        var back = LobbyState.Read(new ByteReader(w.Span.ToArray()));
        var b = back.Options;
        Assert.Equal((o.MapFile, o.MapHash, o.MapName, o.MapPlayers, o.MapBytes, o.MapBiome, o.Biome), (b.MapFile, b.MapHash, b.MapName, b.MapPlayers, b.MapBytes, b.MapBiome, b.Biome));
        Assert.Equal(Biome.Lava, b.EffectiveBiome);
        Assert.False(back.Slots[1].MapReady);
        Assert.Equal(42, back.Slots[1].MapProgress);
        Assert.True(back.Slots[0].MapReady);
        b.Biome = 0;
        Assert.Equal(Biome.Desert, b.EffectiveBiome); // map default
    }

    [Fact]
    public void PackMapPresentOnBothSidesStartsInSync()
    {
        var clientDir = TempDir();
        var host = HostSession.Start("Host", 0, lanAnnounce: false);
        var o = host.Lobby.Options.Clone();
        o.MapFile = "twin_meadows";
        o.Biome = 1 + (byte)Biome.Desert;
        host.SetOptions(o);
        Assert.NotEqual(0UL, host.Lobby.Options.MapHash);
        Assert.Equal(2, host.Lobby.Options.MapPlayers);
        var c = ClientSession.Join("127.0.0.1", host.Port, "P1", mapDir: clientDir);
        var clients = new List<ClientSession> { c };
        try
        {
            WaitFor(() => c.MapTransfer?.Ready == true, "client has the map");
            Assert.Empty(Directory.GetFiles(clientDir)); // found in data/maps: nothing downloaded
            c.SetReady(true);
            WaitFor(() => host.Lobby.Problem() == null, "everybody ready");
            var peers = Start(host, clients);
            Assert.Equal(256, peers[0].Sim.World.Terrain.Width);
            Assert.Equal(peers[0].Sim.StateHash(), peers[1].Sim.StateHash());
            var info = GameInfo.ForSkirmish(NetGameSetup.ToSkirmish(peers[1].Game.Start, NetGameSetup.LoadMap(peers[1].Game.Start, clientDir)), peers[1].Sim, 1, false);
            Assert.Equal("twin_meadows", info.MapFile);
            Assert.Equal("desert", info.Biome);
            Close(host, clients, peers);
        }
        finally
        {
            Close(host, clients);
            Directory.Delete(clientDir, true);
        }
    }

    [Fact]
    public void MissingMapIsDownloadedVerifiedAndUsed()
    {
        var (hostDir, id) = UniqueMap(1);
        var clientDir = TempDir();
        var host = HostSession.Start("Host", 0, lanAnnounce: false);
        host.MapDir = hostDir;
        var o = host.Lobby.Options.Clone();
        o.MapFile = id;
        host.SetOptions(o);
        Assert.Equal(id, host.Lobby.Options.MapFile);
        var c = ClientSession.Join("127.0.0.1", host.Port, "P1", mapDir: clientDir);
        var clients = new List<ClientSession> { c };
        try
        {
            WaitFor(() => c.MapTransfer?.State is MapTransferState.Present or MapTransferState.Failed, "map transfer done");
            Assert.Equal(MapTransferState.Present, c.MapTransfer!.State);
            Assert.Equal(host.Lobby.Options.MapBytes, c.MapTransfer.Received);
            var stored = MapLibrary.Find(id, host.Lobby.Options.MapHash, clientDir);
            Assert.NotNull(stored);
            Assert.StartsWith(Path.GetFullPath(clientDir), stored);
            Assert.Equal(Biome.Tundra, host.Lobby.Options.EffectiveBiome);
            WaitFor(() => host.Lobby.Slots[1].MapReady, "host sees the map ready");
            c.SetReady(true);
            WaitFor(() => host.Lobby.Problem() == null, "everybody ready");
            var peers = Start(host, clients);
            Assert.Equal(peers[0].Sim.StateHash(), peers[1].Sim.StateHash());
            Close(host, clients, peers);
        }
        finally
        {
            Close(host, clients);
            Directory.Delete(hostDir, true);
            Directory.Delete(clientDir, true);
        }
    }

    [Fact]
    public void CorruptedUploadIsRefused()
    {
        var (hostDir, id) = UniqueMap(2);
        var clientDir = TempDir();
        var host = HostSession.Start("Host", 0, lanAnnounce: false);
        host.MapDir = hostDir;
        host.TamperMapUpload = b => b[^8] ^= 0x5A; // inside the compressed payload
        var o = host.Lobby.Options.Clone();
        o.MapFile = id;
        host.SetOptions(o);
        var c = ClientSession.Join("127.0.0.1", host.Port, "P1", mapDir: clientDir);
        var clients = new List<ClientSession> { c };
        try
        {
            WaitFor(() => c.MapTransfer?.State is MapTransferState.Present or MapTransferState.Failed, "map transfer done");
            Assert.Equal(MapTransferState.Failed, c.MapTransfer!.State);
            Assert.Null(MapLibrary.Find(id, host.Lobby.Options.MapHash, clientDir));
            c.SetReady(true);
            Thread.Sleep(300);
            Assert.False(host.Lobby.Slots[1].Ready); // the host refuses Ready without the map
            Assert.Contains("downloading the map", host.Lobby.Problem());
            Assert.NotNull(host.StartGame());
        }
        finally
        {
            Close(host, clients);
            Directory.Delete(hostDir, true);
            Directory.Delete(clientDir, true);
        }
    }

    /// <summary>A pack-sized 1024 map (8 players, heights quantized like the pack, ~700 KB) crosses the window many times
    /// over loopback, unthrottled; the time is printed (detailed logger) for DEV_GUIDE "Reference numbers".</summary>
    [Fact]
    public void BigMapTransfersInLoopback()
    {
        var hostDir = TempDir();
        string id = "nettest_big_" + Guid.NewGuid().ToString("N")[..8];
        var t = MapFile.Quantize(MapGenerator.Generate(new MapGenSettings { Size = 1024, Players = 8, Seed = 9000 + Environment.TickCount % 5000, Preset = MapPreset.Lakes }), 8);
        MapFile.Save(Path.Combine(hostDir, id + MapFile.Extension), t, new MapFileInfo { Name = "Big Net Test", Players = 8 });
        var clientDir = TempDir();
        var host = HostSession.Start("Host", 0, lanAnnounce: false);
        host.MapDir = hostDir;
        var o = host.Lobby.Options.Clone();
        o.MapFile = id;
        host.SetOptions(o);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var c = ClientSession.Join("127.0.0.1", host.Port, "P1", mapDir: clientDir);
        var clients = new List<ClientSession> { c };
        try
        {
            WaitFor(() => c.MapTransfer?.State is MapTransferState.Present or MapTransferState.Failed, "big map transfer done", 30000);
            Assert.Equal(MapTransferState.Present, c.MapTransfer!.State);
            Assert.True(host.Lobby.Options.MapBytes > 400_000, $"map is {host.Lobby.Options.MapBytes} bytes");
            Assert.Equal(host.Lobby.Options.MapBytes, c.MapTransfer.Received);
            _out.WriteLine($"big map transfer: {host.Lobby.Options.MapBytes / 1024} KB in {sw.ElapsedMilliseconds} ms (join + download + verify + store)");
        }
        finally
        {
            Close(host, clients);
            Directory.Delete(hostDir, true);
            Directory.Delete(clientDir, true);
        }
    }

    [Fact]
    public void OldProtocolPeerIsRefusedWithBothVersions()
    {
        var host = HostSession.Start("Host", 0, lanAnnounce: false);
        string? reject = null;
        using var conn = new NetConnection(NetConnection.Connect("127.0.0.1", host.Port), (_, t, r) => { if (t == MsgType.Reject) reject = r.Str(); }, (_, _) => { });
        try
        {
            conn.Send(Msg.Build(MsgType.Hello, w =>
            {
                w.VarU(2);
                w.Str(GameInfo.CurrentSimBuild);
                w.Str("Old");
            }));
            WaitFor(() => reject != null, "reject");
            Assert.Contains($"host {Msg.ProtocolVersion}", reject);
            Assert.Contains("yours 2", reject);
        }
        finally { host.Leave(); }
    }
}

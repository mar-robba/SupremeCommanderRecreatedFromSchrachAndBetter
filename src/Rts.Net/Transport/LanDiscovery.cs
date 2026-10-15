using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Rts.Sim.Persistence;

namespace Rts.Net.Transport;

/// <summary>A game announced on the LAN (see <see cref="LanAnnouncer"/>).</summary>
public sealed record LanGame(ulong HostId, string Address, int Port, string Name, string HostName, int Players, int MaxPlayers,
    bool InGame, bool SameBuild, string Map)
{
    /// <summary>Stopwatch timestamp of the last announcement.</summary>
    public long SeenAt { get; init; }
    public string Endpoint => Address.Contains(':') ? $"[{Address}]:{Port}" : $"{Address}:{Port}";
}

/// <summary>
/// LAN discovery: the host broadcasts a small UDP datagram every second on <see cref="Port"/> (to the IPv4 broadcast
/// address and to loopback, so games hosted on the same machine show up too). Format: "RTSLAN1" + varu protocol +
/// u64 host id + varu tcp port + str name + str host name + varu players + varu max + u8 in game + str sim build + str map.
/// </summary>
public static class LanDiscovery
{
    public const int Port = 47625;
    private static readonly byte[] Magic = "RTSLAN1"u8.ToArray();

    internal static byte[] Encode(ulong hostId, int tcpPort, string name, string hostName, int players, int max, bool inGame, string map)
    {
        var w = new ByteWriter(128);
        w.Bytes(Magic);
        w.VarU(Wire.Msg.ProtocolVersion);
        w.Raw(hostId);
        w.VarU((ulong)tcpPort);
        w.Str(name);
        w.Str(hostName);
        w.VarU((ulong)players);
        w.VarU((ulong)max);
        w.U8(inGame ? (byte)1 : (byte)0);
        w.Str(GameInfo.CurrentSimBuild);
        w.Str(map);
        return w.ToArray();
    }

    internal static LanGame? Decode(byte[] data, int length, IPAddress from)
    {
        try
        {
            if (length < Magic.Length || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return null;
            var r = new ByteReader(data, Magic.Length, length - Magic.Length);
            if (r.VarU() != Wire.Msg.ProtocolVersion) return null;
            ulong id = r.Raw<ulong>();
            int port = r.Count(65535);
            string name = r.Str() ?? "";
            string host = r.Str() ?? "";
            int players = r.Count(64), max = r.Count(64);
            bool inGame = r.U8() != 0;
            string build = r.Str() ?? "";
            string map = r.Str() ?? "";
            if (from.IsIPv4MappedToIPv6) from = from.MapToIPv4();
            return new LanGame(id, from.ToString(), port, name, host, players, max, inGame, build == GameInfo.CurrentSimBuild, map)
            {
                SeenAt = Stopwatch.GetTimestamp(),
            };
        }
        catch (Exception e) when (e is SaveFormatException or ArgumentException) { return null; }
    }
}

/// <summary>Host side: announces the game every second until disposed. Network errors are ignored (no LAN = no listing).</summary>
public sealed class LanAnnouncer : IDisposable
{
    private readonly UdpClient? _udp;
    private readonly Timer? _timer;
    private readonly Func<byte[]> _payload;
    private readonly int _targetPort;

    /// <param name="payload">Called every second (timer thread) for the current datagram (see <see cref="LanDiscovery"/>).</param>
    public LanAnnouncer(Func<byte[]> payload, int targetPort = LanDiscovery.Port)
    {
        _payload = payload;
        _targetPort = targetPort;
        try
        {
            _udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            _timer = new Timer(_ => Announce(), null, 0, 1000);
        }
        catch (SocketException) { _udp = null; }
    }

    private void Announce()
    {
        var u = _udp;
        if (u == null) return;
        byte[] data;
        try { data = _payload(); }
        catch { return; }
        foreach (var target in new[] { IPAddress.Broadcast, IPAddress.Loopback })
        {
            try { u.Send(data, data.Length, new IPEndPoint(target, _targetPort)); }
            catch (Exception e) when (e is SocketException or ObjectDisposedException) { }
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _udp?.Dispose();
    }
}

/// <summary>Client side: listens for announcements; <see cref="Games"/> = the games heard in the last 4 seconds.</summary>
public sealed class LanBrowser : IDisposable
{
    private readonly UdpClient? _udp;
    private readonly Thread? _thread;
    private readonly Dictionary<ulong, LanGame> _games = new();
    private volatile bool _stopped;

    /// <summary>Why listening failed (null = listening).</summary>
    public string? Error { get; }

    public LanBrowser(int port = LanDiscovery.Port)
    {
        try
        {
            _udp = new UdpClient(AddressFamily.InterNetwork);
            // Several clients on one machine (and a host) share the port: broadcasts reach every bound socket.
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            _thread = new Thread(Loop) { Name = "LAN browser", IsBackground = true };
            _thread.Start();
        }
        catch (SocketException e)
        {
            Error = "LAN discovery unavailable: " + e.Message;
            _udp?.Dispose();
            _udp = null;
        }
    }

    private void Loop()
    {
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (!_stopped)
        {
            byte[] data;
            try { data = _udp!.Receive(ref any); }
            catch (Exception e) when (e is SocketException or ObjectDisposedException) { if (_stopped) return; continue; }
            var g = LanDiscovery.Decode(data, data.Length, any.Address);
            if (g == null) continue;
            lock (_games)
            {
                // The same host is heard via loopback and its LAN address: keep the first address, refresh the rest.
                if (_games.TryGetValue(g.HostId, out var old) && old.Address != g.Address && Stopwatch.GetElapsedTime(old.SeenAt).TotalSeconds < 3)
                    g = g with { Address = old.Address };
                _games[g.HostId] = g;
            }
        }
    }

    /// <summary>Games heard recently, sorted by name (copy; any thread).</summary>
    public List<LanGame> Games
    {
        get
        {
            lock (_games)
            {
                foreach (var id in _games.Where(kv => Stopwatch.GetElapsedTime(kv.Value.SeenAt).TotalSeconds > 4).Select(kv => kv.Key).ToList())
                    _games.Remove(id);
                return _games.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.HostId).ToList();
            }
        }
    }

    public void Dispose()
    {
        _stopped = true;
        _udp?.Dispose();
        _thread?.Join(500);
    }
}

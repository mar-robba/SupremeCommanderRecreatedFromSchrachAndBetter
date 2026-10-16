using System.Collections.Concurrent;
using Rts.Net.Lobby;
using Rts.Net.Lockstep;
using Rts.Net.Transport;
using Rts.Net.Wire;
using Rts.Sim.Persistence;

namespace Rts.Net.Session;

/// <summary>
/// A multiplayer session (lobby + game), host or client. The UI thread reads <see cref="Lobby"/>/<see cref="ChatLog"/>,
/// drains <see cref="TryGetEvent"/> every frame and calls the request methods; network threads update the state under
/// <see cref="Gate"/>. Topology: star (the host relays every turn), so only the host needs a reachable port.
/// </summary>
public abstract class NetSession : IDisposable
{
    public const int DefaultPort = 47624;
    public const int MaxChat = 200;
    /// <summary>A peer silent for this long is dropped (in game; the host pings every second).</summary>
    public double TimeoutSeconds = 10;

    protected readonly object Gate = new();
    private readonly ConcurrentQueue<NetEvent> _events = new();
    private readonly List<ChatLine> _chat = new();
    protected LobbyState LobbyRef = new();
    private volatile LobbyState _lobbyCopy = new();
    private volatile NetPhase _phase;

    public string LocalName { get; protected set; } = "";
    /// <summary>Our player id (host = 0).</summary>
    public int PlayerId { get; protected set; } = -1;
    public abstract bool IsHost { get; }
    public NetPhase Phase { get => _phase; protected set => _phase = value; }
    /// <summary>The current game (set when the host starts it).</summary>
    public NetGame? Game { get; protected set; }
    /// <summary>Why the session closed (null = open).</summary>
    public string? CloseReason { get; protected set; }

    internal abstract ITurnSink Sink { get; }

    /// <summary>Copy of the lobby (immutable for the reader: replaced on every change). Any thread.</summary>
    public LobbyState Lobby => _lobbyCopy;

    /// <summary>The local player's slot index (-1 = none).</summary>
    public int LocalSlot => Lobby.SlotOf(PlayerId);

    public List<ChatLine> ChatLog
    {
        get { lock (_chat) return new List<ChatLine>(_chat); }
    }

    public bool TryGetEvent(out NetEvent e) => _events.TryDequeue(out e!);

    protected void Raise(NetEvent e) => _events.Enqueue(e);

    /// <summary>Publishes <see cref="LobbyRef"/> to readers (call under Gate after a change).</summary>
    protected void PublishLobby()
    {
        _lobbyCopy = LobbyRef.Clone();
        Raise(new LobbyChangedEvent());
    }

    protected void AddChat(ChatLine line)
    {
        lock (_chat)
        {
            _chat.Add(line);
            if (_chat.Count > 200) _chat.RemoveRange(0, _chat.Count - 200);
        }
        Raise(new ChatEvent(line));
    }

    protected void SystemChat(string text) => AddChat(new ChatLine(-1, "", text));

    /// <summary>Bytes sent/received over TCP by this session so far (open connections).</summary>
    public abstract (long sent, long received) Traffic { get; }

    public abstract void SendChat(string text);
    internal abstract void ReportLoaded(NetGame game, ulong hash);
    internal abstract void RequestSpeed(NetGame game, float speed, bool paused);

    /// <summary>Leaves the session (graceful: the others are told). Safe to call twice.</summary>
    public abstract void Leave(string reason = "left");

    public void Dispose() => Leave();

    // ------------------------------------------------------------------ shared message encoding

    internal static byte[] TurnFrame(int turn, int tick, byte[] payload) => Msg.Build(MsgType.Turn, w =>
    {
        w.VarU((ulong)turn);
        w.VarU((ulong)tick);
        w.Block(payload);
    });

    internal static byte[] ChatFrame(ChatLine l) => Msg.Build(MsgType.Chat, w =>
    {
        w.VarI(l.PlayerId);
        w.Str(l.Name);
        w.Str(l.Text);
    });

    internal static byte[] SpeedFrame(float speed, bool paused, string by) => Msg.Build(MsgType.Speed, w =>
    {
        w.Raw(speed);
        w.U8(paused ? (byte)1 : (byte)0);
        w.Str(by);
    });

    internal static byte[] DropFrame(int turn, int lastTick, Sim.Orders.DropAction action, string reason) => Msg.Build(MsgType.Drop, w =>
    {
        w.VarU((ulong)turn);
        w.VarU((ulong)lastTick);
        w.U8((byte)action);
        w.Str(reason);
    });

    internal static string CleanText(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var chars = s.Where(c => !char.IsControl(c)).ToArray();
        var t = new string(chars).Trim();
        return t.Length <= max ? t : t[..max];
    }

    internal static float ReadSpeed(ByteReader r)
    {
        float s = r.Raw<float>();
        return float.IsFinite(s) ? Math.Clamp(s, 0.125f, 8f) : 1f;
    }
}

/// <summary>Join failed (refused by the host, no answer, different build...).</summary>
public sealed class NetJoinException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>"host", "host:port", "[v6]:port" → (host, port).</summary>
public static class NetAddress
{
    public static bool TryParse(string? text, out string host, out int port, int defaultPort = NetSession.DefaultPort)
    {
        host = "";
        port = defaultPort;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.StartsWith('['))
        {
            int end = text.IndexOf(']');
            if (end < 0) return false;
            host = text[1..end];
            string rest = text[(end + 1)..];
            if (rest.Length == 0) return host.Length > 0;
            return rest.StartsWith(':') && int.TryParse(rest[1..], out port) && port is > 0 and < 65536 && host.Length > 0;
        }
        int colon = text.LastIndexOf(':');
        if (colon >= 0 && text.IndexOf(':') == colon)
        {
            host = text[..colon];
            return host.Length > 0 && int.TryParse(text[(colon + 1)..], out port) && port is > 0 and < 65536;
        }
        host = text; // a bare IPv6 address or a host name without port
        return true;
    }
}

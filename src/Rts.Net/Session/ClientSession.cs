using Rts.Net.Lobby;
using Rts.Net.Lockstep;
using Rts.Net.Transport;
using Rts.Net.Wire;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;

namespace Rts.Net.Session;

/// <summary>
/// A joined player: mirrors the host's lobby, sends its requests/turns/hashes to the host and applies what the host
/// relays. If the host disappears during a game, the game continues offline (the other players are dropped locally).
/// </summary>
public sealed partial class ClientSession : NetSession, ITurnSink
{
    private readonly NetConnection _conn;
    private readonly ManualResetEventSlim _welcome = new();
    private readonly Timer _monitor;
    private string? _refused;
    private int _monitorTicks;

    public override bool IsHost => false;
    internal override ITurnSink Sink => this;
    /// <summary>Round-trip time to the host in ms (-1 unknown).</summary>
    public float PingMs => _conn.RttMs;
    public string HostEndpoint { get; }

    private ClientSession(System.Net.Sockets.Socket socket, string name, string endpoint, string? mapDir = null, bool observe = false)
    {
        if (mapDir != null) MapDir = mapDir; // (8b-3) before the first lobby arrives
        LocalName = CleanText(name, 24) is { Length: > 0 } n ? n : "Player";
        HostEndpoint = endpoint;
        _conn = new NetConnection(socket, OnMessage, OnClosed, "Net client");
        _conn.Send(Msg.Build(MsgType.Hello, w =>
        {
            w.VarU(Msg.ProtocolVersion);
            w.Str(GameInfo.CurrentSimBuild);
            w.Str(LocalName);
            w.U8(observe ? (byte)1 : (byte)0); // (10a-2) join as an observer
        }));
        _monitor = new Timer(_ => Monitor(), null, 250, 250);
    }

    /// <summary>Connects and joins (blocks until the host answers). Throws <see cref="NetJoinException"/> with the reason.</summary>
    /// <param name="mapDir">(8b-3) Folder for received maps (null = <see cref="Rts.Sim.Map.MapLibrary.DownloadDir"/>).</param>
    /// <param name="observe">(10a-2) Join as an observer (also when the game has no open slot left).</param>
    public static ClientSession Join(string host, int port, string name, int timeoutMs = 5000, string? mapDir = null, bool observe = false)
    {
        System.Net.Sockets.Socket s;
        try { s = NetConnection.Connect(host, port, timeoutMs); }
        catch (Exception e) { throw new NetJoinException($"Can't reach {host}:{port}: {e.Message}", e); }
        var c = new ClientSession(s, name, $"{host}:{port}", mapDir, observe);
        if (!c._welcome.Wait(timeoutMs))
        {
            c.Leave("no answer");
            throw new NetJoinException($"{host}:{port} did not answer (is it an RTS game?)");
        }
        if (c.PlayerId < 0)
        {
            string why = c._refused ?? c.CloseReason ?? "connection closed";
            c.Leave(why);
            throw new NetJoinException("Join refused: " + why);
        }
        return c;
    }

    /// <summary>Asks the host to change the local slot (move, faction, team, color, ready).</summary>
    public void Request(LobbyRequest q)
    {
        var w = new ByteWriter(64);
        q.Write(w);
        _conn.Send(Frame.Make(MsgType.LobbyRequest, w.Span));
    }

    public void SetReady(bool ready) => Request(new LobbyRequest { Ready = ready ? 1 : 0 });

    public override void Observe(bool on, int slot = -1) => Request(new LobbyRequest { Observe = on ? 1 : 0, MoveToSlot = slot });

    internal override void VoteDrop(NetGame game, int turn, bool yes)
    {
        if (game == Game && !game.Offline) _conn.Send(Msg.Build(MsgType.DropVote, w => { w.VarU((ulong)turn); w.U8(yes ? (byte)1 : (byte)0); }));
    }

    void ITurnSink.SendResign(int lastTick) => _conn.Send(Msg.Build(MsgType.Resign, w => w.VarU((ulong)lastTick)));

    public override (long sent, long received) Traffic => (_conn.BytesSent, _conn.BytesReceived);

    public override void SendChat(string text, bool alliesOnly = false)
    {
        text = CleanText(text, MaxChat);
        if (text.Length > 0) _conn.Send(Msg.Build(MsgType.Chat, w =>
        {
            w.Str(text);
            w.U8(alliesOnly ? (byte)1 : (byte)0); // appended (6b)
        }));
    }

    internal override void ReportLoaded(NetGame game, ulong hash)
    {
        if (game == Game) _conn.Send(Msg.Build(MsgType.Loaded, w => w.Raw(hash)));
    }

    internal override void RequestSpeed(NetGame game, float speed, bool paused)
    {
        if (game != Game) return;
        if (game.Offline) game.SetSpeed(speed, paused); // alone now: decide locally
        else _conn.Send(SpeedFrame(speed, paused, LocalName));
    }

    void ITurnSink.SendTurn(int turnPlayer, int tick, byte[] payload) => _conn.Send(TurnFrame(turnPlayer, tick, payload));

    void ITurnSink.SendHash(int tick, ulong hash) => _conn.Send(Msg.Build(MsgType.Hash, w =>
    {
        w.VarU((ulong)tick);
        w.Raw(hash);
    }));

    // ------------------------------------------------------------------ messages from the host (receive thread)

    private void OnMessage(NetConnection c, MsgType type, ByteReader r)
    {
        lock (Gate)
        {
            if (Phase == NetPhase.Closed) return;
            switch (type)
            {
                case MsgType.Welcome:
                    PlayerId = r.Count(int.MaxValue);
                    _welcome.Set();
                    break;
                case MsgType.Reject:
                    _refused = r.Str() ?? "refused";
                    _welcome.Set();
                    break;
                case MsgType.Lobby:
                    LobbyRef = LobbyState.Read(r);
                    PublishLobby();
                    CheckMap(); // (8b-3) ClientSession.Maps.cs
                    break;
                case MsgType.MapChunk:
                    OnMapChunk(r);
                    break;
                case MsgType.Chat:
                {
                    int pid = (int)r.VarI();
                    string name = CleanText(r.Str(), 24), text = CleanText(r.Str(), MaxChat);
                    AddChat(new ChatLine(pid, name, text, ReadAlliesFlag(r)));
                    break;
                }
                case MsgType.Start:
                {
                    var start = GameStart.Read(r);
                    Game = new NetGame(this, start, start.TurnIndexOf(PlayerId));
                    Phase = NetPhase.Loading;
                    Raise(new GameStartingEvent(Game));
                    break;
                }
                case MsgType.Abort:
                {
                    string reason = r.Str() ?? "aborted";
                    Game = null;
                    Phase = NetPhase.Lobby;
                    Raise(new StartAbortedEvent(reason));
                    break;
                }
                case MsgType.Go:
                    if (Game == null) throw new ProtocolException("Go without a game");
                    Phase = NetPhase.Playing;
                    Game.OnGo();
                    Raise(new GameGoEvent());
                    break;
                case MsgType.Turn:
                {
                    int turn = r.Count(64), tick = r.Count(int.MaxValue);
                    var payload = r.Block();
                    if (Game == null) throw new ProtocolException("turn without a game");
                    if (turn == Game.LocalTurn) throw new ProtocolException("own turn relayed back");
                    Game.AddTurn(turn, tick, payload);
                    break;
                }
                case MsgType.Drop:
                {
                    int turn = r.Count(64), last = r.Count(int.MaxValue);
                    var action = (DropAction)Math.Min(r.U8(), (byte)DropAction.Defeat);
                    string reason = r.Str() ?? "";
                    if (Game == null || (uint)turn >= (uint)Game.TurnPlayers || turn == Game.LocalTurn) break; // (10a-2) own resign: applied locally
                    Game.ApplyDrop(turn, last, action);
                    Raise(new PlayerDroppedEvent(Game.NameOfTurn(turn), Game.Start.TurnArmies[turn], action, reason));
                    break;
                }
                case MsgType.Desync:
                {
                    int tick = r.Count(int.MaxValue);
                    string detail = r.Str() ?? "";
                    if (Game != null && Game.MarkDesync(tick, detail)) Raise(new DesyncEvent(tick, detail));
                    break;
                }
                case MsgType.Speed:
                {
                    float speed = ReadSpeed(r);
                    bool paused = r.U8() != 0;
                    string by = CleanText(r.Str(), 24);
                    if (Game == null) break;
                    Game.SetSpeed(speed, paused);
                    Raise(new SpeedChangedEvent(speed, paused, by));
                    break;
                }
                case MsgType.Delay:
                    Game?.SetDelay(r.Count(LockstepHooks.MaxDelay));
                    break;
                case MsgType.Pings:
                {
                    int n = r.Count(64);
                    var pings = new int[n];
                    for (int i = 0; i < n; i++) pings[i] = (int)r.VarI();
                    Game?.SetPings(pings);
                    break;
                }
                case MsgType.Leave:
                    c.Close(r.Str() ?? "The host closed the game");
                    break;
                case MsgType.DropVote: // (10a-2)
                    Game?.SetVotes(ReadTally(r));
                    break;
                case MsgType.LobbyPings:
                    SetPersonPings(ReadPings(r));
                    break;
                default:
                    throw new ProtocolException($"unexpected {type}");
            }
        }
    }

    private void OnClosed(NetConnection c, string reason)
    {
        _welcome.Set();
        lock (Gate)
        {
            if (Phase == NetPhase.Closed) return;
            var g = Game;
            string why = _refused ?? reason;
            if (g != null && Phase == NetPhase.Playing && g.Hooks != null)
            {
                // Star topology: without the host nobody relays turns. Continue alone: drop every other player
                // after the last turn we got from it (deterministic for this peer; the replay records it).
                g.Offline = true;
                var action = g.Start.Lobby.Options.OnDrop;
                for (int t = 0; t < g.TurnPlayers; t++)
                {
                    if (t == g.LocalTurn || g.IsDropped(t)) continue;
                    g.ApplyDrop(t, g.ReceivedOf(t), action);
                    Raise(new PlayerDroppedEvent(g.NameOfTurn(t), g.Start.TurnArmies[t], action, "host connection lost"));
                }
                SystemChat("Connection to the host lost: the game continues offline");
            }
            Phase = NetPhase.Closed;
            CloseReason = why;
            Raise(new DisconnectedEvent(why));
        }
    }

    private void Monitor()
    {
        if (Phase == NetPhase.Closed) return;
        if (++_monitorTicks % 4 == 0) _conn.SendPing();
        double limit = Phase == NetPhase.Lobby ? Math.Max(TimeoutSeconds, 20) : TimeoutSeconds;
        if (_conn.SilentSeconds > limit) _conn.Close("the host timed out");
    }

    public override void Leave(string reason = "left")
    {
        bool open;
        lock (Gate)
        {
            open = Phase != NetPhase.Closed;
            if (open)
            {
                Phase = NetPhase.Closed;
                CloseReason = reason;
            }
        }
        _monitor.Dispose();
        if (open) _conn.Close(reason, Msg.Text(MsgType.Leave, reason));
        _conn.Dispose();
    }
}

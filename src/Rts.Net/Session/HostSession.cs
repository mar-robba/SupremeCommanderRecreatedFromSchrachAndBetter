using System.Net.Sockets;
using Rts.Net.Lobby;
using Rts.Net.Lockstep;
using Rts.Net.Transport;
using Rts.Net.Wire;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.World;

namespace Rts.Net.Session;

/// <summary>
/// The host: owns the lobby (authoritative copy, broadcast after every change), relays every turn, decides drops,
/// speed/pause and the input delay, and compares the state hashes. Also a player (id 0) with its own lockstep.
/// </summary>
public sealed class HostSession : NetSession, ITurnSink
{
    private readonly NetListener _listener;
    private readonly LanAnnouncer? _lan;
    private readonly Timer _monitor;
    private readonly List<NetConnection> _conns = new();
    private readonly ulong _hostId;
    private int _nextPlayerId = 1;
    private int _monitorTicks;
    private readonly Dictionary<int, ulong> _loaded = new();
    private readonly Dictionary<int, ulong> _hostHashes = new();
    private readonly Dictionary<int, List<(int turn, ulong hash)>> _peerHashes = new();
    /// <summary>Game name shown in LAN listings.</summary>
    public string GameName;

    public override bool IsHost => true;
    public int Port => _listener.Port;
    internal override ITurnSink Sink => this;

    private HostSession(string name, int port, bool lan)
    {
        LocalName = CleanText(name, 24) is { Length: > 0 } n ? n : "Host";
        PlayerId = 0;
        GameName = LocalName + "'s game";
        LobbyRef = LobbyState.CreateDefault(LocalName);
        _hostId = (ulong)Random.Shared.NextInt64();
        _listener = new NetListener(port, OnAccept);
        PublishLobby();
        if (lan) _lan = new LanAnnouncer(Announcement);
        _monitor = new Timer(_ => Monitor(), null, 250, 250);
    }

    /// <summary>Opens a game on <paramref name="port"/> (0 = any free port). Throws SocketException if the port is taken.</summary>
    public static HostSession Start(string name, int port = DefaultPort, bool lanAnnounce = true) => new(name, port, lanAnnounce);

    private byte[] Announcement()
    {
        var l = Lobby;
        return LanDiscovery.Encode(_hostId, Port, GameName, LocalName, l.HumanCount, l.Slots.Count(s => s.Kind != NetSlotKind.Closed),
            Phase != NetPhase.Lobby, $"{l.Options.MapSize} {l.Options.Preset}");
    }

    // ------------------------------------------------------------------ lobby API (UI thread)

    /// <summary>Sets a slot to Open/AI/Closed (a human there is kicked). The host's own slot can't be changed.</summary>
    public void SetSlotKind(int slot, NetSlotKind kind)
    {
        if (kind == NetSlotKind.Human) return;
        NetConnection? kick = null;
        lock (Gate)
        {
            if (Phase != NetPhase.Lobby || (uint)slot >= (uint)LobbyRef.Slots.Count) return;
            var s = LobbyRef.Slots[slot];
            if (s.Kind == NetSlotKind.Human)
            {
                if (s.PlayerId == 0) return;
                kick = _conns.FirstOrDefault(c => c.PlayerId == s.PlayerId);
            }
            s.Kind = kind;
            s.PlayerId = -1;
            s.Name = kind == NetSlotKind.AI ? "AI" : "";
            s.Ready = kind == NetSlotKind.AI;
            if (kind == NetSlotKind.AI && ColorTaken(s.Color, slot)) s.Color = FreeColor(slot);
            Changed();
        }
        kick?.Close("kicked", Msg.Text(MsgType.Reject, "You were removed from the game by the host"));
    }

    /// <summary>Changes a slot's settings (the host's own slot or an AI slot; -1/null = unchanged).</summary>
    public void SetSlot(int slot, int team = -1, int color = -1, string? faction = null, AiDifficulty? difficulty = null)
    {
        lock (Gate)
        {
            if (Phase != NetPhase.Lobby || (uint)slot >= (uint)LobbyRef.Slots.Count) return;
            var s = LobbyRef.Slots[slot];
            if (!s.IsArmy || (s.Kind == NetSlotKind.Human && s.PlayerId != 0)) return;
            if (team >= 0) s.Team = Math.Min(team, 4);
            if (color >= 0 && !ColorTaken(color % 8, slot)) s.Color = color % 8;
            if (faction != null) s.Faction = NetSlot.Clip(faction, 32);
            if (difficulty is AiDifficulty d && s.Kind == NetSlotKind.AI) s.Difficulty = d;
            Changed();
        }
    }

    /// <summary>Moves the host to another open slot.</summary>
    public void MoveHost(int slot) => ApplyRequest(0, new LobbyRequest { MoveToSlot = slot });

    /// <summary>Changes the game options (everybody's ready flag is reset).</summary>
    public void SetOptions(LobbyOptions o)
    {
        lock (Gate)
        {
            if (Phase != NetPhase.Lobby) return;
            LobbyRef.Options = o.Clone();
            LobbyRef.Options.MapSize = Math.Clamp(o.MapSize, 128, 4096);
            foreach (var s in LobbyRef.Slots)
                if (s.Kind == NetSlotKind.Human && s.PlayerId != 0) s.Ready = false;
            Changed();
        }
    }

    /// <summary>Optional faction check for <see cref="StartGame"/> (the host's <c>FactionDb</c>): unknown ids are refused.
    /// "" (default) and "random" always pass. The peers' data must match too: the Loaded hashes catch that.</summary>
    public Func<string, bool>? FactionKnown;

    /// <summary>Starts the game; returns why it can't (null = started: a <see cref="GameStartingEvent"/> follows).</summary>
    public string? StartGame()
    {
        lock (Gate)
        {
            if (Phase != NetPhase.Lobby) return "Already started";
            if (LobbyRef.Problem() is string p) return p;
            if (FactionKnown != null)
                foreach (var s in LobbyRef.Slots)
                    if (s.IsArmy && !string.IsNullOrWhiteSpace(s.Faction) && !string.Equals(s.Faction, Rts.Sim.Data.FactionDb.Random, StringComparison.OrdinalIgnoreCase)
                        && !FactionKnown(s.Faction))
                        return $"Unknown faction '{s.Faction}' ({(s.Kind == NetSlotKind.AI ? "AI" : s.Name)})";
            int delay = DelayFor(_conns.Where(c => c.PlayerId > 0).Select(c => c.RttMs).DefaultIfEmpty(0).Max(), 1f);
            var start = GameStart.From(LobbyRef, delay);
            Game = new NetGame(this, start, start.TurnIndexOf(0));
            _loaded.Clear();
            _hostHashes.Clear();
            _peerHashes.Clear();
            Phase = NetPhase.Loading;
            Broadcast(start.ToFrame());
            Raise(new GameStartingEvent(Game));
            return null;
        }
    }

    public override (long sent, long received) Traffic
    {
        get { lock (Gate) return (_conns.Sum(c => c.BytesSent), _conns.Sum(c => c.BytesReceived)); }
    }

    public override void SendChat(string text)
    {
        text = CleanText(text, MaxChat);
        if (text.Length == 0) return;
        lock (Gate) PostChat(new ChatLine(0, LocalName, text));
    }

    // ------------------------------------------------------------------ connections

    private void OnAccept(Socket s)
    {
        lock (Gate)
        {
            if (Phase == NetPhase.Closed) { s.Dispose(); return; }
            var c = new NetConnection(s, OnMessage, OnClosed, "Net host");
            _conns.Add(c);
        }
    }

    private void OnMessage(NetConnection c, MsgType type, ByteReader r)
    {
        lock (Gate)
        {
            if (c.IsClosed) return;
            if (c.PlayerId < 0)
            {
                if (type != MsgType.Hello) throw new ProtocolException("expected Hello");
                HandleHello(c, r);
                return;
            }
            switch (type)
            {
                case MsgType.LobbyRequest:
                    if (Phase == NetPhase.Lobby) ApplyRequest(c.PlayerId, LobbyRequest.Read(r));
                    break;
                case MsgType.Chat:
                {
                    string text = CleanText(r.Str(), MaxChat);
                    if (text.Length > 0) PostChat(new ChatLine(c.PlayerId, c.Name, text));
                    break;
                }
                case MsgType.Loaded:
                    if (Phase == NetPhase.Loading) OnLoaded(c.PlayerId, r.Raw<ulong>());
                    break;
                case MsgType.Turn:
                    HandleTurn(c, r);
                    break;
                case MsgType.Hash:
                    if (Game != null) OnPeerHash(Game.Start.TurnIndexOf(c.PlayerId), r.Count(int.MaxValue), r.Raw<ulong>());
                    break;
                case MsgType.Speed:
                    if (Game != null) ApplySpeed(ReadSpeed(r), r.U8() != 0, c.Name);
                    break;
                case MsgType.Leave:
                    c.Close("left the game");
                    break;
                default:
                    throw new ProtocolException($"unexpected {type}");
            }
        }
    }

    private void HandleHello(NetConnection c, ByteReader r)
    {
        uint protocol = (uint)r.VarU();
        string build = r.Str() ?? "";
        string name = CleanText(r.Str(), 24);
        string? refuse = protocol != Msg.ProtocolVersion ? "Different network protocol version"
            : build != GameInfo.CurrentSimBuild ? "Different game build: both players need the same version"
            : Phase != NetPhase.Lobby ? "The game has already started"
            : LobbyRef.FirstOpenSlot() < 0 ? "The game is full" : null;
        if (refuse != null)
        {
            c.PlayerId = int.MaxValue; // not a player: ignore anything else it sends
            c.Close("refused: " + refuse, Msg.Text(MsgType.Reject, refuse));
            return;
        }
        if (name.Length == 0) name = "Player";
        string unique = name;
        for (int k = 2; LobbyRef.Slots.Any(s => s.Kind == NetSlotKind.Human && s.Name == unique); k++) unique = $"{name} {k}";
        c.Name = unique;
        c.PlayerId = _nextPlayerId++;
        int slot = LobbyRef.FirstOpenSlot();
        var sl = LobbyRef.Slots[slot];
        sl.Kind = NetSlotKind.Human;
        sl.PlayerId = c.PlayerId;
        sl.Name = unique;
        sl.Ready = false;
        if (ColorTaken(sl.Color, slot)) sl.Color = FreeColor(slot);
        c.Send(Msg.Build(MsgType.Welcome, w => w.VarU((ulong)c.PlayerId)));
        foreach (var line in ChatLog.TakeLast(30)) c.Send(ChatFrame(line));
        Changed();
        SystemChatAll($"{unique} joined");
    }

    private void ApplyRequest(int playerId, LobbyRequest q)
    {
        lock (Gate)
        {
            int i = LobbyRef.SlotOf(playerId);
            if (i < 0 || Phase != NetPhase.Lobby) return;
            var s = LobbyRef.Slots[i];
            if (q.MoveToSlot >= 0 && q.MoveToSlot < LobbyRef.Slots.Count && LobbyRef.Slots[q.MoveToSlot].Kind == NetSlotKind.Open)
            {
                var dst = LobbyRef.Slots[q.MoveToSlot];
                dst.Kind = NetSlotKind.Human;
                dst.PlayerId = s.PlayerId;
                dst.Name = s.Name;
                dst.Faction = s.Faction;
                dst.Team = s.Team;
                dst.Ready = s.Ready;
                if (ColorTaken(s.Color, q.MoveToSlot, ignoreSlot: i)) dst.Color = FreeColor(q.MoveToSlot);
                else dst.Color = s.Color;
                s.Kind = NetSlotKind.Open;
                s.PlayerId = -1;
                s.Name = "";
                s.Ready = false;
                s = dst;
                i = q.MoveToSlot;
            }
            if (q.Faction != null) s.Faction = NetSlot.Clip(q.Faction, 32);
            if (q.Team >= 0) s.Team = Math.Min(q.Team, 4);
            if (q.Color >= 0 && !ColorTaken(q.Color % 8, i)) s.Color = q.Color % 8;
            if (q.Ready >= 0) s.Ready = q.Ready != 0 || playerId == 0;
            Changed();
        }
    }

    private void OnClosed(NetConnection c, string reason)
    {
        lock (Gate)
        {
            _conns.Remove(c);
            if (c.PlayerId <= 0 || c.PlayerId == int.MaxValue || Phase == NetPhase.Closed) return;
            if (Phase == NetPhase.Lobby)
            {
                int i = LobbyRef.SlotOf(c.PlayerId);
                if (i >= 0)
                {
                    var s = LobbyRef.Slots[i];
                    s.Kind = NetSlotKind.Open;
                    s.PlayerId = -1;
                    s.Name = "";
                    s.Ready = false;
                    Changed();
                }
                SystemChatAll($"{c.Name} left ({reason})");
                return;
            }
            DropPlayer(c.PlayerId, c.Name, reason);
        }
    }

    /// <summary>Drops a player from the running/loading game (under Gate): last tick = the last turn relayed.</summary>
    private void DropPlayer(int playerId, string name, string reason)
    {
        var g = Game;
        if (g == null) return;
        int turn = g.Start.TurnIndexOf(playerId);
        if (turn < 0 || g.IsDropped(turn)) return;
        int last = g.ReceivedOf(turn);
        var action = g.Start.Lobby.Options.OnDrop;
        g.ApplyDrop(turn, last, action);
        Broadcast(DropFrame(turn, last, action, reason));
        Raise(new PlayerDroppedEvent(name, g.Start.TurnArmies[turn], action, reason));
        SystemChatAll($"{name} left the game ({reason}): {(action == DropAction.AiTakeover ? "the AI takes over" : "army defeated")}");
        if (Phase == NetPhase.Loading) CheckAllLoaded();
    }

    // ------------------------------------------------------------------ loading / lockstep

    internal override void ReportLoaded(NetGame game, ulong hash)
    {
        lock (Gate)
            if (game == Game && Phase == NetPhase.Loading) OnLoaded(0, hash);
    }

    private void OnLoaded(int playerId, ulong hash)
    {
        _loaded[playerId] = hash;
        CheckAllLoaded();
    }

    private void CheckAllLoaded()
    {
        var g = Game!;
        var waiting = g.Start.TurnPlayers.Where((pid, t) => !g.IsDropped(t) && !_loaded.ContainsKey(pid)).ToList();
        if (waiting.Count > 0 || !_loaded.ContainsKey(0)) return;
        ulong mine = _loaded[0];
        foreach (var (pid, h) in _loaded)
        {
            if (h == mine) continue;
            string who = g.NameOfTurn(g.Start.TurnIndexOf(pid));
            string reason = $"{who} built a different starting state (different game data or floating-point behaviour)";
            Broadcast(Msg.Text(MsgType.Abort, reason));
            Game = null;
            Phase = NetPhase.Lobby;
            foreach (var s in LobbyRef.Slots)
                if (s.Kind == NetSlotKind.Human && s.PlayerId != 0) s.Ready = false;
            Changed();
            Raise(new StartAbortedEvent(reason));
            return;
        }
        Phase = NetPhase.Playing;
        Broadcast(Msg.Build(MsgType.Go));
        g.OnGo();
        Raise(new GameGoEvent());
    }

    private void HandleTurn(NetConnection c, ByteReader r)
    {
        var g = Game;
        int turn = r.Count(64), tick = r.Count(int.MaxValue);
        byte[] payload = r.Block();
        if (g == null || Phase != NetPhase.Playing) throw new ProtocolException("turn outside the game");
        if (turn != g.Start.TurnIndexOf(c.PlayerId)) throw new ProtocolException("turn for another player");
        if (g.IsDropped(turn)) return;
        g.AddTurn(turn, tick, payload);
        var frame = TurnFrame(turn, tick, payload);
        foreach (var o in _conns)
            if (o != c && o.PlayerId > 0) o.Send(frame);
    }

    void ITurnSink.SendTurn(int turnPlayer, int tick, byte[] payload)
    {
        var frame = TurnFrame(turnPlayer, tick, payload);
        lock (Gate) Broadcast(frame);
    }

    void ITurnSink.SendHash(int tick, ulong hash)
    {
        lock (Gate)
        {
            _hostHashes[tick] = hash;
            if (_peerHashes.Remove(tick, out var list))
                foreach (var (turn, h) in list) Compare(turn, tick, h, hash);
            // Keep ~2 minutes of history (late peers report old ticks).
            if (_hostHashes.Count > 1200) _hostHashes.Remove(_hostHashes.Keys.Min());
        }
    }

    private void OnPeerHash(int turn, int tick, ulong hash)
    {
        if (turn < 0) return;
        if (_hostHashes.TryGetValue(tick, out var mine)) Compare(turn, tick, hash, mine);
        else
        {
            if (!_peerHashes.TryGetValue(tick, out var list)) _peerHashes[tick] = list = new();
            list.Add((turn, hash));
        }
    }

    private void Compare(int turn, int tick, ulong theirs, ulong mine)
    {
        if (theirs == mine) return;
        var g = Game!;
        string detail = $"{g.NameOfTurn(turn)} differs from the host at tick {tick}";
        if (!g.MarkDesync(tick, detail)) return;
        Broadcast(Msg.Build(MsgType.Desync, w => { w.VarU((ulong)tick); w.Str(detail); }));
        Raise(new DesyncEvent(tick, detail));
        SystemChatAll($"DESYNC at tick {tick}: {detail}. The replays were saved.");
    }

    internal override void RequestSpeed(NetGame game, float speed, bool paused)
    {
        lock (Gate)
            if (game == Game) ApplySpeed(speed, paused, LocalName);
    }

    private void ApplySpeed(float speed, bool paused, string by)
    {
        var g = Game!;
        speed = g.AllowedSpeed(speed); // locked game speed (lobby option): only pause/unpause go through
        if (g.Speed == speed && g.Paused == paused) return;
        g.SetSpeed(speed, paused);
        Broadcast(SpeedFrame(speed, paused, by));
        Raise(new SpeedChangedEvent(speed, paused, by));
    }

    /// <summary>Input delay in ticks for a round-trip time: a turn must reach every peer (via the host) before its tick.</summary>
    public static int DelayFor(float maxRttMs, float speed)
    {
        float tickMs = 1000f / (SimWorld.TicksPerSecond * Math.Max(0.125f, speed));
        return Math.Clamp(1 + (int)((Math.Max(0, maxRttMs) + 20f) / tickMs), 1, LockstepHooks.MaxDelay);
    }

    /// <summary>Every 250 ms (timer thread): pings, timeouts, adaptive delay, ping list.</summary>
    private void Monitor()
    {
        List<NetConnection> conns;
        lock (Gate)
        {
            if (Phase == NetPhase.Closed) return;
            conns = _conns.ToList();
        }
        bool second = ++_monitorTicks % 4 == 0;
        foreach (var c in conns)
        {
            if (second) c.SendPing();
            double limit = c.PlayerId < 0 ? 10 : Phase == NetPhase.Lobby ? Math.Max(TimeoutSeconds, 20) : TimeoutSeconds;
            if (c.SilentSeconds > limit) c.Close("timed out");
        }
        if (!second) return;
        lock (Gate)
        {
            var g = Game;
            if (g == null || Phase != NetPhase.Playing) return;
            var pings = new int[g.TurnPlayers];
            float max = 0;
            for (int t = 0; t < pings.Length; t++)
            {
                int pid = g.Start.TurnPlayers[t];
                var c = _conns.FirstOrDefault(x => x.PlayerId == pid);
                pings[t] = pid == 0 ? 0 : c == null ? -1 : (int)Math.Round(c.RttMs);
                if (c != null) max = Math.Max(max, c.RttMs);
            }
            g.SetPings(pings);
            Broadcast(Msg.Build(MsgType.Pings, w =>
            {
                w.VarU((ulong)pings.Length);
                foreach (int p in pings) w.VarI(p);
            }));
            int d = DelayFor(max, g.Speed);
            if (g.Hooks is { } h && h.Delay != d)
            {
                g.SetDelay(d);
                Broadcast(Msg.Build(MsgType.Delay, w => w.VarU((ulong)d)));
            }
        }
    }

    // ------------------------------------------------------------------ helpers (under Gate)

    private void Changed()
    {
        LobbyRef.Version++;
        PublishLobby();
        var w = new ByteWriter(512);
        LobbyRef.Write(w);
        Broadcast(Frame.Make(MsgType.Lobby, w.Span));
    }

    private void Broadcast(byte[] frame)
    {
        foreach (var c in _conns)
            if (c.PlayerId > 0 && c.PlayerId != int.MaxValue) c.Send(frame);
    }

    private void PostChat(ChatLine line)
    {
        AddChat(line);
        Broadcast(ChatFrame(line));
    }

    private void SystemChatAll(string text) => PostChat(new ChatLine(-1, "", text));

    private bool ColorTaken(int color, int slot, int ignoreSlot = -1)
    {
        for (int i = 0; i < LobbyRef.Slots.Count; i++)
            if (i != slot && i != ignoreSlot && LobbyRef.Slots[i].IsArmy && LobbyRef.Slots[i].Color == color) return true;
        return false;
    }

    private int FreeColor(int slot)
    {
        for (int c = 0; c < 8; c++)
            if (!ColorTaken(c, slot)) return c;
        return slot % 8;
    }

    /// <summary>Kicks the player in a slot (lobby only).</summary>
    public void Kick(int slot) => SetSlotKind(slot, NetSlotKind.Open);

    /// <summary>Test hook: drops a player as if it timed out (closes its connection).</summary>
    public void Disconnect(int playerId, string reason = "disconnected")
    {
        NetConnection? c;
        lock (Gate) c = _conns.FirstOrDefault(x => x.PlayerId == playerId);
        c?.Close(reason);
    }

    public override void Leave(string reason = "left")
    {
        List<NetConnection> conns;
        lock (Gate)
        {
            if (Phase == NetPhase.Closed) return;
            Phase = NetPhase.Closed;
            CloseReason = reason;
            conns = _conns.ToList();
            _conns.Clear();
        }
        _monitor.Dispose();
        _lan?.Dispose();
        _listener.Dispose();
        var bye = Msg.Text(MsgType.Leave, "The host closed the game");
        foreach (var c in conns) c.Close("host left", bye);
        foreach (var c in conns) c.Dispose();
    }
}

using System.Diagnostics;
using Rts.Net.Lobby;
using Rts.Net.Transport;
using Rts.Net.Wire;
using Rts.Sim.Map;
using Rts.Sim.Persistence;

namespace Rts.Net.Session;

/// <summary>
/// (10a-3) The host's side of multiplayer save/load. Saving: <see cref="OrderSave"/> (own request, a client's
/// <see cref="MsgType.SaveRequest"/> or the autosave) schedules the save one tick past every turn the host holds and
/// broadcasts the order, collects every peer's <see cref="MsgType.SaveDone"/> (same state hash = the same save) and
/// broadcasts the result. Loading: <see cref="OpenLoadLobby"/> turns the lobby into the save's lobby (saved slots,
/// claimed by name), clients without the file get it in chunks like a map file (<see cref="MsgType.SaveChunk"/>).
/// Everything private runs under <c>Gate</c>.
/// </summary>
public sealed partial class HostSession
{
    private sealed class SaveCollect(NetSaveOrder order, HashSet<int> expected)
    {
        public readonly NetSaveOrder Order = order;
        public readonly HashSet<int> Expected = expected;
        public readonly Dictionary<int, (bool ok, ulong hash, string error)> Results = new();
        public readonly long Started = Stopwatch.GetTimestamp();
    }

    private readonly Dictionary<string, SaveCollect> _saves = new();
    private int _saveCounter;
    private int _nextAutosaveTick = -1, _autosaveSlot;

    // load lobby
    private byte[]? _saveBytes;
    private ulong _saveHash;
    private readonly Dictionary<int, MapUpload> _saveUploads = new();
    private readonly Dictionary<int, (bool ready, int progress)> _saveState = new();

    /// <summary>Seconds the host waits for every peer's SaveDone before it reports the missing ones.</summary>
    public double SaveTimeoutSeconds = 30;

    /// <summary>Multiplayer autosave (the host's Autosave setting): every <see cref="AutosaveIntervalTicks"/> of game
    /// time, rolling <c>mp_autosave_1..3</c>.</summary>
    public volatile bool Autosave;
    public int AutosaveIntervalTicks = 3000;
    public const string AutosavePrefix = "mp_autosave_";

    /// <summary>Test hook: changes the uploaded copy of the save (e.g. corrupts it) to test the client's check.</summary>
    internal Action<byte[]>? TamperSaveUpload;

    /// <summary>True while the lobby is the load lobby of a multiplayer save.</summary>
    public bool IsLoadLobby
    {
        get { lock (Gate) return LobbyRef.IsLoad; }
    }

    // ------------------------------------------------------------------ saving

    /// <summary>Orders a save of the running game for every peer; returns why not (null = ordered: a
    /// <see cref="SaveOrderedEvent"/> and later a <see cref="SaveResultEvent"/> follow).</summary>
    public string? OrderSave(string name, bool auto = false, string? by = null)
    {
        lock (Gate)
        {
            var g = Game;
            if (g == null || Phase != NetPhase.Playing || g.Hooks is not { } h) return "The game is not running";
            if (g.DesyncTick >= 0) return "The game is out of sync: it can't be saved";
            if (_saves.Count > 0) return "A save is already in progress";
            bool hostPlays = g.HostTurn >= 0 && !g.IsDropped(g.HostTurn) && !g.Resigned;
            int live = Enumerable.Range(0, g.TurnPlayers).Count(t => t != g.HostTurn && !g.IsDropped(t));
            // A lone client is gated by nobody: it could step past the save tick before the order arrives.
            if (!hostPlays && live < 2) return "Saving needs the host or two players still in the game";
            name = CleanText(name, 64);
            if (name.Length == 0) name = "Multiplayer";
            string id = $"{_hostId:X16}-{++_saveCounter}";
            int tick = h.ScheduleSaveAfterTurns(); // under Gate: the order goes out before any turn past it
            var order = new NetSaveOrder(id, tick, name, auto, by ?? LocalName);
            var expected = new HashSet<int> { 0 };
            foreach (var c in _conns)
                if (c.PlayerId > 0 && c.PlayerId != int.MaxValue && !c.IsClosed) expected.Add(c.PlayerId);
            _saves[id] = new SaveCollect(order, expected);
            Broadcast(SaveOrderFrame(id, tick, name, auto, order.By));
            g.OnSaveOrder(order);
            Raise(new SaveOrderedEvent(id, tick, name, auto, order.By));
            return null;
        }
    }

    internal override void RequestSave(NetGame game, string name)
    {
        lock (Gate)
        {
            if (game != Game) return;
            if (OrderSave(name) is string why) Raise(new SaveRefusedEvent(why));
        }
    }

    internal override void ReportSaved(NetGame game, string id, bool ok, ulong stateHash, string error)
    {
        lock (Gate)
            if (game == Game) OnSaveDone(0, id, ok, stateHash, error);
    }

    private void OnSaveDone(int playerId, string id, bool ok, ulong hash, string error)
    {
        if (!_saves.TryGetValue(id, out var p) || !p.Expected.Contains(playerId)) return;
        p.Results[playerId] = (ok, hash, CleanText(error, 200));
        CheckSaveDone(p, timedOut: false);
    }

    private string NameOfPerson(int playerId) =>
        playerId == 0 ? LocalName : _conns.FirstOrDefault(c => c.PlayerId == playerId)?.Name ?? $"player {playerId}";

    private void CheckSaveDone(SaveCollect p, bool timedOut)
    {
        var alive = p.Expected.Where(pid => pid == 0 || _conns.Any(c => c.PlayerId == pid && !c.IsClosed)).ToList();
        if (!timedOut && alive.Any(pid => !p.Results.ContainsKey(pid))) return;
        _saves.Remove(p.Order.Id);
        var o = p.Order;
        bool hostOk = p.Results.TryGetValue(0, out var mine) && mine.ok;
        var problems = new List<string>();
        foreach (int pid in alive)
        {
            if (!p.Results.TryGetValue(pid, out var r)) problems.Add($"{NameOfPerson(pid)}: no answer");
            else if (!r.ok) problems.Add($"{NameOfPerson(pid)}: {r.error}");
            else if (hostOk && r.hash != mine.hash) problems.Add($"{NameOfPerson(pid)}: a different game state");
        }
        bool ok = hostOk && problems.Count == 0;
        string text = ok ? $"Game saved as \"{o.Name}\" (every player has a copy)"
            : $"Save \"{o.Name}\" failed: {string.Join("; ", problems.DefaultIfEmpty("the host could not write it"))}";
        Broadcast(SaveResultFrame(o.Id, ok, o.Name, o.Auto, text));
        Game?.OnSaveResult(o.Id, ok);
        Raise(new SaveResultEvent(o.Id, ok, o.Name, o.Auto, text));
        if (!ok) SystemChatAll(text);
    }

    /// <summary>Monitor (every 250 ms): save timeouts, the autosave, throttled save uploads.</summary>
    private void PumpSaves()
    {
        foreach (var p in _saves.Values.ToList())
            if (Stopwatch.GetElapsedTime(p.Started).TotalSeconds > SaveTimeoutSeconds) CheckSaveDone(p, timedOut: true);
        foreach (var (pid, u) in _saveUploads.ToList())
        {
            if (u.Conn.IsClosed) _saveUploads.Remove(pid);
            else PumpUpload(u, MsgType.SaveChunk);
        }
        var g = Game;
        if (!Autosave || g?.Hooks is not { } h || Phase != NetPhase.Playing || AutosaveIntervalTicks <= 0) return;
        if (_nextAutosaveTick < 0) _nextAutosaveTick = g.Start.StartTick + AutosaveIntervalTicks;
        if (h.LastStepped < _nextAutosaveTick || _saves.Count > 0) return;
        _autosaveSlot = _autosaveSlot % 3 + 1;
        if (OrderSave(AutosavePrefix + _autosaveSlot, auto: true, by: "Autosave") != null) _autosaveSlot--;
        _nextAutosaveTick = h.LastStepped + AutosaveIntervalTicks;
    }

    /// <summary>Under Gate (receive thread): the save messages of a client.</summary>
    private void OnSaveMessage(NetConnection c, MsgType type, ByteReader r)
    {
        switch (type)
        {
            case MsgType.SaveRequest:
                if (OrderSave(r.Str() ?? "", by: c.Name) is string why) c.Send(SaveRefusalFrame(why));
                break;
            case MsgType.SaveDone:
            {
                string id = r.Str() ?? "";
                bool ok = r.U8() != 0;
                ulong hash = r.Raw<ulong>();
                OnSaveDone(c.PlayerId, id, ok, hash, r.Str() ?? "");
                break;
            }
            case MsgType.SaveFileRequest:
            {
                r.Str();
                ulong want = r.Raw<ulong>();
                if (Phase != NetPhase.Lobby || _saveBytes == null || !LobbyRef.IsLoad || want != _saveHash) return;
                var copy = _saveBytes;
                if (TamperSaveUpload != null)
                {
                    copy = (byte[])_saveBytes.Clone();
                    TamperSaveUpload(copy);
                }
                var u = new MapUpload(c, want, copy);
                _saveUploads[c.PlayerId] = u;
                PumpUpload(u, MsgType.SaveChunk);
                break;
            }
            case MsgType.SaveStatus:
                OnSaveStatus(c, r.Raw<ulong>(), r.U8(), (long)Math.Min(r.VarU(), (ulong)long.MaxValue));
                break;
        }
    }

    private void OnSaveStatus(NetConnection c, ulong hash, byte state, long received)
    {
        if (!LobbyRef.IsLoad || hash != _saveHash || Phase != NetPhase.Lobby) return;
        var before = _saveState.GetValueOrDefault(c.PlayerId);
        (bool ready, int progress) now;
        if (state == 1)
        {
            _saveUploads.Remove(c.PlayerId);
            now = (true, 100);
        }
        else if (state == 2)
        {
            _saveUploads.Remove(c.PlayerId);
            now = (false, 0);
            SystemChatAll($"{c.Name} could not get the save (its copy differs from the host's)");
        }
        else
        {
            long total = LobbyRef.Load!.FileBytes;
            if (_saveUploads.TryGetValue(c.PlayerId, out var u))
            {
                u.Acked = Math.Max(u.Acked, received);
                PumpUpload(u, MsgType.SaveChunk);
            }
            now = (false, total <= 0 ? 0 : (int)Math.Clamp(received * 100 / total, 0, 99));
            if (!before.ready && now.progress / 5 == before.progress / 5) { _saveState[c.PlayerId] = now; return; }
        }
        _saveState[c.PlayerId] = now;
        if (!now.ready && LobbyRef.EntryOf(c.PlayerId) is { } e) e.Ready = e.Kind == NetSlotKind.Observer;
        if (now != before || state == 2) Changed();
    }

    /// <summary>From <see cref="Changed"/>: every person's save state into the lobby (load lobby only).</summary>
    private void StampSaves()
    {
        bool load = LobbyRef.IsLoad;
        foreach (var s in LobbyRef.People)
        {
            var st = !load || s.PlayerId == 0 ? (true, 0) : _saveState.GetValueOrDefault(s.PlayerId);
            s.SaveReady = st.Item1;
            s.SaveProgress = st.Item2;
        }
    }

    // ------------------------------------------------------------------ the load lobby

    /// <summary>Turns the lobby into the load lobby of the multiplayer save at <paramref name="path"/>: its slots,
    /// factions, teams, colours and options; the people already here take their saved slots by name (others observe).
    /// Throws <see cref="SaveFormatException"/> / <see cref="IncompatibleSaveException"/> (not a usable multiplayer save),
    /// <see cref="MapFileException"/> (its map file is not installed) or an IO exception.</summary>
    public void OpenLoadLobby(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (info, hash) = MultiplayerSaveInfo.ReadStateHash(bytes);
        if (info.SimBuild != GameInfo.CurrentSimBuild)
            throw new IncompatibleSaveException("This save was made by another game build: a multiplayer game can't resume from it");
        var lobby = LobbyState.ForLoad(info, hash, bytes.Length);
        var map = ResolveMap(lobby.Options);
        if (lobby.Options.HasMapFile && map == null)
            throw new MapFileException($"The map file '{lobby.Options.MapName}' of this save is not installed");
        var mp = info.Multiplayer!;
        lock (Gate)
        {
            if (Phase != NetPhase.Lobby) throw new InvalidOperationException("The game has already started");
            var people = LobbyRef.People.Select(s => (s.PlayerId, s.Name, s.MapReady, s.MapProgress)).OrderBy(p => p.PlayerId).ToList();
            var before = LobbyRef.Options;
            lobby.Version = LobbyRef.Version;
            LobbyRef = lobby;
            foreach (var (pid, name, mapReady, progress) in people)
            {
                int slot = lobby.SavedSlotFor(name, pid == 0 ? mp.SavedBySlot : -1);
                if (slot >= 0) Claim(lobby.Slots[slot], pid, name, mapReady, progress);
                else lobby.Observers.Add(NewObserver(pid, name, mapReady, progress));
            }
            ApplyMapOptions(before, lobby.Options, map);
            if (before.MapFile != lobby.Options.MapFile || before.MapHash != lobby.Options.MapHash)
                foreach (var o in lobby.Observers)
                    if (o.PlayerId != 0) o.MapReady = !lobby.Options.HasMapFile;
            _saveBytes = bytes;
            _saveHash = hash;
            _saveUploads.Clear();
            _saveState.Clear();
            SaveTransfer = new MapTransferStatus(mp.SaveId, info.Name, hash, MapTransferState.Present, bytes.Length, bytes.Length, Path.GetFullPath(path));
            Changed();
            SystemChatAll($"Loading the saved game \"{info.Name}\" ({TimeSpan.FromSeconds(info.GameSeconds):h\\:mm\\:ss} played): every player takes a saved slot");
        }
    }

    private static void Claim(NetSlot s, int playerId, string name, bool mapReady, int progress)
    {
        s.Kind = NetSlotKind.Human;
        s.PlayerId = playerId;
        s.Name = name;
        s.Ready = playerId == 0;
        s.MapReady = mapReady;
        s.MapProgress = progress;
    }

    /// <summary>HandleHello in a load lobby: the saved slot with the person's name, else an observer.</summary>
    private void SeatInLoadLobby(NetConnection c, string name, bool observe)
    {
        bool mapReady = !LobbyRef.Options.HasMapFile;
        int slot = observe ? -1 : LobbyRef.SavedSlotFor(name);
        if (slot >= 0) Claim(LobbyRef.Slots[slot], c.PlayerId, name, mapReady, 0);
        else LobbyRef.Observers.Add(NewObserver(c.PlayerId, name, mapReady, 0));
        c.Send(Msg.Build(MsgType.Welcome, w => w.VarU((ulong)c.PlayerId)));
        foreach (var line in ChatLog.TakeLast(30)) c.Send(ChatFrame(line));
        Changed();
        SystemChatAll(slot >= 0 ? $"{name} joined and takes the saved slot of {LobbyRef.Slots[slot].SavedName}"
            : $"{name} joined as an observer (a free saved slot can be taken)");
    }

    /// <summary>ApplyRequest in a load lobby: only moves between free saved slots / observers and the ready flag
    /// (factions, teams and colours are the saved ones).</summary>
    private void LoadRequest(int playerId, LobbyRequest q)
    {
        bool changed = false;
        if (q.Observe == 1) changed = MoveObserver(playerId, true, -1);
        else if (q.Observe == 0 || q.MoveToSlot >= 0)
        {
            int dst = q.MoveToSlot;
            if (dst < 0) dst = LobbyRef.FirstOpenSlot();
            if ((uint)dst < (uint)LobbyRef.Slots.Count && LobbyState.IsFreeSavedSlot(LobbyRef.Slots[dst]))
            {
                int i = LobbyRef.SlotOf(playerId);
                if (i >= 0)
                {
                    var s = LobbyRef.Slots[i];
                    Claim(LobbyRef.Slots[dst], playerId, s.Name, s.MapReady, s.MapProgress);
                    s.Kind = NetSlotKind.Open;
                    s.PlayerId = -1;
                    s.Name = "";
                    s.Ready = false;
                    changed = true;
                }
                else if (LobbyRef.ObserverOf(playerId) >= 0) changed = MoveObserver(playerId, false, dst);
                if (changed && playerId != 0) LobbyRef.Slots[dst].Ready = false;
            }
        }
        if (q.Ready >= 0 && LobbyRef.SlotOf(playerId) is int k and >= 0)
        {
            var s = LobbyRef.Slots[k];
            bool hasSave = playerId == 0 || _saveState.GetValueOrDefault(playerId).ready;
            s.Ready = (q.Ready != 0 && s.MapReady && hasSave) || playerId == 0;
            changed = true;
        }
        if (changed) Changed();
    }

    /// <summary>SetSlotKind in a load lobby: a saved human slot can be handed to the AI (Open/kicked → AI) and back.</summary>
    private void SetLoadSlotKind(int slot, NetSlotKind kind)
    {
        NetConnection? kick = null;
        lock (Gate)
        {
            if (Phase != NetPhase.Lobby || (uint)slot >= (uint)LobbyRef.Slots.Count) return;
            var s = LobbyRef.Slots[slot];
            if (s.SavedName.Length == 0 || kind is not (NetSlotKind.AI or NetSlotKind.Open)) return;
            if (s.Kind == NetSlotKind.Human)
            {
                if (s.PlayerId == 0) return;
                kick = _conns.FirstOrDefault(c => c.PlayerId == s.PlayerId);
            }
            else if (s.Kind == kind) return;
            s.Kind = kind;
            s.PlayerId = -1;
            s.Name = kind == NetSlotKind.AI ? NetSlot.Clip(s.SavedName + " (AI)", 24) : "";
            s.Ready = kind == NetSlotKind.AI;
            s.MapReady = true;
            s.MapProgress = 0;
            Changed();
        }
        kick?.Close("kicked", Msg.Text(MsgType.Reject, "You were removed from the game by the host"));
    }

    /// <summary>SetOptions in a load lobby: only what happens to a leaving player can change.</summary>
    private void SetLoadOptions(LobbyOptions o)
    {
        lock (Gate)
        {
            if (Phase != NetPhase.Lobby || !LobbyRef.IsLoad) return;
            LobbyRef.Options.OnDrop = o.OnDrop;
            Changed();
        }
    }
}

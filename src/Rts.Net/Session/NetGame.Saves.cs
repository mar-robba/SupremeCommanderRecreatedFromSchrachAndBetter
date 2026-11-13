using Rts.Net.Lobby;
using Rts.Net.Lockstep;
using Rts.Sim;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;

namespace Rts.Net.Session;

/// <summary>(10a-3) A save the host ordered: every peer writes <see cref="Name"/> right after stepping to <see cref="Tick"/>.</summary>
public sealed record NetSaveOrder(string Id, int Tick, string Name, bool Auto, string By);

/// <summary>
/// (10a-3) Saving a multiplayer game. Every peer (players and observers) writes its own file at the tick the host chose
/// (<see cref="LockstepHooks.ScheduleSave"/>): world state captured on the sim thread (<see cref="CaptureSave"/>),
/// header + <see cref="MultiplayerSaveInfo"/> filled here, encoded and written on a worker into
/// <see cref="NetSession.SaveDir"/>, the state hash reported to the host. A loaded game's AI takeovers (saved humans
/// nobody took) are injected at the first tick after the load.
/// </summary>
public sealed partial class NetGame
{
    private readonly List<NetSaveOrder> _saveOrders = new();
    private volatile string? _lastSavePath;

    /// <summary>Captures the world for a save (sim thread; set before the runner starts). The client passes its
    /// recording (replay so far + client data); null = a bare header (terrain embedded, no replay).</summary>
    public Func<Simulation, string, SaveGame.Captured>? CaptureSave;

    /// <summary>Path of the last file this peer wrote for a save order (null = none yet).</summary>
    public string? LastSavePath => _lastSavePath;

    /// <summary>The earliest save order this peer has not written yet (null = none).</summary>
    public NetSaveOrder? PendingSave
    {
        get { lock (_lock) return _saveOrders.Count == 0 ? null : _saveOrders.MinBy(o => o.Tick); }
    }

    /// <summary>The runner's pause state: paused, unless a scheduled save tick is still ahead (every peer must step up
    /// to it, then the game pauses again).</summary>
    public bool HoldForPause => Paused && !(Hooks is { } h && h.PendingSaveTick >= 0);

    /// <summary>Asks the host to save the game for everybody under <paramref name="name"/> (the host decides; a
    /// <see cref="SaveOrderedEvent"/> or <see cref="SaveRefusedEvent"/> follows, then a <see cref="SaveResultEvent"/>).</summary>
    public void RequestSave(string name)
    {
        name = NetSession.CleanText(name, 64);
        _session.RequestSave(this, name.Length > 0 ? name : "Multiplayer");
    }

    /// <summary>Under the game lock, from <see cref="CreateHooks"/>.</summary>
    private void SetupSaves(LockstepHooks h, int startTick)
    {
        foreach (int army in Start.TakeoverArmies())
            h.InjectAt(startTick + 1, new PlayerLeftCommand(army, DropAction.AiTakeover));
        h.SaveDue = OnSaveDue;
        foreach (var o in _saveOrders) h.ScheduleSave(o.Tick);
    }

    /// <summary>A save order of the host (network thread). The host itself calls it after scheduling the tick.</summary>
    internal void OnSaveOrder(NetSaveOrder o)
    {
        bool missed = false;
        lock (_lock)
        {
            if (_saveOrders.Any(x => x.Id == o.Id)) return;
            _saveOrders.Add(o);
            if (Hooks is { } h && !h.ScheduleSave(o.Tick))
            {
                _saveOrders.Remove(o);
                missed = true;
            }
        }
        if (missed) _session.ReportSaved(this, o.Id, false, 0, "this peer had already passed the save tick");
        ApplyPause();
    }

    internal void OnSaveResult(string id, bool ok)
    {
        lock (_lock) _saveOrders.RemoveAll(o => o.Id == id); // a timed-out order is not written any more
        ApplyPause();
    }

    private void ApplyPause()
    {
        if (_runner is { } r) r.Paused = HoldForPause;
    }

    /// <summary>Sim thread, right after the step to <paramref name="tick"/> (negative = a scheduled tick was passed).</summary>
    private void OnSaveDue(Simulation sim, int tick)
    {
        List<NetSaveOrder> missed;
        NetSaveOrder? o = null;
        int now = sim.World.Tick;
        lock (_lock)
        {
            missed = _saveOrders.Where(x => x.Tick < now).ToList();
            foreach (var m in missed) _saveOrders.Remove(m);
            if (tick >= 0 && _saveOrders.FirstOrDefault(x => x.Tick == tick) is { } due)
            {
                o = due;
                _saveOrders.Remove(due);
            }
        }
        ApplyPause();
        foreach (var m in missed) _session.ReportSaved(this, m.Id, false, 0, "the save tick was missed");
        if (o == null) return;
        SaveGame.Captured c;
        try
        {
            c = CaptureSave?.Invoke(sim, o.Name) ?? SaveGame.Capture(sim, new GameInfo(), null, o.Name);
            FillHeader(c.Info, o, tick, c.State);
        }
        catch (Exception e)
        {
            _session.ReportSaved(this, o.Id, false, 0, "capture failed: " + e.Message);
            return;
        }
        string dir = _session.SaveDir;
        var order = o;
        Task.Run(() =>
        {
            try
            {
                var bytes = SaveGame.Encode(c);
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, SaveStore.Sanitize(order.Name) + SaveStore.SaveExt);
                SaveStore.WriteAtomic(path, bytes);
                _lastSavePath = Path.GetFullPath(path);
                _session.ReportSaved(this, order.Id, true, c.Info.Multiplayer!.StateHash, "");
            }
            catch (Exception e)
            {
                _session.ReportSaved(this, order.Id, false, 0, "cannot write the file: " + e.Message);
            }
        });
    }

    /// <summary>The multiplayer part of a save header (identical on every peer except the writer's slot and name).</summary>
    private void FillHeader(GameInfo info, NetSaveOrder o, int tick, byte[] state)
    {
        info.Name = o.Name;
        info.LocalArmy = Math.Max(0, LocalArmy); // an observer's copy loads in single player as army 0
        info.Multiplayer = new MultiplayerSaveInfo
        {
            SaveId = o.Id, StateHash = MultiplayerSaveInfo.HashState(state), Tick = tick,
            Lobby = LobbySave.Encode(LobbySave.ForSave(Start)),
            SavedBySlot = Start.Lobby.SlotOf(_session.PlayerId), SavedBy = _session.LocalName,
            Auto = o.Auto, Humans = Start.TurnPlayers.Length,
        };
    }
}

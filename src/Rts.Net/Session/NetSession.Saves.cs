using Rts.Net.Lobby;
using Rts.Net.Wire;
using Rts.Sim.Data;
using Rts.Sim.Persistence;

namespace Rts.Net.Session;

/// <summary>(10a-3) The host ordered a save: every peer writes <paramref name="Name"/> after <paramref name="Tick"/>.</summary>
public sealed record SaveOrderedEvent(string Id, int Tick, string Name, bool Auto, string By) : NetEvent;

/// <summary>(10a-3) Outcome of a save order for everybody (ok = every peer wrote the same state).</summary>
public sealed record SaveResultEvent(string Id, bool Ok, string Name, bool Auto, string Text) : NetEvent;

/// <summary>(10a-3) The host refused this peer's save request.</summary>
public sealed record SaveRefusedEvent(string Reason) : NetEvent;

/// <summary>
/// (10a-3) Multiplayer save/load, common part. Saving: <see cref="NetGame.RequestSave"/> → the host orders a save at a
/// tick (<see cref="MsgType.SaveOrder"/>) → every peer writes its own file there (<see cref="SaveDir"/>) and reports the
/// state hash (<see cref="MsgType.SaveDone"/>) → the host announces the result (<see cref="MsgType.SaveResult"/>).
/// Loading: the host opens a load lobby (<see cref="HostSession.OpenLoadLobby"/>); every client finds its copy by save
/// id + state hash or downloads it (<see cref="SaveTransfer"/>), then all build the game with <see cref="LoadSavedGame"/>.
/// </summary>
public abstract partial class NetSession
{
    /// <summary>Folder where multiplayer saves are written and looked for (default: the game's saves folder).</summary>
    public string SaveDir = SaveStore.SavesDir;

    /// <summary>Largest save file a client accepts from the host.</summary>
    public const long MaxSaveBytes = 512L << 20;

    private volatile MapTransferStatus? _saveTransfer;

    /// <summary>Load lobby: this peer's copy of the save (Id = save id, Hash = state hash; Path once present).</summary>
    public MapTransferStatus? SaveTransfer { get => _saveTransfer; protected set => _saveTransfer = value; }

    /// <summary>Path of this peer's copy of the load lobby's save (null = not present yet).</summary>
    public string? SavePath => SaveTransfer is { Ready: true } t ? t.Path : null;

    internal abstract void RequestSave(NetGame game, string name);
    internal abstract void ReportSaved(NetGame game, string id, bool ok, ulong stateHash, string error);

    /// <summary>Builds the simulation of a loaded game from this peer's copy of the save (<see cref="SavePath"/>);
    /// <paramref name="mapDir"/> = where downloaded map files are. Pure sim work: safe on a background thread.</summary>
    public static LoadedGame LoadSavedGame(BlueprintDb db, GameStart start, string path, string? mapDir = null)
    {
        var load = start.Lobby.Load ?? throw new InvalidOperationException("not a load lobby");
        var bytes = SaveGame.ReadFile(path);
        var (info, hash) = MultiplayerSaveInfo.ReadStateHash(bytes);
        if (info.Multiplayer?.SaveId != load.SaveId || hash != load.StateHash)
            throw new SaveFormatException("The save file differs from the host's");
        var map = NetGameSetup.LoadMap(start, mapDir);
        var g = SaveGame.Decode(bytes, db, map);
        if (g.Sim.World.Tick != start.StartTick) throw new SaveFormatException($"The save is at tick {g.Sim.World.Tick}, the lobby says {start.StartTick}");
        return g;
    }

    /// <summary>True if <paramref name="info"/> was written by this peer (its own camera may be restored): same lobby
    /// slot and name as the local person of <paramref name="start"/>.</summary>
    public bool WroteSave(GameInfo info, GameStart start)
    {
        var mp = info.Multiplayer;
        if (mp == null) return false;
        int slot = start.Lobby.SlotOf(PlayerId);
        return string.Equals(mp.SavedBy, LocalName, StringComparison.OrdinalIgnoreCase) && (slot == mp.SavedBySlot || slot < 0 && mp.SavedBySlot < 0);
    }

    /// <summary>Multiplayer saves of <paramref name="dir"/> (newest first): the "Load game" list of the host.</summary>
    public static List<SaveEntry> ListMultiplayerSaves(string? dir = null) =>
        SaveStore.List(dir ?? SaveStore.SavesDir, SaveStore.SaveExt).Where(e => e.Info?.Multiplayer != null).ToList();

    internal static byte[] SaveOrderFrame(string id, int tick, string name, bool auto, string by) => Msg.Build(MsgType.SaveOrder, w =>
    {
        w.Str(id);
        w.VarU((ulong)tick);
        w.Str(name);
        w.U8(auto ? (byte)1 : (byte)0);
        w.Str(by);
    });

    internal static byte[] SaveRefusalFrame(string reason) => Msg.Build(MsgType.SaveOrder, w =>
    {
        w.Str("");
        w.VarU(0);
        w.Str(reason);
    });

    internal static byte[] SaveResultFrame(string id, bool ok, string name, bool auto, string text) => Msg.Build(MsgType.SaveResult, w =>
    {
        w.Str(id);
        w.U8(ok ? (byte)1 : (byte)0);
        w.Str(name);
        w.Str(text);
        w.U8(auto ? (byte)1 : (byte)0);
    });

    /// <summary>Under Gate: a SaveOrder (or refusal) from the host / the host itself.</summary>
    internal void ReceiveSaveOrder(ByteReader r)
    {
        string id = r.Str() ?? "";
        int tick = r.Count(int.MaxValue);
        if (tick == 0)
        {
            Raise(new SaveRefusedEvent(CleanText(r.Str(), 200)));
            return;
        }
        string name = CleanText(r.Str(), 64);
        bool auto = r.U8() != 0;
        string by = CleanText(r.Str(), 24);
        Game?.OnSaveOrder(new NetSaveOrder(id, tick, name, auto, by));
        Raise(new SaveOrderedEvent(id, tick, name, auto, by));
    }

    internal void ReceiveSaveResult(ByteReader r)
    {
        string id = r.Str() ?? "";
        bool ok = r.U8() != 0;
        string name = CleanText(r.Str(), 64), text = CleanText(r.Str(), 300);
        bool auto = !r.AtEnd && r.U8() != 0;
        Game?.OnSaveResult(id, ok);
        Raise(new SaveResultEvent(id, ok, name, auto, text));
    }

    /// <summary>Under Gate: raises an event (for the host's own results).</summary>
    internal void RaiseSaveResult(SaveResultEvent e) => Raise(e);
}

using Rts.Net.Wire;
using Rts.Sim.Persistence;

namespace Rts.Net.Session;

/// <summary>
/// (10a-3) The client's side of multiplayer save/load. Saving: requests go to the host, the host's orders schedule the
/// local save (<see cref="NetGame"/>), the written file's state hash goes back. Load lobby: the save is looked for in
/// <see cref="NetSession.SaveDir"/> by save id + state hash (the hash is recomputed from the file: a changed copy does
/// not count); missing → <see cref="MsgType.SaveFileRequest"/>, chunks acknowledged with <see cref="MsgType.SaveStatus"/>,
/// the complete file verified off-thread and stored (a damaged or changed copy of the same save is replaced).
/// </summary>
public sealed partial class ClientSession
{
    private string _saveId = "";
    private ulong _saveHash;
    private byte[]? _saveBuf;
    private long _saveReceived;
    private int _saveGen;
    private string? _saveReplace;

    internal override void RequestSave(NetGame game, string name)
    {
        if (game != Game || game.Offline) return;
        _conn.Send(Msg.Build(MsgType.SaveRequest, w => w.Str(name)));
    }

    internal override void ReportSaved(NetGame game, string id, bool ok, ulong stateHash, string error)
    {
        if (game != Game) return;
        _conn.Send(Msg.Build(MsgType.SaveDone, w =>
        {
            w.Str(id);
            w.U8(ok ? (byte)1 : (byte)0);
            w.Raw(stateHash);
            w.Str(NetSlotClip(error));
        }));
    }

    private static string NetSlotClip(string s) => s.Length <= 200 ? s : s[..200];

    /// <summary>Under Gate (receive thread): a save message of the host.</summary>
    private void OnSaveMessage(MsgType type, ByteReader r)
    {
        switch (type)
        {
            case MsgType.SaveOrder: ReceiveSaveOrder(r); break;
            case MsgType.SaveResult: ReceiveSaveResult(r); break;
            case MsgType.SaveChunk: OnSaveChunk(r); break;
        }
    }

    /// <summary>Under Gate, after every lobby message: a new load lobby save → find it locally (worker) or download it.</summary>
    private void CheckSave()
    {
        var load = LobbyRef.Load;
        if (load == null)
        {
            if (_saveId.Length > 0) { _saveId = ""; _saveHash = 0; _saveBuf = null; _saveGen++; }
            SaveTransfer = null;
            return;
        }
        if (load.SaveId == _saveId && load.StateHash == _saveHash) return;
        _saveId = load.SaveId;
        _saveHash = load.StateHash;
        _saveBuf = null;
        _saveReceived = 0;
        _saveReplace = null;
        int gen = ++_saveGen;
        SaveTransfer = new MapTransferStatus(load.SaveId, load.SaveName, load.StateHash, MapTransferState.Verifying, 0, load.FileBytes);
        string dir = SaveDir, id = load.SaveId;
        ulong hash = load.StateHash;
        long size = load.FileBytes;
        string name = load.SaveName;
        Task.Run(() =>
        {
            var (path, replace) = FindSave(dir, id, hash);
            lock (Gate) FoundSave(gen, path, replace, size, name);
        });
    }

    /// <summary>Worker: the local copy of save <paramref name="id"/> whose world state hashes to <paramref name="hash"/>
    /// (null = none), and a file of the same save that failed the check (replaced by the download).</summary>
    internal static (string? path, string? replace) FindSave(string dir, string id, ulong hash)
    {
        string? replace = null;
        if (!Directory.Exists(dir)) return (null, null);
        foreach (var f in Directory.EnumerateFiles(dir, "*" + SaveStore.SaveExt).Order(StringComparer.Ordinal))
        {
            bool sameSave = false;
            try
            {
                var info = SaveContainer.ReadInfo(f, out var kind);
                if (kind != SaveFileKind.Save || info.Multiplayer is not { } mp || mp.SaveId != id) continue;
                sameSave = true;
                var (_, h) = MultiplayerSaveInfo.ReadStateHash(File.ReadAllBytes(f));
                if (h == hash) return (Path.GetFullPath(f), null);
            }
            catch (SaveFormatException) { }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (sameSave) replace ??= f; // changed or damaged copy of this save: the download replaces it
        }
        return (null, replace);
    }

    private void FoundSave(int gen, string? path, string? replace, long size, string name)
    {
        if (gen != _saveGen || Phase == NetPhase.Closed) return;
        var t = SaveTransfer;
        if (path != null)
        {
            // Report first, publish second: a caller that sees the file Present may send Ready at once, and the host drops a Ready that arrives before this status.
            SendSaveStatus(1, size);
            if (t != null) SaveTransfer = t with { State = MapTransferState.Present, Received = size, Path = path };
            return;
        }
        if (size <= 0 || size > MaxSaveBytes)
        {
            if (t != null) SaveTransfer = t with { State = MapTransferState.Failed, Error = "the save file is too large" };
            SendSaveStatus(2, 0);
            return;
        }
        _saveReplace = replace;
        _saveBuf = new byte[size];
        if (t != null) SaveTransfer = t with { State = MapTransferState.Downloading };
        string id = _saveId;
        ulong hash = _saveHash;
        _conn.Send(Msg.Build(MsgType.SaveFileRequest, w =>
        {
            w.Str(id);
            w.Raw(hash);
        }));
        SendSaveStatus(0, 0);
        SystemChat(replace != null ? $"Your copy of the save \"{name}\" differs from the host's: downloading it again ({(size + 1023) / 1024} KB)"
            : $"Downloading the save \"{name}\" from the host ({(size + 1023) / 1024} KB)");
    }

    private void OnSaveChunk(ByteReader r)
    {
        ulong hash = r.Raw<ulong>();
        long off = (long)Math.Min(r.VarU(), (ulong)long.MaxValue);
        long total = (long)Math.Min(r.VarU(), (ulong)long.MaxValue);
        var data = r.Block();
        var buf = _saveBuf;
        if (buf == null || hash != _saveHash || total != buf.Length || off < 0 || off + data.Length > total) return;
        data.CopyTo(buf, off);
        _saveReceived = Math.Max(_saveReceived, off + data.Length);
        var t = SaveTransfer;
        if (_saveReceived < total)
        {
            if (t != null) SaveTransfer = t with { Received = _saveReceived };
            SendSaveStatus(0, _saveReceived);
            return;
        }
        _saveBuf = null;
        if (t != null) SaveTransfer = t with { Received = total, State = MapTransferState.Verifying };
        int gen = _saveGen;
        string id = _saveId, dir = SaveDir;
        string? replace = _saveReplace;
        Task.Run(() => FinishSave(buf, id, hash, gen, dir, replace));
    }

    /// <summary>Worker: verify (container + same save id + same state hash), store, report.</summary>
    private void FinishSave(byte[] bytes, string id, ulong hash, int gen, string dir, string? replace)
    {
        string? path = null, error = null;
        try
        {
            var (info, h) = MultiplayerSaveInfo.ReadStateHash(bytes);
            if (info.Multiplayer?.SaveId != id || h != hash) error = "it differs from the host's save (state hash mismatch)";
            else path = StoreSave(bytes, info, dir, replace);
        }
        catch (SaveFormatException e) { error = "damaged: " + e.Message; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { error = "cannot save it: " + e.Message; }
        lock (Gate)
        {
            if (gen != _saveGen || Phase == NetPhase.Closed) return;
            var t = SaveTransfer;
            if (error != null)
            {
                if (t != null) SaveTransfer = t with { State = MapTransferState.Failed, Error = error };
                SendSaveStatus(2, bytes.Length);
                SystemChat("Save download failed: " + error);
                return;
            }
            SendSaveStatus(1, bytes.Length);
            if (t != null) SaveTransfer = t with { State = MapTransferState.Present, Path = path, Received = bytes.Length };
            SystemChat("Save received");
        }
    }

    /// <summary>Writes the downloaded save: over <paramref name="replace"/> (a bad copy of the same save), else
    /// <c>&lt;dir&gt;/&lt;name&gt;.rtssave</c> unless that is another save (then <c>&lt;name&gt;_&lt;id&gt;</c>).</summary>
    internal static string StoreSave(byte[] bytes, GameInfo info, string dir, string? replace)
    {
        Directory.CreateDirectory(dir);
        string target = replace ?? Path.Combine(dir, SaveStore.Sanitize(info.Name) + SaveStore.SaveExt);
        if (replace == null && File.Exists(target))
        {
            string? other = null;
            try { other = SaveContainer.ReadInfo(target, out _).Multiplayer?.SaveId; }
            catch (Exception e) when (e is SaveFormatException or IOException) { }
            if (other != info.Multiplayer!.SaveId)
                target = Path.Combine(dir, SaveStore.Sanitize($"{info.Name}_{info.Multiplayer.SaveId}") + SaveStore.SaveExt);
        }
        SaveStore.WriteAtomic(target, bytes);
        return Path.GetFullPath(target);
    }

    private void SendSaveStatus(byte state, long received)
    {
        ulong hash = _saveHash;
        _conn.Send(Msg.Build(MsgType.SaveStatus, w =>
        {
            w.Raw(hash);
            w.U8(state);
            w.VarU((ulong)Math.Max(0, received));
        }));
    }
}

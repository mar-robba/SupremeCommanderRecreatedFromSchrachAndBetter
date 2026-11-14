using Rts.Net.Wire;
using Rts.Sim.Map;
using Rts.Sim.Persistence;

namespace Rts.Net.Session;

/// <summary>
/// (8b-3) The client's side of map files: every lobby with a map file is checked against the installed maps
/// (<see cref="MapLibrary.Find"/>: data/maps, then <see cref="NetSession.MapDir"/>, by id + content hash). Missing →
/// <see cref="MsgType.MapRequest"/>, the chunks are acknowledged one by one (<see cref="MsgType.MapStatus"/> 0), the
/// complete file is verified off-thread (decodes + header hash = the lobby's hash) and stored in <see cref="NetSession.MapDir"/>
/// (temp file + move). Failure (damaged / different file) is reported (status 2): the host can't start with this player.
/// </summary>
public sealed partial class ClientSession
{
    private string _mapId = "";
    private ulong _mapHash;
    private byte[]? _mapBuf;
    private long _mapReceived;
    private int _mapGen;

    /// <summary>Under Gate, after every lobby message.</summary>
    private void CheckMap()
    {
        var o = LobbyRef.Options;
        if (!o.HasMapFile)
        {
            if (_mapId.Length > 0) { _mapId = ""; _mapHash = 0; _mapBuf = null; _mapGen++; }
            MapTransfer = null;
            return;
        }
        if (o.MapFile == _mapId && o.MapHash == _mapHash) return;
        _mapId = o.MapFile;
        _mapHash = o.MapHash;
        _mapBuf = null;
        _mapReceived = 0;
        _mapGen++;
        string? path = null;
        try { path = MapLibrary.Find(o.MapFile, o.MapHash, MapDir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (path != null)
        {
            // Report first, publish second: a caller that sees the file Present may send Ready at once, and the host drops a Ready that arrives before this status.
            SendMapStatus(1, o.MapBytes);
            MapTransfer = new MapTransferStatus(o.MapFile, o.MapName, o.MapHash, MapTransferState.Present, o.MapBytes, o.MapBytes, path);
            return;
        }
        if (o.MapBytes <= 0 || o.MapBytes > MaxMapBytes)
        {
            MapTransfer = new MapTransferStatus(o.MapFile, o.MapName, o.MapHash, MapTransferState.Failed, 0, o.MapBytes, Error: "the map file is too large");
            SendMapStatus(2, 0);
            return;
        }
        _mapBuf = new byte[o.MapBytes];
        MapTransfer = new MapTransferStatus(o.MapFile, o.MapName, o.MapHash, MapTransferState.Downloading, 0, o.MapBytes);
        string id = o.MapFile;
        ulong hash = o.MapHash;
        _conn.Send(Msg.Build(MsgType.MapRequest, w =>
        {
            w.Str(id);
            w.Raw(hash);
        }));
        SystemChat($"Downloading the map {o.MapName} from the host ({(o.MapBytes + 1023) / 1024} KB)");
    }

    /// <summary>Under Gate (receive thread).</summary>
    private void OnMapChunk(ByteReader r)
    {
        ulong hash = r.Raw<ulong>();
        long off = (long)Math.Min(r.VarU(), (ulong)long.MaxValue);
        long total = (long)Math.Min(r.VarU(), (ulong)long.MaxValue);
        var data = r.Block();
        var buf = _mapBuf;
        if (buf == null || hash != _mapHash || total != buf.Length || off < 0 || off + data.Length > total) return;
        data.CopyTo(buf, off);
        _mapReceived = Math.Max(_mapReceived, off + data.Length);
        var t = MapTransfer;
        if (_mapReceived < total)
        {
            if (t != null) MapTransfer = t with { Received = _mapReceived };
            SendMapStatus(0, _mapReceived);
            return;
        }
        _mapBuf = null;
        if (t != null) MapTransfer = t with { Received = total, State = MapTransferState.Verifying };
        int gen = _mapGen;
        string id = _mapId, dir = MapDir;
        Task.Run(() => FinishMap(buf, id, hash, gen, dir));
    }

    /// <summary>Worker: verify, store, report.</summary>
    private void FinishMap(byte[] bytes, string id, ulong hash, int gen, string dir)
    {
        string? path = null, error = null;
        try
        {
            var f = MapFile.Decode(bytes); // throws when damaged (payload hash = header hash)
            if (f.Info.Hash != hash) error = "it differs from the host's map (content hash mismatch)";
            else path = StoreMap(bytes, id, hash, dir);
        }
        catch (MapFileException e) { error = e.Message; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { error = "cannot save it: " + e.Message; }
        lock (Gate)
        {
            if (gen != _mapGen || Phase == NetPhase.Closed) return;
            var t = MapTransfer;
            if (error != null)
            {
                if (t != null) MapTransfer = t with { State = MapTransferState.Failed, Error = error };
                SendMapStatus(2, bytes.Length);
                SystemChat("Map download failed: " + error);
                return;
            }
            SendMapStatus(1, bytes.Length);
            if (t != null) MapTransfer = t with { State = MapTransferState.Present, Path = path, Received = bytes.Length };
            SystemChat("Map received");
        }
    }

    /// <summary>Writes <c>&lt;dir&gt;/&lt;id&gt;.rtsmap</c> (another file of that name with a different hash is kept:
    /// then <c>&lt;id&gt;_&lt;hash&gt;.rtsmap</c>).</summary>
    internal static string StoreMap(byte[] bytes, string id, ulong hash, string dir)
    {
        Directory.CreateDirectory(dir);
        string safe = SafeMapId(id);
        string target = Path.Combine(dir, safe + MapFile.Extension);
        if (File.Exists(target))
        {
            ulong? old = null;
            try { old = MapFile.ReadHeader(target).Hash; }
            catch (Exception e) when (e is MapFileException or IOException) { }
            if (old != null && old != hash) target = Path.Combine(dir, $"{safe}_{hash:X16}{MapFile.Extension}");
        }
        string tmp = target + ".part";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, target, overwrite: true);
        return Path.GetFullPath(target);
    }

    private void SendMapStatus(byte state, long received)
    {
        ulong hash = _mapHash;
        _conn.Send(Msg.Build(MsgType.MapStatus, w =>
        {
            w.Raw(hash);
            w.U8(state);
            w.VarU((ulong)Math.Max(0, received));
        }));
    }
}

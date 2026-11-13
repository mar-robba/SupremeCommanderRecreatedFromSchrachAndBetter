using System.Diagnostics;
using Rts.Net.Lobby;
using Rts.Net.Transport;
using Rts.Net.Wire;
using Rts.Sim.Map;
using Rts.Sim.Persistence;

namespace Rts.Net.Session;

/// <summary>
/// (8b-3) The host's side of map files in the lobby: <see cref="SetOptions"/> resolves the chosen file (hash, name,
/// start positions, size, biome go into <see cref="LobbyOptions"/>), clients report <see cref="MsgType.MapStatus"/>
/// (have it / receiving / failed) and the ones without it get the file in <see cref="NetSession.MapChunkBytes"/>
/// chunks, at most <see cref="NetSession.MapWindowBytes"/> beyond what they acknowledged.
/// </summary>
public sealed partial class HostSession
{
    private sealed class MapUpload(NetConnection conn, ulong hash, byte[] bytes)
    {
        public readonly NetConnection Conn = conn;
        public readonly ulong Hash = hash;
        public readonly byte[] Bytes = bytes;
        public readonly long Started = Stopwatch.GetTimestamp();
        public long Sent, Acked;
    }

    private sealed record ResolvedMap(string Path, MapFileInfo Info, byte[] Bytes);

    private byte[]? _mapBytes;
    private ResolvedMap? _map;
    private readonly Dictionary<int, MapUpload> _uploads = new();

    /// <summary>Upload throttle in bytes per second (0 = as fast as the window allows). Debug/screenshots: env
    /// <c>RTS_MAP_UPLOAD_KBPS</c> sets it at start.</summary>
    public long MapUploadBytesPerSecond = long.TryParse(Environment.GetEnvironmentVariable("RTS_MAP_UPLOAD_KBPS"), out long k) && k > 0 ? k * 1024 : 0;

    /// <summary>Test hook: changes the uploaded copy (e.g. corrupts it) to test the client's check.</summary>
    internal Action<byte[]>? TamperMapUpload;

    /// <summary>Finds and reads the chosen map file (outside the lock). Null = generated map or not found.</summary>
    private ResolvedMap? ResolveMap(LobbyOptions o)
    {
        if (!o.HasMapFile) return null;
        var cur = _map;
        if (cur != null && cur.Info.Id == o.MapFile && (o.MapHash == 0 || o.MapHash == cur.Info.Hash)) return cur;
        try
        {
            string? path = o.MapHash != 0 ? MapLibrary.Find(o.MapFile, o.MapHash, MapDir)
                : MapLibrary.Resolve(o.MapFile) ?? MapLibrary.Resolve(o.MapFile, MapDir);
            if (path == null) return null;
            var bytes = File.ReadAllBytes(path);
            var info = MapFile.ReadHeader(path);
            return new ResolvedMap(path, info, bytes);
        }
        catch (Exception e) when (e is MapFileException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Under Gate: fills the map fields of the new options; a new map resets the clients' map state.</summary>
    private void ApplyMapOptions(LobbyOptions before, LobbyOptions o, ResolvedMap? map)
    {
        if (o.HasMapFile && map == null)
        {
            SystemChatAll($"Map file '{o.MapFile}' not found: using a generated map");
            o.MapFile = "";
        }
        if (!o.HasMapFile)
        {
            o.MapHash = 0;
            o.MapName = "";
            o.MapPlayers = 0;
            o.MapBytes = 0;
            o.MapBiome = 0;
            _map = null;
            _mapBytes = null;
            _uploads.Clear();
            MapTransfer = null;
            foreach (var s in LobbyRef.Slots) s.MapReady = true;
            return;
        }
        var info = map!.Info;
        o.MapFile = info.Id;
        o.MapHash = info.Hash;
        o.MapName = NetSlot.Clip(info.Name.Length > 0 ? info.Name : info.Id, 64);
        o.MapPlayers = info.Players;
        o.MapBytes = map.Bytes.Length;
        o.MapBiome = (byte)info.Biome;
        o.MapSize = info.Width; // display (LAN listing, loading screen)
        o.Preset = info.Preset;
        if (before.MapFile == o.MapFile && before.MapHash == o.MapHash && _mapBytes != null) return;
        _map = map;
        _mapBytes = map.Bytes;
        _uploads.Clear();
        MapTransfer = new MapTransferStatus(info.Id, o.MapName, info.Hash, MapTransferState.Present, map.Bytes.Length, map.Bytes.Length, map.Path);
        foreach (var s in LobbyRef.Slots)
        {
            bool client = s.Kind == NetSlotKind.Human && s.PlayerId != 0;
            s.MapReady = !client;
            s.MapProgress = 0;
            if (client) s.Ready = false;
        }
    }

    /// <summary>Under Gate (receive thread): MapRequest / MapStatus of a client.</summary>
    private void OnMapMessage(NetConnection c, MsgType type, ByteReader r)
    {
        var o = LobbyRef.Options;
        if (type == MsgType.MapRequest)
        {
            r.Str();
            ulong want = r.Raw<ulong>();
            if (Phase != NetPhase.Lobby || _mapBytes == null || want != o.MapHash) return; // stale: a new lobby follows
            var copy = _mapBytes;
            if (TamperMapUpload != null)
            {
                copy = (byte[])_mapBytes.Clone();
                TamperMapUpload(copy);
            }
            var u = new MapUpload(c, want, copy);
            _uploads[c.PlayerId] = u;
            PumpUpload(u);
            return;
        }
        ulong hash = r.Raw<ulong>();
        byte state = r.U8();
        long received = (long)Math.Min(r.VarU(), (ulong)long.MaxValue);
        if (hash != o.MapHash || !o.HasMapFile) return;
        int i = LobbyRef.SlotOf(c.PlayerId);
        if (i < 0) return;
        var s = LobbyRef.Slots[i];
        switch (state)
        {
            case 1:
                _uploads.Remove(c.PlayerId);
                if (!s.MapReady || s.MapProgress != 100)
                {
                    s.MapReady = true;
                    s.MapProgress = 100;
                    Changed();
                }
                break;
            case 2:
                _uploads.Remove(c.PlayerId);
                s.MapReady = false;
                s.MapProgress = 0;
                s.Ready = false;
                Changed();
                SystemChatAll($"{c.Name} could not use the map file (it differs from the host's)");
                break;
            default:
            {
                int pct = o.MapBytes <= 0 ? 0 : (int)Math.Clamp(received * 100 / o.MapBytes, 0, 99);
                if (_uploads.TryGetValue(c.PlayerId, out var u))
                {
                    u.Acked = Math.Max(u.Acked, received);
                    PumpUpload(u);
                }
                if (pct / 5 != s.MapProgress / 5 || s.MapReady)
                {
                    s.MapReady = false;
                    s.MapProgress = pct;
                    Changed();
                }
                break;
            }
        }
    }

    /// <summary>Under Gate (monitor, every 250 ms): continues throttled uploads, forgets closed connections.</summary>
    private void PumpMapUploads()
    {
        if (_uploads.Count == 0) return;
        foreach (var (pid, u) in _uploads.ToList())
        {
            if (u.Conn.IsClosed) _uploads.Remove(pid);
            else PumpUpload(u);
        }
    }

    private void PumpUpload(MapUpload u, MsgType type = MsgType.MapChunk) // (10a-3) SaveChunk for saves
    {
        var bytes = u.Bytes;
        long rate = MapUploadBytesPerSecond;
        int chunk = rate > 0 ? (int)Math.Clamp(rate / 8, 1024, MapChunkBytes) : MapChunkBytes;
        while (u.Sent < bytes.Length && u.Sent - u.Acked < MapWindowBytes && !u.Conn.IsClosed)
        {
            if (rate > 0 && u.Sent >= Stopwatch.GetElapsedTime(u.Started).TotalSeconds * rate + chunk) break;
            long off = u.Sent;
            int n = (int)Math.Min(chunk, bytes.Length - off);
            ulong hash = u.Hash;
            u.Conn.Send(Msg.Build(type, w =>
            {
                w.Raw(hash);
                w.VarU((ulong)off);
                w.VarU((ulong)bytes.Length);
                w.Block(bytes.AsSpan((int)off, n));
            }));
            u.Sent += n;
        }
    }
}

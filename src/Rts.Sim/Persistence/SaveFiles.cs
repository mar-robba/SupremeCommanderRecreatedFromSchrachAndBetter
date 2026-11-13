using System.IO.Compression;
using System.Text;
using Rts.Sim.Data;
using Rts.Sim.Map;

namespace Rts.Sim.Persistence;

public enum SaveFileKind { Save, Replay }

/// <summary>
/// File container shared by saves and replays:
/// magic (8 bytes) · container version (u16) · header JSON (<see cref="GameInfo"/>, length-prefixed, uncompressed) ·
/// payload length (u64) · payload hash (u64) · Brotli-compressed payload. Files of an older version
/// (≥ <see cref="MinVersion"/>) are upgraded on load by <see cref="SaveMigrations"/>.
/// </summary>
public static class SaveContainer
{
    /// <summary>Version written by this build (history: <see cref="SaveMigrations.Steps"/>).</summary>
    public const ushort Version = 2;
    /// <summary>Oldest version this build still reads (migrated on load).</summary>
    public const ushort MinVersion = 1;
    private static readonly byte[] SaveMagic = "RTSSAVE\x1A"u8.ToArray();
    private static readonly byte[] ReplayMagic = "RTSREPL\x1A"u8.ToArray();

    private static byte[] Magic(SaveFileKind k) => k == SaveFileKind.Save ? SaveMagic : ReplayMagic;

    public static byte[] Encode(SaveFileKind kind, GameInfo info, ReadOnlySpan<byte> payload, CompressionLevel level = CompressionLevel.Fastest) =>
        EncodeRaw(kind, Version, info.ToJson(), payload, level);

    /// <summary>Writes a container with an explicit version and header JSON (tests write older-format files with it).</summary>
    public static byte[] EncodeRaw(SaveFileKind kind, ushort version, string headerJson, ReadOnlySpan<byte> payload,
        CompressionLevel level = CompressionLevel.Fastest)
    {
        var ms = new MemoryStream(payload.Length / 8 + 4096);
        ms.Write(Magic(kind));
        var head = new ByteWriter(1024);
        head.Raw(version);
        var json = Encoding.UTF8.GetBytes(headerJson);
        head.Raw(json.Length);
        head.Bytes(json);
        head.Raw((ulong)payload.Length);
        head.Raw(Hash64.Of(payload));
        ms.Write(head.Span);
        using (var z = new BrotliStream(ms, level, leaveOpen: true)) z.Write(payload);
        return ms.ToArray();
    }

    /// <summary>Reads only the header (fast: for file lists).</summary>
    public static GameInfo ReadInfo(string path, out SaveFileKind kind)
    {
        using var f = File.OpenRead(path);
        var head = new byte[8 + 2 + 4];
        if (f.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length) throw new SaveFormatException("Not a save or replay file (too short)");
        kind = KindOf(head);
        var r = new ByteReader(head, 8, 6);
        ushort version = CheckVersion(r.Raw<ushort>());
        int len = r.Raw<int>();
        if (len < 2 || len > f.Length) throw ByteReader.Corrupt("header length");
        var json = new byte[len];
        f.ReadExactly(json);
        return Header(version, json);
    }

    public static (GameInfo info, byte[] payload) Decode(byte[] file, SaveFileKind expected)
    {
        if (file.Length < 14) throw new SaveFormatException("Not a save or replay file (too short)");
        var kind = KindOf(file);
        if (kind != expected) throw new SaveFormatException(expected == SaveFileKind.Save ? "This is a replay, not a saved game" : "This is a saved game, not a replay");
        var r = new ByteReader(file, 8, file.Length - 8);
        ushort version = CheckVersion(r.Raw<ushort>());
        int len = r.Raw<int>();
        if (len < 2 || len > r.Remaining) throw ByteReader.Corrupt("header length");
        var info = Header(version, r.Take(len));
        ulong size = r.Raw<ulong>();
        ulong hash = r.Raw<ulong>();
        if (size > (ulong)Array.MaxLength) throw ByteReader.Corrupt("payload size");
        var payload = new byte[size];
        try
        {
            using var z = new BrotliStream(new MemoryStream(file, r.Position, r.Remaining), CompressionMode.Decompress);
            z.ReadExactly(payload);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or IOException or InvalidOperationException)
        {
            throw new SaveFormatException("Damaged file (compressed data)", e);
        }
        if (Hash64.Of(payload) != hash) throw new SaveFormatException("Damaged file (checksum mismatch)");
        return (info, SaveMigrations.MigratePayload(version, payload, kind));
    }

    private static GameInfo Header(ushort version, ReadOnlySpan<byte> json)
    {
        var info = GameInfo.FromJson(SaveMigrations.MigrateHeader(version, Encoding.UTF8.GetString(json)));
        info.FileVersion = version;
        return info;
    }

    private static SaveFileKind KindOf(ReadOnlySpan<byte> head)
    {
        if (head[..8].SequenceEqual(SaveMagic)) return SaveFileKind.Save;
        if (head[..8].SequenceEqual(ReplayMagic)) return SaveFileKind.Replay;
        throw new SaveFormatException("Not a save or replay file");
    }

    private static ushort CheckVersion(ushort v)
    {
        if (v < MinVersion || v > Version)
            throw new IncompatibleSaveException($"File format version {v} is not supported by this build (reads {MinVersion}..{Version})");
        return v;
    }
}

/// <summary>A decoded save game (or the start of a replay): ready-to-run simulation + metadata + replay so far.</summary>
public sealed class LoadedGame
{
    public required GameInfo Info;
    public required Simulation Sim;
    public ReplayLog? Replay;
    /// <summary>(11a-2) What the load had to translate (another sim layout or other blueprints); when
    /// <c>Migrated</c>, <see cref="Replay"/> is null: the recording restarts at the loaded tick.</summary>
    public SaveMigrationReport Migration = new();
}

/// <summary>
/// Save games: the full current state + the replay log so far (a loaded game keeps a complete replay).
/// <see cref="Capture"/> must run on the sim thread (between ticks) or with the runner stopped; encoding,
/// compression and file IO can then run on any thread.
/// </summary>
public static class SaveGame
{
    public sealed class Captured
    {
        public required GameInfo Info;
        public required byte[] State;
        public ReplayLog? Replay;
        public byte[]? Terrain;
    }

    /// <summary>SEQUENTIAL: snapshot of the game for saving. <paramref name="info"/> is copied and updated.</summary>
    public static Captured Capture(Simulation sim, GameInfo info, ReplayRecorder? recorder, string name)
    {
        var i = info.Clone();
        i.FillFrom(sim);
        i.Name = name;
        i.CreatedUtc = DateTime.UtcNow;
        return new Captured
        {
            Info = i, State = WorldState.Capture(sim), Replay = recorder?.Snapshot(),
            Terrain = i.Map == null && i.MapFile == null ? TerrainBytes(sim.World.Terrain) : null,
        };
    }

    public static byte[] Encode(Captured c)
    {
        var o = new ByteWriter(c.State.Length + (c.Replay?.InitialState.Length ?? 0) + 4096);
        o.Block(c.State);
        o.U8(c.Replay != null ? (byte)1 : (byte)0);
        c.Replay?.Write(o);
        o.Block(c.Terrain ?? []);
        return SaveContainer.Encode(SaveFileKind.Save, c.Info, o.Span);
    }

    public static LoadedGame Decode(byte[] file, BlueprintDb db) => Decode(file, db, null);

    /// <summary>(10a-3) Same; <paramref name="map"/> = the game's map file already found and checked by the caller (a
    /// multiplayer peer: data/maps or its download folder), null = look it up as usual.</summary>
    public static LoadedGame Decode(byte[] file, BlueprintDb db, MapFile? map)
    {
        var (info, payload) = SaveContainer.Decode(file, SaveFileKind.Save);
        var r = new ByteReader(payload);
        var state = r.Block();
        ReplayLog? replay = r.U8() != 0 ? ReplayLog.Read(r) : null;
        var embedded = r.Block();
        var terrain = map != null && info.MapFile != null && info.Map == null
            ? CheckedTerrain(info, db, map.TerrainFor(Math.Max(1, info.Players.Count)), StateHasBlueprintTable(state))
            : TerrainFor(info, db, embedded, StateHasBlueprintTable(state));
        if (info.MathVersion != Core.DMath.Version)
        {
            // (10a-1) A save made with other math (e.g. the old MathF sim, on a map file) loads and plays on, but the
            // replay recorded so far cannot be re-simulated by this build: the recording restarts at the loaded tick.
            replay = null;
            info.MathVersion = Core.DMath.Version;
        }
        var sim = WorldState.Restore(state, db, terrain, out var migration);
        if (migration.Migrated)
        {
            // (11a-2) made by another sim build (other layouts or blueprints): it plays on, but the replay recorded so
            // far cannot be re-simulated by this build, so the recording restarts at the loaded tick (like the math case)
            replay = null;
            info.BlueprintHash = GameInfo.BlueprintHashOf(db);
        }
        return new LoadedGame { Info = info, Sim = sim, Replay = replay, Migration = migration };
    }

    /// <summary>(11a-2) True when a saved state carries the blueprint id table (state format 2+): its blueprints may
    /// differ from this build's (translated by id); older states load only with the same blueprints.</summary>
    /// <summary>(11a-2) The blueprint check a menu may run on a file's header (<see cref="SaveContainer.ReadInfo"/>) before
    /// loading it, the same rule the loader applies: a replay needs exactly these blueprints; a save is not refused here
    /// (changed blueprints migrate, a blueprint in use that is gone, or an older save without a blueprint table, is
    /// refused by the loader with the id). Null = go ahead.</summary>
    public static string? BlueprintMismatch(GameInfo info, SaveFileKind kind, BlueprintDb db) =>
        kind == SaveFileKind.Replay && info.BlueprintHash != GameInfo.BlueprintHashOf(db)
            ? "The unit blueprints changed since this replay was made (different game version)"
            : null;

    public static bool StateHasBlueprintTable(byte[] state) => state.Length > 0 && new ByteReader(state).VarU() >= 2;

    public static LoadedGame Load(string path, BlueprintDb db) => Decode(ReadFile(path), db);

    // ------------------------------------------------------------------ replays

    public static byte[] EncodeReplay(GameInfo info, ReplayLog log, byte[]? terrain = null)
    {
        var i = info.Clone();
        i.Ticks = log.LastTick;
        var o = new ByteWriter(log.InitialState.Length + 4096);
        log.Write(o);
        o.Block(terrain ?? []);
        return SaveContainer.Encode(SaveFileKind.Replay, i, o.Span, CompressionLevel.Optimal);
    }

    /// <summary>Decodes a replay; the terrain is regenerated (or read from the file) and checked.</summary>
    public static (GameInfo info, ReplayLog log, Terrain terrain) DecodeReplay(byte[] file, BlueprintDb db)
    {
        var (info, payload) = SaveContainer.Decode(file, SaveFileKind.Replay);
        CheckReplayMath(info);
        var r = new ByteReader(payload);
        var log = ReplayLog.Read(r);
        var terrain = TerrainFor(info, db, r.Block());
        return (info, log, terrain);
    }

    public static ReplayPlayer OpenReplay(byte[] file, BlueprintDb db)
    {
        var (info, log, terrain) = DecodeReplay(file, db);
        return new ReplayPlayer(info, log, db, terrain);
    }

    public static ReplayPlayer OpenReplay(string path, BlueprintDb db) => OpenReplay(ReadFile(path), db);

    /// <summary>(10a-1) A replay re-simulates only with the math it was recorded with (<see cref="Core.DMath.Version"/>;
    /// 0 = recorded before the deterministic math, with the old MathF sim): any other version is refused up front
    /// instead of desyncing a few seconds in.</summary>
    public static void CheckReplayMath(GameInfo info)
    {
        if (info.MathVersion != Core.DMath.Version)
            throw new IncompatibleSaveException(info.MathVersion == 0
                ? "This replay was recorded by an older game version (before the cross-platform math): it cannot be played back by this version"
                : $"This replay was recorded with math version {info.MathVersion}, this game version uses {Core.DMath.Version}: it cannot be played back");
    }

    /// <summary>True if <paramref name="info"/>'s replay can be played back by this build (<see cref="CheckReplayMath"/>).</summary>
    public static bool ReplayMathMatches(GameInfo info) => info.MathVersion == Core.DMath.Version;

    // ------------------------------------------------------------------ helpers

    public static byte[] ReadFile(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new SaveFormatException($"Cannot read {Path.GetFileName(path)}: {e.Message}", e); }
    }

    public static byte[] TerrainBytes(Terrain t)
    {
        var o = new ByteWriter(t.Heights.Length * 5 + 4096);
        var g = new GraphWriter(o);
        g.WriteRef(t);
        g.Flush();
        return o.ToArray();
    }

    /// <summary>The terrain of a file: regenerated from the stored map settings, or the embedded one. Also checks that
    /// the blueprints match (block indices and stats are part of the saved state).</summary>
    public static Terrain TerrainFor(GameInfo info, BlueprintDb db, byte[] embedded, bool blueprintsMayDiffer = false)
    {
        if (!blueprintsMayDiffer && info.BlueprintHash != GameInfo.BlueprintHashOf(db))
            throw new IncompatibleSaveException("The unit blueprints changed since this file was made (different game version)");
        Terrain terrain;
        if (info.Map != null) terrain = MapGenerator.Generate(info.Map);
        else if (info.MapFile != null) terrain = MapFileTerrain(info);
        else
        {
            if (embedded.Length == 0) throw ByteReader.Corrupt("missing terrain");
            var g = new GraphReader(new ByteReader(embedded));
            terrain = g.ReadRef() as Terrain ?? throw ByteReader.Corrupt("terrain");
            g.Complete();
        }
        if (GameInfo.TerrainHashOf(terrain) != info.TerrainHash)
            throw new IncompatibleSaveException(info.MapFile != null
                ? $"The map '{info.MapFile}' does not give the terrain this file was made on"
                : info.Map != null && info.MathVersion != Core.DMath.Version // (10a-1) the generator uses DMath
                    ? "This file was made on a generated map by an older game version (before the cross-platform math): " +
                      "the map generator now makes a different terrain, so it cannot be loaded (games on map files still load)"
                    : "The map generator changed since this file was made (different game version)");
        return terrain;
    }

    /// <summary>(10a-3) Blueprint + terrain hash checks for a terrain supplied by the caller.</summary>
    private static Terrain CheckedTerrain(GameInfo info, BlueprintDb db, Terrain terrain, bool blueprintsMayDiffer)
    {
        if (!blueprintsMayDiffer && info.BlueprintHash != GameInfo.BlueprintHashOf(db))
            throw new IncompatibleSaveException("The unit blueprints changed since this file was made (different game version)");
        if (GameInfo.TerrainHashOf(terrain) != info.TerrainHash)
            throw new IncompatibleSaveException($"The map '{info.MapFile}' does not give the terrain this file was made on");
        return terrain;
    }

    /// <summary>(8b-2) The terrain of a game played on a map file: the installed file with the same id and content hash.</summary>
    public static Terrain MapFileTerrain(GameInfo info, string? mapDir = null)
    {
        string id = info.MapFile!;
        var path = (mapDir == null ? MapLibrary.Find(id, info.MapFileHash) : null) // (8b-3) also maps downloaded in MP
            ?? MapLibrary.Resolve(id, mapDir)
            ?? throw new IncompatibleSaveException($"This game was played on the map '{info.MapName ?? id}' ({id}{MapFile.Extension}), which is not installed in data/maps");
        MapFile file;
        try
        {
            var head = MapFile.ReadHeader(path);
            if (head.Hash != info.MapFileHash)
                throw new IncompatibleSaveException($"The map file '{id}' is different from the one this game was played on (hash {head.HashHex}, expected {info.MapFileHash:X16})");
            file = MapFile.Load(path);
        }
        catch (MapFileException e) { throw new IncompatibleSaveException($"The map file '{id}' cannot be read: {e.Message}"); }
        catch (IOException e) { throw new IncompatibleSaveException($"The map file '{id}' cannot be read: {e.Message}"); }
        return file.TerrainFor(Math.Max(1, info.Players.Count));
    }
}

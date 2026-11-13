using Rts.Net.Wire;
using Rts.Sim;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.World;

namespace Rts.Net.Lobby;

/// <summary>Lobby slot state. Append-only (on the wire).</summary>
public enum NetSlotKind : byte { Open, Human, AI, Closed, /** (10a-2) an entry of <see cref="LobbyState.Observers"/> */ Observer }

/// <summary>One lobby slot. Humans are bound to a player id (0 = host).</summary>
public sealed class NetSlot
{
    public NetSlotKind Kind;
    public int PlayerId = -1;
    public string Name = "";
    /// <summary>Faction id ("" = default faction).</summary>
    public string Faction = "";
    /// <summary>0 = no team (free for all), 1..4 = team number.</summary>
    public int Team;
    /// <summary>Index into the army color palette (GameSetup.ArmyColors).</summary>
    public int Color;
    public AiDifficulty Difficulty = AiDifficulty.Normal;
    public bool Ready;
    /// <summary>(8b-3) The player has the lobby's map file (always true for generated maps, AIs and the host).</summary>
    public bool MapReady = true;
    /// <summary>(8b-3) Map download progress 0..100 (display only).</summary>
    public int MapProgress;
    /// <summary>(10a-3) Load lobby: the name of the human who played this slot in the saved game ("" = not a saved human slot).
    /// An unclaimed saved slot is Open; an AI that takes over a missing human is AI with this name set.</summary>
    public string SavedName = "";
    /// <summary>(10a-3) Load lobby: the person has the save file (host and AIs always); progress 0..100 while downloading.</summary>
    public bool SaveReady = true;
    public int SaveProgress;

    /// <summary>The slot becomes an army (human or AI).</summary>
    public bool IsArmy => Kind is NetSlotKind.Human or NetSlotKind.AI;

    public NetSlot Clone() => (NetSlot)MemberwiseClone();

    public void Write(ByteWriter w)
    {
        w.U8((byte)Kind);
        w.VarI(PlayerId);
        w.Str(Name);
        w.Str(Faction);
        w.VarU((ulong)Math.Max(0, Team));
        w.VarU((ulong)Math.Max(0, Color));
        w.U8((byte)Difficulty);
        w.U8(Ready ? (byte)1 : (byte)0);
        w.U8(MapReady ? (byte)1 : (byte)0); // (8b-3)
        w.U8((byte)Math.Clamp(MapProgress, 0, 100));
        w.Str(SavedName); // (10a-3) protocol 6
        w.U8(SaveReady ? (byte)1 : (byte)0);
        w.U8((byte)Math.Clamp(SaveProgress, 0, 100));
    }

    public static NetSlot Read(ByteReader r) => new()
    {
        Kind = (NetSlotKind)Math.Min(r.U8(), (byte)NetSlotKind.Observer),
        PlayerId = (int)r.VarI(),
        Name = Clip(r.Str(), 24),
        Faction = Clip(r.Str(), 32),
        Team = r.Count(16),
        Color = r.Count(64),
        Difficulty = (AiDifficulty)Math.Min(r.U8(), (byte)AiDifficulty.Hard),
        Ready = r.U8() != 0,
        MapReady = r.U8() != 0,
        MapProgress = Math.Min(r.U8(), (byte)100),
        SavedName = Clip(r.Str(), 24),
        SaveReady = r.U8() != 0,
        SaveProgress = Math.Min(r.U8(), (byte)100),
    };

    internal static string Clip(string? s, int max) => s == null ? "" : s.Length <= max ? s : s[..max];
}

/// <summary>Game options owned by the host.</summary>
public sealed class LobbyOptions
{
    public int MapSize = 256;
    public MapPreset Preset = MapPreset.Plains;
    /// <summary>0..1, negative = preset default.</summary>
    public float Water = -1;
    public int Seed = 42;
    /// <summary>What happens to the army of a player who disconnects.</summary>
    public DropAction OnDrop = DropAction.AiTakeover;
    /// <summary>Game options (victory, unit cap, resources, fog, speed, cheats, prebuilt): lobby defaults = cheats off.</summary>
    public GameOptions Game = GameOptions.SetupDefaults();
    /// <summary>(8b-3) Map file id ("" = generated map from size/preset/water/seed). The host fills the rest from the
    /// file (<see cref="Session.HostSession.SetOptions"/>): content hash, name, start positions, file size, its biome.</summary>
    public string MapFile = "";
    public ulong MapHash;
    public string MapName = "";
    public int MapPlayers;
    public long MapBytes;
    /// <summary>(8b-3) The map file's own biome (<see cref="Biome"/> byte).</summary>
    public byte MapBiome;
    /// <summary>(8b-3) Chosen biome: 0 = the map's default, 1 + <see cref="Rts.Sim.Map.Biome"/>. Visual only.</summary>
    public byte Biome;

    public bool HasMapFile => MapFile.Length > 0;

    /// <summary>(8b-3) The chosen biome, or null = the map's (temperate for generated maps).</summary>
    public Biome? BiomeChoice => Biome == 0 ? null : Biomes.FromByte(Biome - 1);

    /// <summary>(8b-3) The biome the game shows.</summary>
    public Biome EffectiveBiome => BiomeChoice ?? (HasMapFile ? Biomes.FromByte(MapBiome) : Rts.Sim.Map.Biome.Temperate);

    public LobbyOptions Clone()
    {
        var c = (LobbyOptions)MemberwiseClone();
        c.Game = Game.Clone();
        return c;
    }

    public void Write(ByteWriter w)
    {
        w.VarU((ulong)MapSize);
        w.U8((byte)Preset);
        w.Raw(Water);
        w.VarI(Seed);
        w.U8((byte)OnDrop);
        w.U8((byte)Game.Victory);
        w.VarU((ulong)Math.Clamp(Game.UnitCap, 1, GameOptions.MaxUnitCap));
        w.U8((byte)Game.Resources);
        w.U8((byte)Game.Fog);
        w.U8((byte)Game.Speed);
        w.U8((byte)((Game.Cheats ? 1 : 0) | (Game.Prebuilt ? 2 : 0)));
        // (8b-3) protocol 3
        w.Str(MapFile);
        w.Raw(MapHash);
        w.Str(MapName);
        w.VarU((ulong)Math.Max(0, MapPlayers));
        w.VarU((ulong)Math.Max(0, MapBytes));
        w.U8(MapBiome);
        w.U8(Biome);
    }

    public static LobbyOptions Read(ByteReader r)
    {
        var o = new LobbyOptions
        {
            MapSize = r.Count(8192),
            Preset = (MapPreset)r.U8(),
            Water = r.Raw<float>(),
            Seed = (int)r.VarI(),
            OnDrop = (DropAction)Math.Min(r.U8(), (byte)DropAction.Defeat),
        };
        var g = o.Game;
        g.Victory = (VictoryCondition)Math.Min(r.U8(), (byte)VictoryCondition.Sandbox);
        g.UnitCap = (int)Math.Clamp(r.VarU(), 1UL, (ulong)GameOptions.MaxUnitCap);
        g.Resources = (StartResources)Math.Min(r.U8(), (byte)StartResources.High);
        g.Fog = (FogMode)Math.Min(r.U8(), (byte)FogMode.None);
        g.Speed = (GameSpeedMode)Math.Min(r.U8(), (byte)GameSpeedMode.Fast);
        byte flags = r.U8();
        g.Cheats = (flags & 1) != 0;
        g.Prebuilt = (flags & 2) != 0;
        o.MapFile = NetSlot.Clip(r.Str(), 64);
        o.MapHash = r.Raw<ulong>();
        o.MapName = NetSlot.Clip(r.Str(), 64);
        o.MapPlayers = r.Count(64);
        o.MapBytes = (long)Math.Min(r.VarU(), 1UL << 30);
        o.MapBiome = r.U8();
        o.Biome = r.U8();
        return o;
    }
}

/// <summary>The whole lobby (host authoritative: clients receive a full copy after every change).</summary>
public sealed partial class LobbyState
{
    public const int MaxSlots = 8;
    /// <summary>(10a-2) Observers at most (next to the slots; they are not armies and send no turns).</summary>
    public const int MaxObservers = 16;
    public readonly List<NetSlot> Slots = new();
    /// <summary>(10a-2) Observers (Kind = Observer, PlayerId, Name, MapReady/MapProgress): watch the whole game, chat.</summary>
    public readonly List<NetSlot> Observers = new();
    public LobbyOptions Options = new();
    /// <summary>Incremented by the host at every change.</summary>
    public int Version;
    /// <summary>(10a-3) Set in a load lobby: the multiplayer save being resumed (slots, options fixed by it).</summary>
    public LoadInfo? Load;
    public bool IsLoad => Load != null;

    public static LobbyState CreateDefault(string hostName)
    {
        var s = new LobbyState();
        for (int i = 0; i < MaxSlots; i++)
            s.Slots.Add(new NetSlot { Kind = i < 2 ? NetSlotKind.Open : NetSlotKind.Closed, Color = i });
        s.Slots[0].Kind = NetSlotKind.Human;
        s.Slots[0].PlayerId = 0;
        s.Slots[0].Name = hostName;
        s.Slots[0].Ready = true;
        return s;
    }

    public LobbyState Clone()
    {
        var c = new LobbyState { Options = Options.Clone(), Version = Version, Load = Load?.Clone() };
        foreach (var s in Slots) c.Slots.Add(s.Clone());
        foreach (var o in Observers) c.Observers.Add(o.Clone());
        return c;
    }

    public int SlotOf(int playerId) => Slots.FindIndex(s => s.Kind == NetSlotKind.Human && s.PlayerId == playerId);
    public int FirstOpenSlot() => Slots.FindIndex(s => s.Kind == NetSlotKind.Open);
    public int ArmyCount => Slots.Count(s => s.IsArmy);
    public int HumanCount => Slots.Count(s => s.Kind == NetSlotKind.Human);
    /// <summary>(10a-2) Index in <see cref="Observers"/> of a player id (-1 = not observing).</summary>
    public int ObserverOf(int playerId) => Observers.FindIndex(o => o.PlayerId == playerId);
    public bool IsObserver(int playerId) => ObserverOf(playerId) >= 0;
    /// <summary>(10a-2) The slot or observer entry of a connected player (null = unknown).</summary>
    public NetSlot? EntryOf(int playerId) => SlotOf(playerId) is int i and >= 0 ? Slots[i] : ObserverOf(playerId) is int k and >= 0 ? Observers[k] : null;
    /// <summary>(10a-2) Every connected person: human slots, then observers.</summary>
    public IEnumerable<NetSlot> People => Slots.Where(s => s.Kind == NetSlotKind.Human).Concat(Observers);

    /// <summary>Why the host can't start yet (null = it can).</summary>
    public string? Problem()
    {
        if (Load != null && LoadProblem() is string lp) return lp; // (10a-3) LoadLobby.cs
        var armies = Slots.Where(s => s.IsArmy).ToList();
        if (armies.Count < 2) return "At least two players are needed";
        if (armies.All(s => s.Team > 0 && s.Team == armies[0].Team)) return "Everybody is on the same team";
        var o = Options;
        if (o.HasMapFile && armies.Count > o.MapPlayers) return $"{o.MapName} has only {o.MapPlayers} start positions";
        var loading = People.FirstOrDefault(s => s.PlayerId != 0 && !s.MapReady); // (10a-2) observers need the map too
        if (loading != null) return $"{loading.Name} is downloading the map ({loading.MapProgress} %)";
        var notReady = Slots.FirstOrDefault(s => s.Kind == NetSlotKind.Human && s.PlayerId != 0 && !s.Ready);
        if (notReady != null) return $"{notReady.Name} is not ready";
        return null;
    }

    public void Write(ByteWriter w)
    {
        w.VarU((ulong)Version);
        Options.Write(w);
        w.VarU((ulong)Slots.Count);
        foreach (var s in Slots) s.Write(w);
        w.VarU((ulong)Observers.Count); // (10a-2) protocol 5
        foreach (var o in Observers) o.Write(w);
        w.U8(Load != null ? (byte)1 : (byte)0); // (10a-3) protocol 6
        Load?.Write(w);
    }

    public static LobbyState Read(ByteReader r)
    {
        var l = new LobbyState { Version = r.Count(int.MaxValue), Options = LobbyOptions.Read(r) };
        int n = r.Count(MaxSlots);
        for (int i = 0; i < n; i++) l.Slots.Add(NetSlot.Read(r));
        int m = r.Count(MaxObservers);
        for (int i = 0; i < m; i++)
        {
            var o = NetSlot.Read(r);
            o.Kind = NetSlotKind.Observer;
            l.Observers.Add(o);
        }
        if (r.U8() != 0) l.Load = LoadInfo.Read(r);
        return l;
    }
}

/// <summary>A client's request to change its own slot (null/-1 fields = unchanged).</summary>
public sealed class LobbyRequest
{
    public int MoveToSlot = -1;
    public string? Faction;
    public int Team = -1;
    public int Color = -1;
    /// <summary>-1 unchanged, 0 not ready, 1 ready.</summary>
    public int Ready = -1;
    /// <summary>(10a-2) -1 unchanged, 1 = become an observer, 0 = leave the observers for <see cref="MoveToSlot"/> (or the first open slot).</summary>
    public int Observe = -1;

    public void Write(ByteWriter w)
    {
        w.VarI(MoveToSlot);
        w.Str(Faction);
        w.VarI(Team);
        w.VarI(Color);
        w.VarI(Ready);
        w.VarI(Observe); // (10a-2)
    }

    public static LobbyRequest Read(ByteReader r) => new()
    {
        MoveToSlot = (int)r.VarI(), Faction = r.Str(), Team = (int)r.VarI(), Color = (int)r.VarI(), Ready = (int)r.VarI(),
        Observe = r.AtEnd ? -1 : (int)r.VarI(),
    };
}

/// <summary>
/// The start message: the final lobby + the initial input delay. Every peer derives the same armies from it:
/// army slots (human or AI) in slot order are armies 0..n-1, human slots in slot order are the lockstep turn players.
/// </summary>
public sealed partial class GameStart
{
    public LobbyState Lobby = new();
    public int Delay = 2;
    /// <summary>Lobby slot index of each army.</summary>
    public int[] ArmySlots = [];
    /// <summary>Player id of each turn player (humans in slot order).</summary>
    public int[] TurnPlayers = [];
    /// <summary>Army index of each turn player.</summary>
    public int[] TurnArmies = [];

    public static GameStart From(LobbyState lobby, int delay)
    {
        var g = new GameStart { Lobby = lobby.Clone(), Delay = delay };
        g.Derive();
        return g;
    }

    private void Derive()
    {
        var armies = new List<int>();
        var players = new List<int>();
        var playerArmies = new List<int>();
        for (int i = 0; i < Lobby.Slots.Count; i++)
        {
            var s = Lobby.Slots[i];
            if (!s.IsArmy) continue;
            if (s.Kind == NetSlotKind.Human)
            {
                players.Add(s.PlayerId);
                playerArmies.Add(armies.Count);
            }
            armies.Add(i);
        }
        ArmySlots = armies.ToArray();
        TurnPlayers = players.ToArray();
        TurnArmies = playerArmies.ToArray();
    }

    /// <summary>Turn player index of a player id (-1 = not playing).</summary>
    public int TurnIndexOf(int playerId) => Array.IndexOf(TurnPlayers, playerId);

    /// <summary>(10a-2) The player watches the game (an observer of the lobby: gets every turn, sends none).</summary>
    public bool IsObserver(int playerId) => Lobby.IsObserver(playerId);

    public void Write(ByteWriter w)
    {
        Lobby.Write(w);
        w.VarU((ulong)Delay);
    }

    public static GameStart Read(ByteReader r)
    {
        var g = new GameStart { Lobby = LobbyState.Read(r), Delay = r.Count(1000) };
        g.Derive();
        return g;
    }

    public byte[] ToFrame() => Msg.Build(MsgType.Start, Write);
}

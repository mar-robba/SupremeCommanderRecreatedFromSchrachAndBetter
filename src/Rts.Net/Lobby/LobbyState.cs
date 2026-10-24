using Rts.Net.Wire;
using Rts.Sim;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.World;

namespace Rts.Net.Lobby;

/// <summary>Lobby slot state. Append-only (on the wire).</summary>
public enum NetSlotKind : byte { Open, Human, AI, Closed }

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
    }

    public static NetSlot Read(ByteReader r) => new()
    {
        Kind = (NetSlotKind)Math.Min(r.U8(), (byte)NetSlotKind.Closed),
        PlayerId = (int)r.VarI(),
        Name = Clip(r.Str(), 24),
        Faction = Clip(r.Str(), 32),
        Team = r.Count(16),
        Color = r.Count(64),
        Difficulty = (AiDifficulty)Math.Min(r.U8(), (byte)AiDifficulty.Hard),
        Ready = r.U8() != 0,
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
        return o;
    }
}

/// <summary>The whole lobby (host authoritative: clients receive a full copy after every change).</summary>
public sealed class LobbyState
{
    public const int MaxSlots = 8;
    public readonly List<NetSlot> Slots = new();
    public LobbyOptions Options = new();
    /// <summary>Incremented by the host at every change.</summary>
    public int Version;

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
        var c = new LobbyState { Options = Options.Clone(), Version = Version };
        foreach (var s in Slots) c.Slots.Add(s.Clone());
        return c;
    }

    public int SlotOf(int playerId) => Slots.FindIndex(s => s.Kind == NetSlotKind.Human && s.PlayerId == playerId);
    public int FirstOpenSlot() => Slots.FindIndex(s => s.Kind == NetSlotKind.Open);
    public int ArmyCount => Slots.Count(s => s.IsArmy);
    public int HumanCount => Slots.Count(s => s.Kind == NetSlotKind.Human);

    /// <summary>Why the host can't start yet (null = it can).</summary>
    public string? Problem()
    {
        var armies = Slots.Where(s => s.IsArmy).ToList();
        if (armies.Count < 2) return "At least two players are needed";
        if (armies.All(s => s.Team > 0 && s.Team == armies[0].Team)) return "Everybody is on the same team";
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
    }

    public static LobbyState Read(ByteReader r)
    {
        var l = new LobbyState { Version = r.Count(int.MaxValue), Options = LobbyOptions.Read(r) };
        int n = r.Count(MaxSlots);
        for (int i = 0; i < n; i++) l.Slots.Add(NetSlot.Read(r));
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

    public void Write(ByteWriter w)
    {
        w.VarI(MoveToSlot);
        w.Str(Faction);
        w.VarI(Team);
        w.VarI(Color);
        w.VarI(Ready);
    }

    public static LobbyRequest Read(ByteReader r) => new()
    {
        MoveToSlot = (int)r.VarI(), Faction = r.Str(), Team = (int)r.VarI(), Color = (int)r.VarI(), Ready = (int)r.VarI(),
    };
}

/// <summary>
/// The start message: the final lobby + the initial input delay. Every peer derives the same armies from it:
/// army slots (human or AI) in slot order are armies 0..n-1, human slots in slot order are the lockstep turn players.
/// </summary>
public sealed class GameStart
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

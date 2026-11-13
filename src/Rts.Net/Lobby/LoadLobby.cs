using Rts.Net.Wire;
using Rts.Sim.Persistence;

namespace Rts.Net.Lobby;

/// <summary>(10a-3) The multiplayer save a load lobby resumes (<see cref="LobbyState.Load"/>, on the wire since protocol 6).</summary>
public sealed class LoadInfo
{
    /// <summary>Save order id + hash of the saved world state: a peer's copy is "the same save" when both match.</summary>
    public string SaveId = "";
    public ulong StateHash;
    /// <summary>Save name (the file name every peer used) and the saved tick.</summary>
    public string SaveName = "";
    public int Tick;
    /// <summary>Size of the host's file (download progress, limit check).</summary>
    public long FileBytes;

    public LoadInfo Clone() => (LoadInfo)MemberwiseClone();

    public void Write(ByteWriter w)
    {
        w.Str(SaveId);
        w.Raw(StateHash);
        w.Str(SaveName);
        w.VarU((ulong)Math.Max(0, Tick));
        w.VarU((ulong)Math.Max(0, FileBytes));
    }

    public static LoadInfo Read(ByteReader r) => new()
    {
        SaveId = NetSlot.Clip(r.Str(), 64), StateHash = r.Raw<ulong>(), SaveName = NetSlot.Clip(r.Str(), 64),
        Tick = r.Count(int.MaxValue), FileBytes = (long)Math.Min(r.VarU(), (ulong)long.MaxValue),
    };
}

/// <summary>(10a-3) The lobby stored in a multiplayer save header (<see cref="MultiplayerSaveInfo.Lobby"/>): base64 of
/// u32 protocol version + the lobby codec. Another protocol version = refused with a clear message.</summary>
public static class LobbySave
{
    public static string Encode(LobbyState lobby)
    {
        var w = new ByteWriter(1024);
        w.Raw(Msg.ProtocolVersion);
        lobby.Write(w);
        return Convert.ToBase64String(w.Span);
    }

    public static LobbyState Decode(string data)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(data); }
        catch (FormatException) { throw new SaveFormatException("Damaged multiplayer save (lobby)"); }
        var r = new ByteReader(bytes);
        uint v = r.Raw<uint>();
        if (v != Msg.ProtocolVersion)
            throw new IncompatibleSaveException($"This multiplayer save was made with network protocol {v} (this game uses {Msg.ProtocolVersion})");
        return LobbyState.Read(r);
    }

    /// <summary>The lobby a save of <paramref name="start"/>'s game records: the game's slots with the current holders'
    /// names (an AI that took over a missing human stays AI), no observers' state, no load info.</summary>
    public static LobbyState ForSave(GameStart start)
    {
        var l = start.Lobby.Clone();
        l.Load = null;
        foreach (var s in l.Slots.Concat(l.Observers))
        {
            s.SavedName = "";
            s.SaveReady = true;
            s.SaveProgress = 0;
            s.Ready = false;
        }
        return l;
    }
}

public sealed partial class LobbyState
{
    /// <summary>(10a-3) Load lobby: an unclaimed saved human slot (Open with its saved name).</summary>
    public static bool IsFreeSavedSlot(NetSlot s) => s.Kind == NetSlotKind.Open && s.SavedName.Length > 0;

    /// <summary>(10a-3) Why a load lobby can't start yet (null = only the usual checks remain).</summary>
    private string? LoadProblem()
    {
        var free = Slots.FirstOrDefault(IsFreeSavedSlot);
        if (free != null) return $"Waiting for {free.SavedName} (or let the AI play that slot)";
        var loading = People.FirstOrDefault(s => s.PlayerId != 0 && !s.SaveReady);
        if (loading != null) return loading.SaveProgress > 0 ? $"{loading.Name} is downloading the save ({loading.SaveProgress} %)" : $"{loading.Name} is getting the save";
        return null;
    }

    /// <summary>
    /// (10a-3) The load lobby of a multiplayer save: slots, factions, teams, colours and options come from the save.
    /// Saved human slots become Open slots with their <see cref="NetSlot.SavedName"/> (claimed by a joining player);
    /// AI slots stay AI, and so do human armies an AI already took over during the saved game. Non-army slots are Closed.
    /// </summary>
    public static LobbyState ForLoad(GameInfo info, ulong stateHash, long fileBytes)
    {
        var mp = info.Multiplayer ?? throw new IncompatibleSaveException("Not a multiplayer save");
        var saved = LobbySave.Decode(mp.Lobby);
        var l = new LobbyState { Options = saved.Options.Clone() };
        int army = 0;
        foreach (var s0 in saved.Slots)
        {
            var s = s0.Clone();
            s.Ready = false;
            s.MapReady = true;
            s.MapProgress = 0;
            s.SaveReady = true;
            s.SaveProgress = 0;
            s.SavedName = "";
            if (!s.IsArmy)
            {
                s.Kind = NetSlotKind.Closed;
                s.PlayerId = -1;
                s.Name = "";
            }
            else
            {
                var p = army < info.Players.Count ? info.Players[army] : null;
                if (p == null) throw new SaveFormatException("Damaged multiplayer save (the lobby has more armies than the game)");
                army++;
                if (s.Kind == NetSlotKind.AI || p.IsAI)
                {
                    s.Kind = NetSlotKind.AI;
                    s.Name = p.Name;
                }
                else
                {
                    s.Kind = NetSlotKind.Open;
                    s.SavedName = s.Name.Length > 0 ? s.Name : p.Name;
                    s.Name = "";
                }
                s.PlayerId = -1;
            }
            l.Slots.Add(s);
        }
        if (army != info.Players.Count) throw new SaveFormatException("Damaged multiplayer save (the lobby does not match the armies)");
        l.Load = new LoadInfo { SaveId = mp.SaveId, StateHash = stateHash, SaveName = info.Name, Tick = info.Ticks, FileBytes = fileBytes };
        return l;
    }

    /// <summary>(10a-3) The free saved slot a person named <paramref name="name"/> takes in a load lobby: the one with its
    /// saved name (case-insensitive), else <paramref name="preferred"/> if it is free, else -1 (observe until it picks one).</summary>
    public int SavedSlotFor(string name, int preferred = -1)
    {
        int i = Slots.FindIndex(s => IsFreeSavedSlot(s) && string.Equals(s.SavedName, name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) return i;
        return (uint)preferred < (uint)Slots.Count && IsFreeSavedSlot(Slots[preferred]) ? preferred : -1;
    }
}

public sealed partial class GameStart
{
    /// <summary>(10a-3) The tick a loaded game resumes from (0 = a new game).</summary>
    public int StartTick => Lobby.Load?.Tick ?? 0;

    /// <summary>(10a-3) Armies of saved humans nobody took: an AI takes them over at the first tick after the load.</summary>
    public int[] TakeoverArmies()
    {
        if (Lobby.Load == null) return [];
        var list = new List<int>();
        for (int a = 0; a < ArmySlots.Length; a++)
        {
            var s = Lobby.Slots[ArmySlots[a]];
            if (s.Kind == NetSlotKind.AI && s.SavedName.Length > 0) list.Add(a);
        }
        return list.ToArray();
    }
}

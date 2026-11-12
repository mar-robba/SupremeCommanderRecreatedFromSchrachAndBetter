using Rts.Sim.Persistence;

namespace Rts.Net.Session;

/// <summary>(10a-2) Observers, drop votes and per-player pings (shared by host and client).</summary>
public abstract partial class NetSession
{
    private volatile Dictionary<int, int> _personPings = new();

    /// <summary>Last round-trip time of a connected person in ms (lobby and game, the host's view; -1 = unknown, host = 0).</summary>
    public int PingOf(int playerId) => _personPings.TryGetValue(playerId, out int p) ? p : -1;

    /// <summary>Replaces the ping table (any thread; readers see an immutable copy).</summary>
    protected void SetPersonPings(Dictionary<int, int> pings) => _personPings = pings;

    /// <summary>The local player watches instead of playing (true: leaves its slot for the observers), or takes
    /// <paramref name="slot"/> (or the first open slot, -1) again. Lobby only.</summary>
    public abstract void Observe(bool on, int slot = -1);

    /// <summary>True if the local player sits among the observers of the lobby.</summary>
    public bool IsLocalObserver => Lobby.IsObserver(PlayerId);

    internal abstract void VoteDrop(NetGame game, int turn, bool yes);

    internal static DropVoteTally? ReadTally(ByteReader r)
    {
        int target = r.Count(64), votes = r.Count(64), needed = r.Count(64), n = r.Count(64);
        var voters = new int[n];
        for (int i = 0; i < n; i++) voters[i] = r.Count(64);
        return new DropVoteTally(target, votes, needed, voters);
    }

    internal static byte[] TallyFrame(DropVoteTally t) => Wire.Msg.Build(Wire.MsgType.DropVote, w =>
    {
        w.VarU((ulong)t.Target);
        w.VarU((ulong)t.Votes);
        w.VarU((ulong)t.Needed);
        w.VarU((ulong)t.Voters.Length);
        foreach (int v in t.Voters) w.VarU((ulong)v);
    });

    internal static Dictionary<int, int> ReadPings(ByteReader r)
    {
        int n = r.Count(Rts.Net.Lobby.LobbyState.MaxSlots + Rts.Net.Lobby.LobbyState.MaxObservers + 8);
        var d = new Dictionary<int, int>();
        for (int i = 0; i < n; i++)
        {
            int pid = (int)r.VarI(), ms = (int)r.VarI();
            d[pid] = ms;
        }
        return d;
    }
}

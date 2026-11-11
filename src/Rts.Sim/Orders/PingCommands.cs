using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using Rts.Sim.Core;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Sim.Orders;

/// <summary>(7b) Map ping kinds (SupCom: alert / move here / attack here). Append-only.</summary>
public enum PingKind : byte { Alert, Move, Attack }

/// <summary>(7b) Map ping at <paramref name="Pos"/>, shown to the sender's team (snapshot <c>Pings</c>). Carried by
/// replays and lockstep like any command; cosmetic (not part of StateHash).</summary>
public sealed record MapPingCommand(int Army, PingKind Kind, Vector2 Pos) : Command(Army);

/// <summary>One live ping (plain data, saved with the world).</summary>
public struct MapPing
{
    public int Id;
    public int Army;
    public PingKind Kind;
    public Vector2 Pos;
    /// <summary>Tick it was created.</summary>
    public int Tick;
}

/// <summary>Per-world ping state (created by the first ping only, so games without pings save identical bytes).</summary>
public sealed class PingState
{
    private static readonly ConditionalWeakTable<SimWorld, PingState> Table = new();

    public static PingState Of(SimWorld w) => Table.GetValue(w, static _ => new PingState());
    public static bool TryGet(SimWorld w, [NotNullWhen(true)] out PingState? s) => Table.TryGetValue(w, out s);

    /// <summary>Pings, oldest first (pruned on Apply).</summary>
    public readonly List<MapPing> Pings = new();
    /// <summary>Total pings created (= Id of the newest).</summary>
    public int Serial;
}

/// <summary>Map pings: throttled, short-lived markers. Sequential (CommandSystem).</summary>
public static class PingSystem
{
    /// <summary>Lifetime in ticks (10 s).</summary>
    public const int Lifetime = 100;
    /// <summary>At most <see cref="ThrottleCount"/> pings per army within <see cref="ThrottleTicks"/>.</summary>
    public const int ThrottleCount = 3, ThrottleTicks = 30;
    public const int MaxPings = 32;

    public static void Apply(SimWorld w, MapPingCommand c)
    {
        if ((byte)c.Kind > (byte)PingKind.Attack || !float.IsFinite(c.Pos.X) || !float.IsFinite(c.Pos.Y)) return;
        var st = PingState.Of(w);
        var list = st.Pings;
        list.RemoveAll(p => p.Tick <= w.Tick - Lifetime);
        int recent = 0;
        foreach (var p in list)
            if (p.Army == c.Army && p.Tick > w.Tick - ThrottleTicks) recent++;
        if (recent >= ThrottleCount) return;
        list.Add(new MapPing { Id = ++st.Serial, Army = c.Army, Kind = c.Kind, Pos = w.Terrain.Clamp(c.Pos), Tick = w.Tick });
        while (list.Count > MaxPings) list.RemoveAt(0);
    }

    /// <summary>Live pings of non-enemies of <paramref name="viewArmy"/> (all when -1). Never creates the state.</summary>
    public static void WriteSnapshot(SimWorld w, SimSnapshot s, int viewArmy)
    {
        s.PingCount = 0;
        if (!PingState.TryGet(w, out var st)) { s.PingSerial = 0; return; }
        s.PingSerial = st.Serial;
        foreach (var p in st.Pings)
        {
            if (p.Tick <= w.Tick - Lifetime) continue;
            if (viewArmy >= 0 && viewArmy < w.Armies.Length && w.IsEnemy(viewArmy, p.Army)) continue;
            if (s.PingCount == s.Pings.Length) Array.Resize(ref s.Pings, s.Pings.Length * 2);
            s.Pings[s.PingCount++] = new MapPingView
            {
                Id = p.Id, Army = p.Army, Kind = p.Kind, Tick = p.Tick,
                Pos = DMath.To3(p.Pos, w.Terrain.GetSurfaceHeight(p.Pos)),
            };
        }
    }
}

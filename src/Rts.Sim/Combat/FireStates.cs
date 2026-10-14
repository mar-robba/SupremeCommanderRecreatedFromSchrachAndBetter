using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>
/// Fire states (Fire at will / Return fire / Hold fire, <see cref="SetFireStateCommand"/>) and the "don't shoot what
/// we are capturing" rule, both read by <see cref="TargetingSystem"/> (parallel, read-only here).
/// <list type="bullet">
/// <item>Hold fire: no automatic targets. Attack orders (forced targets) and attack-ground still fire.</item>
/// <item>Return fire: automatic targets only while the unit was hit in the last <see cref="ReturnFireTicks"/>.</item>
/// <item>Captures: a unit an army (or an ally) is capturing is never acquired automatically by that army's units, so
///   the captors and nearby defenders hold fire on it; an explicit Attack order still shoots it.</item>
/// </list>
/// State: <see cref="UnitStore.FireStates"/> (reset to FireAtWill on spawn) and <see cref="BuilderState.CaptureArmy"/>.
/// </summary>
public static class FireStates
{
    /// <summary>Return fire stays "provoked" this long after the last hit (5 s).</summary>
    public const int ReturnFireTicks = 5 * SimWorld.TicksPerSecond;
    /// <summary>A capture counts as active for this many ticks after the last capture work.</summary>
    public const int CaptureHoldTicks = SimWorld.TicksPerSecond;

    public static void Apply(SimWorld w, SetFireStateCommand c)
    {
        if ((uint)c.State > (uint)FireState.HoldFire) return;
        var u = w.Units;
        foreach (var h in c.Units)
        {
            if (!CommandSystem.Owns(w, c.Army, h)) continue;
            u.FireStates[h.Index] = c.State;
        }
    }

    /// <summary>May unit i's weapons pick targets on their own this tick?</summary>
    public static bool AutoTargetAllowed(SimWorld w, int i) => w.Units.FireStates[i] switch
    {
        FireState.FireAtWill => true,
        FireState.ReturnFire => w.Tick - w.Units.LastHitTick[i] <= ReturnFireTicks,
        _ => false,
    };

    /// <summary>Is unit t being captured right now by <paramref name="army"/> or one of its allies?</summary>
    public static bool CapturedByFriend(SimWorld w, int army, int t)
    {
        ref readonly var b = ref w.Units.Builder[t];
        int captor = b.CaptureArmy - 1;
        if (captor < 0 || w.Tick - b.CaptureTick > CaptureHoldTicks) return false;
        return !w.IsEnemy(army, captor);
    }
}

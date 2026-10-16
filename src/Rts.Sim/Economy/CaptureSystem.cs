using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// SupCom capture: engineers/commanders take over enemy units and structures (<see cref="Orders.OrderType.Capture"/>,
/// executed by <see cref="ConstructionSystem"/>; Guard-assisting engineers join a capture). SEQUENTIAL.
/// <list type="bullet">
/// <item>Valid targets: active, complete, ENEMY, non-commander units the captor's army can see
///   (<see cref="Intel.IntelService.IsVisible"/>), not carried, not carrying a commander. The captor moves into build
///   range first.</item>
/// <item>Time = target BuildTime / captor BuildRate / 2 seconds; cost = the target's BuildCostEnergy (no mass),
///   paid through the flow economy like construction (<see cref="BuilderMode.Capture"/>: request energy at T, apply
///   progress at T+1 × <see cref="EconState.Efficiency"/>), so an energy stall slows it down.</item>
/// <item>Progress lives on the TARGET (<see cref="BuilderState.CaptureProgress"/>): several captors add up (with a
///   per-tick reservation so they never overshoot). It resets when nobody worked on it for <see cref="ResetTicks"/>.</item>
/// <item>At 100% the unit (with its cargo) changes army (<see cref="UnitTransfer.Transfer"/>) and an <see cref="EffectKind.Captured"/>
///   event is raised; the captors' orders then complete.</item>
/// </list>
/// </summary>
public static class CaptureSystem
{
    /// <summary>Capture is this many times faster than building the target with the same build power.</summary>
    public const float SpeedFactor = 2f;
    /// <summary>A capture nobody worked on for this long (2 s) starts again from zero.</summary>
    public const int ResetTicks = 2 * SimWorld.TicksPerSecond;
    private const float CompleteEpsilon = 1e-4f;

    /// <summary>Can unit <paramref name="captor"/> (a builder) capture unit <paramref name="t"/> right now?</summary>
    public static bool CanCapture(SimWorld w, int captor, int t)
    {
        var u = w.Units;
        if (t == captor || !u.IsActive(t) || !u.IsComplete(t)) return false;
        int army = u.Army[captor];
        if (!w.IsEnemy(army, u.Army[t])) return false;
        var tbp = w.BpOf(t);
        if (tbp.Has(UnitCategory.Commander) || tbp.Economy.BuildTime <= 0) return false;
        if ((u.Flags[t] & UnitFlags.Carried) != 0 || UnitTransfer.HoldsCommander(w, t)) return false; // inside a carrier / carrying a commander
        return w.Intel.IsVisible(army, t);
    }

    /// <summary>True if some captor worked on unit t recently (its progress is not stale).</summary>
    public static bool IsBeingCaptured(SimWorld w, int t)
    {
        ref readonly var b = ref w.Units.Builder[t];
        return b.CaptureProgress > 0 && w.Tick - b.CaptureTick <= ResetTicks;
    }

    /// <summary>0..1 capture progress of unit t (0 when nobody is capturing it).</summary>
    public static float Progress(SimWorld w, int t) => IsBeingCaptured(w, t) ? w.Units.Builder[t].CaptureProgress : 0;

    /// <summary>Seconds a builder of this blueprint needs alone to capture the target blueprint.</summary>
    public static float CaptureTime(UnitBlueprint captor, UnitBlueprint target) =>
        target.Economy.BuildTime / MathF.Max(1e-3f, captor.Economy.BuildRate) / SpeedFactor;

    /// <summary>Captor i requests capture work on t for this tick (from <see cref="ConstructionSystem.Work"/>).
    /// Capped by the remaining progress minus what other captors reserved this tick.</summary>
    internal static void RequestWork(SimWorld w, int i, int t, ref BuilderState b)
    {
        var u = w.Units;
        ref var tb = ref u.Builder[t];
        if (tb.CaptureProgress > 0 && w.Tick - tb.CaptureTick > ResetTicks) tb.CaptureProgress = 0; // abandoned
        var tbp = w.BpOf(t);
        float step = w.BpOf(i).Economy.BuildRate * SpeedFactor / tbp.Economy.BuildTime * SimWorld.Dt;
        float reserved = tb.CaptureTick == w.Tick ? tb.CaptureReserved : 0;
        float work = MathF.Min(step, 1f - tb.CaptureProgress - reserved);
        tb.CaptureTick = w.Tick; // capture activity (also while energy-stalled)
        tb.CaptureArmy = u.Army[i] + 1; // friendly units hold fire on it (Combat/FireStates)
        tb.CaptureReserved = reserved + MathF.Max(0, work);
        if (work <= 0) return;
        b.PendingWork = work;
        u.Econ[i].EnergyRequest += tbp.Economy.BuildCostEnergy * work / SimWorld.Dt;
    }

    /// <summary>Paid capture work of captor i arrives (ConstructionSystem apply phase); completes at 100%.</summary>
    internal static void AddProgress(SimWorld w, int captor, int t, float delta)
    {
        var u = w.Units;
        if (!u.IsActive(t) || !w.IsEnemy(u.Army[captor], u.Army[t])) return; // already captured (or allied now)
        ref var tb = ref u.Builder[t];
        tb.CaptureProgress = MathF.Min(1f, tb.CaptureProgress + delta);
        if (tb.CaptureProgress >= 1f - CompleteEpsilon) Complete(w, t, u.Army[captor]);
    }

    /// <summary>Unit t now belongs to <paramref name="army"/> (capture finished). SEQUENTIAL.</summary>
    public static void Complete(SimWorld w, int t, int army)
    {
        var u = w.Units;
        if (!UnitTransfer.Transfer(w, t, army)) return;
        var bp = w.BpOf(t);
        w.Events.Effects.Add(new EffectEvent
        {
            Kind = EffectKind.Captured,
            Pos = DMath.To3(u.Pos[t], u.Elevation[t]),
            Size = MathF.Max(bp.Visual.Size.X, bp.Visual.Size.Z),
            Army = army,
            BlueprintIndex = bp.Index,
        });
    }
}

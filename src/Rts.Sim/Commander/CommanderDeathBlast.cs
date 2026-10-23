using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Commander;

/// <summary>A commander death explosion waiting for its delay (<see cref="CommanderDeathBlast.Run"/> at DueTick).</summary>
public struct PendingDeathBlast
{
    public int DueTick;
    public Vector2 Center;
    public int Army;
    /// <summary>The commander that died (no longer alive; kills are not credited).</summary>
    public UnitHandle Source;
    /// <summary>Its blueprint (for the blast data and the effect's BlueprintIndex).</summary>
    public int Bp;
}

/// <summary>Per-world state of <see cref="CommanderDeathBlast"/> (plain data, saved with the world).</summary>
public sealed class CommanderDeathBlastState
{
    private static readonly ConditionalWeakTable<SimWorld, CommanderDeathBlastState> Table = new();

    public static CommanderDeathBlastState Of(SimWorld w) => Table.GetValue(w, static _ => new CommanderDeathBlastState());
    public static bool TryGet(SimWorld w, [NotNullWhen(true)] out CommanderDeathBlastState? s) => Table.TryGetValue(w, out s);

    /// <summary>Blasts in scheduling order.</summary>
    public readonly List<PendingDeathBlast> Pending = new();
}

/// <summary>
/// SupCom commander death explosion (data: <see cref="UnitBlueprint.DeathBlast"/>). A complete unit with the block that
/// dies (not a silently removed one) schedules a blast <see cref="DeathBlastBlueprint.Delay"/> s later where it died;
/// <see cref="Run"/> (SEQUENTIAL, right after ExperimentalSystem) hands it to the strategic detonation queue
/// (Strategic.MissileSystem.QueueDetonation): applied by this tick's DamageSystem to every army, wrecks of the inner
/// ring vaporized one tick later, <see cref="EffectKind.Nuke"/> raised with Size = OuterRadius and
/// BlueprintIndex = the commander's blueprint (tells it apart from a missile, whose BlueprintIndex is the silo's).
/// </summary>
public static class CommanderDeathBlast
{
    /// <summary>Death hook (UnitLifecycle.OnDeath). Never applies damage here.</summary>
    public static void OnUnitDeath(SimWorld w, int i, UnitBlueprint bp)
    {
        var x = bp.DeathBlast;
        var u = w.Units;
        if (x == null || !u.IsComplete(i) || u.BuildFraction[i] <= 0) return; // unfinished or removed silently
        CommanderDeathBlastState.Of(w).Pending.Add(new PendingDeathBlast
        {
            DueTick = w.Tick + Math.Max(1, (int)MathF.Round(x.Delay / SimWorld.Dt)),
            Center = u.Pos[i],
            Army = u.Army[i],
            Source = u.HandleOf(i),
            Bp = bp.Index,
        });
    }

    public static void Run(SimWorld w)
    {
        if (!CommanderDeathBlastState.TryGet(w, out var s) || s.Pending.Count == 0) return;
        int n = 0;
        for (int k = 0; k < s.Pending.Count; k++)
        {
            var b = s.Pending[k];
            if (b.DueTick > w.Tick) { s.Pending[n++] = b; continue; }
            var x = w.Blueprints[b.Bp].DeathBlast;
            if (x == null) continue;
            Strategic.MissileSystem.QueueDetonation(w, b.Army, b.Source, b.Center,
                x.InnerRadius, x.InnerDamage, x.OuterRadius, x.OuterDamage, b.Bp, 0);
        }
        s.Pending.RemoveRange(n, s.Pending.Count - n);
    }

    /// <summary>Hash of the pending blasts (determinism checks).</summary>
    public static ulong Hash(SimWorld w)
    {
        if (!CommanderDeathBlastState.TryGet(w, out var s)) return 0;
        ulong h = 1469598103934665603UL;
        void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
        foreach (var b in s.Pending)
        {
            Mix((ulong)b.DueTick); Mix((ulong)b.Army); Mix((ulong)b.Bp);
            Mix(BitConverter.SingleToUInt32Bits(b.Center.X)); Mix(BitConverter.SingleToUInt32Bits(b.Center.Y));
        }
        return h;
    }
}

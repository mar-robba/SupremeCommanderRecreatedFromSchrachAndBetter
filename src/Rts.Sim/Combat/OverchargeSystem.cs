using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>Per-world state of <see cref="OverchargeSystem"/> (plain data, saved with the world).</summary>
public sealed class OverchargeState
{
    private static readonly ConditionalWeakTable<SimWorld, OverchargeState> Table = new();

    public static OverchargeState Of(SimWorld w) => Table.GetValue(w, static x => new OverchargeState(x));
    public static bool TryGet(SimWorld w, [NotNullWhen(true)] out OverchargeState? s) => Table.TryGetValue(w, out s);

    /// <summary>Registered units with an Overcharge weapon (spawn order). Dead units are pruned in Run.</summary>
    public readonly List<UnitHandle> Units = new();
    /// <summary>Energy taken from storage for the next Overcharge shot, per unit slot (0 = not charged).</summary>
    public readonly float[] Charge;
    /// <summary>Seconds until the next Overcharge shot may charge, per unit slot.</summary>
    public readonly float[] Cooldown;
    /// <summary>Ticks the current charge has been held without firing, per unit slot.</summary>
    public readonly int[] HeldTicks;

    private OverchargeState(SimWorld w)
    {
        Charge = new float[w.Units.Capacity];
        Cooldown = new float[w.Units.Capacity];
        HeldTicks = new int[w.Units.Capacity];
    }
}

/// <summary>
/// SupCom ACU Overcharge (data: <see cref="WeaponBlueprint.Overcharge"/>, toggle: <see cref="UnitFlags.Overcharge"/>
/// via <see cref="SetOverchargeCommand"/>).
/// <list type="bullet">
/// <item><see cref="Run"/> (SEQUENTIAL, right before TargetingSystem): an armed, complete, unstunned unit whose OC weapon
///   was engaging last tick, off reload, takes E = min(stored, MaxEnergy) from its army's storage when at least
///   MinEnergy is stored (Charge = E). A charge is refunded (clamped to storage) when disarmed, stunned or held
///   unused for more than <see cref="HoldRefundSeconds"/>; it is lost if the unit dies.</item>
/// <item><see cref="TryShot"/> (PARALLEL, WeaponSystem, own slots only): the next shot of that weapon spends the
///   charge: one projectile, damage = E × DamagePerEnergy, radius = DamageRadius, ProjectileFlags.Overcharge; reload.</item>
/// <item>Impact (<see cref="TagLastAreaDamage"/>, sequential): the area damage event carries the rule applied by
///   DamageSystem through <see cref="Scale"/>: × StructureDamageMult vs structures, capped at CommanderMaxDamage vs
///   commanders. Effects: <see cref="EffectKind.Overcharge"/> (muzzle) and <see cref="EffectKind.OverchargeHit"/>.</item>
/// </list>
/// </summary>
public static class OverchargeSystem
{
    /// <summary>A charge held this long without firing goes back to storage.</summary>
    public const float HoldRefundSeconds = 3f;
    private const int HoldRefundTicks = 30;
    private const float CooldownEps = 1e-4f;

    /// <summary>Index of the unit's Overcharge weapon (first weapon with an Overcharge block), -1 = none.</summary>
    public static int WeaponIndex(UnitBlueprint bp)
    {
        var ws = bp.Weapons;
        for (int k = 0; k < ws.Length; k++) if (ws[k].Overcharge != null) return k;
        return -1;
    }

    /// <summary>Spawn hook (UnitLifecycle.OnSpawn; re-run on in-place blueprint changes, so idempotent). Sequential.</summary>
    public static void InitUnit(SimWorld w, int i, UnitBlueprint bp)
    {
        bool needs = WeaponIndex(bp) >= 0;
        OverchargeState? s;
        if (needs) s = OverchargeState.Of(w);
        else if (!OverchargeState.TryGet(w, out s)) return;
        var h = w.Units.HandleOf(i);
        bool registered = s.Units.Contains(h);
        if (registered && needs) return; // enhancement swap: keep charge/reload
        if (registered) s.Units.Remove(h);
        s.Charge[i] = 0;
        s.Cooldown[i] = 0;
        s.HeldTicks[i] = 0;
        if (needs) s.Units.Add(h);
    }

    /// <summary>Toggle (CommandSystem). Only the army's own units with an Overcharge weapon get the flag.</summary>
    public static void Apply(SimWorld w, SetOverchargeCommand c)
    {
        if (c.Units == null) return;
        var u = w.Units;
        foreach (var h in c.Units)
        {
            if (!CommandSystem.Owns(w, c.Army, h) || WeaponIndex(w.BpOf(h.Index)) < 0) continue;
            if (c.On) u.Flags[h.Index] |= UnitFlags.Overcharge;
            else u.Flags[h.Index] &= ~UnitFlags.Overcharge; // a held charge is refunded by the next Run
        }
    }

    public static void Run(SimWorld w)
    {
        if (!OverchargeState.TryGet(w, out var s) || s.Units.Count == 0) return;
        var u = w.Units;
        int n = 0;
        for (int k = 0; k < s.Units.Count; k++)
            if (u.IsActive(s.Units[k])) s.Units[n++] = s.Units[k];
        s.Units.RemoveRange(n, s.Units.Count - n);

        var engaged = CombatState.Of(w).Engaged;
        foreach (var h in s.Units)
        {
            int i = h.Index;
            var bp = w.BpOf(i);
            int wi = WeaponIndex(bp);
            var eco = w.Armies[u.Army[i]].Eco;
            if (s.Cooldown[i] > 0) s.Cooldown[i] = MathF.Max(0, s.Cooldown[i] - SimWorld.Dt);
            bool armed = wi >= 0 && (u.Flags[i] & UnitFlags.Overcharge) != 0;
            bool stunned = (u.Flags[i] & UnitFlags.Stunned) != 0;
            if (s.Charge[i] > 0)
            {
                if (armed && !stunned && ++s.HeldTicks[i] <= HoldRefundTicks) continue;
                eco.Energy = MathF.Max(eco.Energy, MathF.Min(eco.EnergyMax, eco.Energy + s.Charge[i])); // refund, clamped to storage
                s.Charge[i] = 0;
                s.HeldTicks[i] = 0;
                continue;
            }
            if (!armed || stunned || !u.IsComplete(i) || s.Cooldown[i] > CooldownEps || engaged[i] == 0) continue;
            ref readonly var ws = ref u.WeaponsOf(i)[wi];
            if (!u.IsActive(ws.Target) && !ws.HasGroundTarget) continue; // engaged with another weapon only
            var oc = bp.Weapons[wi].Overcharge!;
            if (eco.Energy < oc.MinEnergy) continue;
            float e = MathF.Min(eco.Energy, oc.MaxEnergy);
            eco.Energy -= e;
            s.Charge[i] = e;
            s.HeldTicks[i] = 0;
        }
    }

    /// <summary>WeaponSystem (PARALLEL, touches only unit i's slots): spends unit i's charge on this shot.</summary>
    public static bool TryShot(SimWorld w, int i, OverchargeBlueprint oc, out float damage)
    {
        damage = 0;
        if (!OverchargeState.TryGet(w, out var s) || s.Charge[i] <= 0) return false;
        damage = s.Charge[i] * oc.DamagePerEnergy;
        s.Charge[i] = 0;
        s.HeldTicks[i] = 0;
        s.Cooldown[i] = oc.Reload;
        return true;
    }

    /// <summary>Energy held for unit i's next Overcharge shot (0 = none).</summary>
    public static float ChargeOf(SimWorld w, int i) => OverchargeState.TryGet(w, out var s) ? s.Charge[i] : 0;

    /// <summary>Seconds until unit i may charge its next Overcharge shot.</summary>
    public static float CooldownOf(SimWorld w, int i) => OverchargeState.TryGet(w, out var s) ? s.Cooldown[i] : 0;

    /// <summary>Sequential, right after an Overcharge shot added its area damage event: copies the shooter weapon's
    /// damage rule into that event and raises <see cref="EffectKind.OverchargeHit"/>.</summary>
    internal static void TagLastAreaDamage(SimWorld w, int bp, int weapon, Vector3 pos, float radius, int army)
    {
        var weapons = w.Blueprints[bp].Weapons;
        if ((uint)weapon >= (uint)weapons.Length || weapons[weapon].Overcharge is not { } oc) return;
        var list = CollectionsMarshal.AsSpan(w.Events.AreaDamage);
        if (list.Length == 0) return;
        ref var e = ref list[^1];
        e.Overcharge = true;
        e.OcStructureMult = oc.StructureDamageMult;
        e.OcCommanderCap = oc.CommanderMaxDamage;
        CombatFx.Add(w, EffectKind.OverchargeHit, pos, radius, army, bp, weapon);
    }

    /// <summary>DamageSystem: the Overcharge damage rule for unit j.</summary>
    public static float Scale(SimWorld w, int j, in AreaDamageEvent a, float amount)
    {
        var bp = w.BpOf(j);
        if (bp.IsStructure) amount *= a.OcStructureMult;
        if (bp.Has(UnitCategory.Commander)) amount = MathF.Min(amount, a.OcCommanderCap);
        return amount;
    }

    /// <summary>Hash of the Overcharge state (determinism checks).</summary>
    public static ulong Hash(SimWorld w)
    {
        if (!OverchargeState.TryGet(w, out var s)) return 0;
        ulong h = 1469598103934665603UL;
        void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
        foreach (var uh in s.Units)
        {
            int i = uh.Index;
            Mix((ulong)i);
            Mix(BitConverter.SingleToUInt32Bits(s.Charge[i]));
            Mix(BitConverter.SingleToUInt32Bits(s.Cooldown[i]));
            Mix((ulong)s.HeldTicks[i]);
        }
        return h;
    }
}

using System.Numerics;
using System.Runtime.CompilerServices;
using Rts.Sim.Data;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>
/// EMP stun (SupCom 1 Cybran), data-driven from <see cref="WeaponBlueprint"/>: <c>StunDuration</c> (s, &gt; 0 = the
/// weapon stuns), <c>StunRadius</c> (area around the impact; 0 = the weapon's DamageRadius, and with neither only the
/// unit hit), <c>StunMaxTech</c> (units above this tech are immune; default 3, so experimentals are immune).
/// <list type="bullet">
/// <item>Applied in the sequential impact phase (ProjectileSystem.Apply, beams in WeaponSystem.Emit) to ENEMY units
/// on the weapon's target layers; commanders and units in the air are immune; an enemy shield dome protects the
/// units inside from an area stun fired from outside (same rule as area damage). A new stun extends a running one
/// (until = max).</item>
/// <item>A stunned unit has <see cref="UnitFlags.Stunned"/>: it doesn't move (MovementSystem), acquire/fire
/// (TargetingSystem, WeaponSystem, missile defense, silos) or build/assist/repair/reclaim/upgrade/produce
/// (ConstructionSystem, FactorySystem, UpgradeSystem, SiloSystem, mobile factories). Shields, intel, economy and
/// stealth keep working.</item>
/// <item><see cref="Expire"/> (sequential, start of the tick) clears the flag when the time is up. Per-world state
/// (until-tick + generation per slot) lives in <see cref="EmpStunState"/>, created on the first stun: zero cost in
/// games without EMP weapons (one table lookup per tick).</item>
/// </list>
/// </summary>
public static class EmpStun
{
    /// <summary>Is unit slot <paramref name="unit"/> stunned right now? Thread-safe read (a flag bit).</summary>
    public static bool IsStunned(SimWorld w, int unit) => (w.Units.Flags[unit] & UnitFlags.Stunned) != 0;

    /// <summary>MovementSystem (parallel, own slot only): a stunned unit stands still.</summary>
    internal static bool HoldStill(UnitStore u, int i)
    {
        u.Move[i].Velocity = Vector2.Zero;
        return true;
    }

    /// <summary>Remaining stun time of a unit in seconds (0 = not stunned).</summary>
    public static float SecondsLeft(SimWorld w, int unit)
    {
        if (!IsStunned(w, unit) || EmpStunState.Peek(w) is not { } s) return 0;
        return Math.Max(0, s.Until[unit] - w.Tick) * SimWorld.Dt;
    }

    /// <summary>Can this blueprint be stunned by a weapon with the given StunMaxTech? (Commanders never.)</summary>
    public static bool CanBeStunned(UnitBlueprint bp, int maxTech) => !bp.Has(UnitCategory.Commander) && bp.Tech <= maxTech;

    /// <summary>
    /// Stuns unit <paramref name="unit"/> for <paramref name="seconds"/> (extends a running stun; no eligibility
    /// checks: tests, sandbox). SEQUENTIAL code only.
    /// </summary>
    public static void Stun(SimWorld w, int unit, float seconds)
    {
        var u = w.Units;
        if (!u.IsActive(unit) || seconds <= 0) return;
        var s = EmpStunState.Of(w);
        int until = w.Tick + Math.Max(1, (int)MathF.Round(seconds * SimWorld.TicksPerSecond));
        if ((u.Flags[unit] & UnitFlags.Stunned) != 0 && s.Gen[unit] == u.Gen[unit])
        {
            if (until > s.Until[unit]) s.Until[unit] = until;
            return;
        }
        u.Flags[unit] |= UnitFlags.Stunned;
        s.Until[unit] = until;
        s.Gen[unit] = u.Gen[unit];
        s.Active.Add(unit);
    }

    /// <summary>
    /// Impact of a shot of weapon <paramref name="weapon"/> of blueprint <paramref name="bp"/> fired by
    /// <paramref name="army"/> at <paramref name="center"/> (<paramref name="hitUnit"/> = the unit hit directly, -1 =
    /// none). Stuns the eligible enemy units. SEQUENTIAL (impact phase).
    /// </summary>
    internal static void OnImpact(SimWorld w, int bp, int weapon, int army, Vector2 center, int hitUnit)
    {
        var weapons = w.Blueprints[bp].Weapons;
        if ((uint)weapon >= (uint)weapons.Length) return;
        var wb = weapons[weapon];
        if (wb.StunDuration <= 0) return;
        float radius = wb.StunRadius > 0 ? wb.StunRadius : wb.DamageRadius;
        if (radius > 0) StunArea(w, army, center, radius, wb.StunDuration, wb.StunMaxTech, wb.TargetMask);
        else if (hitUnit >= 0) TryStun(w, army, hitUnit, wb.StunDuration, wb.StunMaxTech, wb.TargetMask);
    }

    /// <summary>
    /// Area EMP by <paramref name="army"/>: stuns the eligible enemy units within <paramref name="radius"/> (+ their
    /// radius) of <paramref name="center"/> on <paramref name="layers"/> (None = all) for <paramref name="seconds"/>.
    /// Units inside an enemy shield dome are protected from a center outside it. SEQUENTIAL code only.
    /// </summary>
    public static void StunArea(SimWorld w, int army, Vector2 center, float radius, float seconds, int maxTech = 3, LayerMask layers = LayerMask.None)
    {
        var u = w.Units;
        var list = EmpStunState.Of(w).Scratch;
        list.Clear();
        w.Grid.QueryRadius(u, center, radius + 2f, list);
        foreach (int j in list)
        {
            if (!u.IsActive(j)) continue;
            float r = radius + w.BpOf(j).Radius;
            if (Vector2.DistanceSquared(u.Pos[j], center) > r * r) continue;
            if (Strategic.ShieldSystem.ProtectsFromArea(w, army, center, j)) continue; // stun outside an enemy dome
            TryStun(w, army, j, seconds, maxTech, layers);
        }
        list.Clear();
    }

    /// <summary>Stuns unit <paramref name="j"/> if it is an enemy of <paramref name="army"/>, on the map, not in the
    /// air, on <paramref name="layers"/> (None = all) and <see cref="CanBeStunned"/>. Returns whether it was stunned.
    /// SEQUENTIAL code only.</summary>
    public static bool TryStun(SimWorld w, int army, int j, float seconds, int maxTech = 3, LayerMask layers = LayerMask.None)
    {
        var u = w.Units;
        if (!u.IsActive(j) || !w.IsEnemy(army, u.Army[j]) || (u.Flags[j] & UnitFlags.Carried) != 0) return false;
        if (!CanBeStunned(w.BpOf(j), maxTech)) return false;
        var layer = OrderSystem.LayerOf(w, j);
        if (layer == LayerMask.Air) return false; // aircraft can't stop in mid-air: immune
        if (layers != LayerMask.None && (layer & layers) == 0) return false;
        Stun(w, j, seconds);
        return true;
    }

    /// <summary>Start of the tick (sequential): ends the stuns whose time is up, forgets dead/reused slots.</summary>
    public static void Expire(SimWorld w)
    {
        if (EmpStunState.Peek(w) is not { } s || s.Active.Count == 0) return;
        var u = w.Units;
        var list = s.Active;
        int n = 0;
        for (int k = 0; k < list.Count; k++)
        {
            int i = list[k];
            bool same = u.Alive[i] && u.Gen[i] == s.Gen[i];
            if (same && s.Until[i] > w.Tick && (u.Flags[i] & UnitFlags.Stunned) != 0) { list[n++] = i; continue; }
            if (same) u.Flags[i] &= ~UnitFlags.Stunned;
            s.Gen[i] = 0;
        }
        if (n < list.Count) list.RemoveRange(n, list.Count - n);
    }
}

/// <summary>Per-world EMP stun state (see <see cref="EmpStun"/>): plain arrays + the list of stunned slots.</summary>
public sealed class EmpStunState
{
    private static readonly ConditionalWeakTable<SimWorld, EmpStunState> Table = new();

    public static EmpStunState Of(SimWorld w) => Table.GetValue(w, static x => new EmpStunState(x.Units.Capacity));

    internal static EmpStunState? Peek(SimWorld w) => Table.TryGetValue(w, out var s) ? s : null;

    /// <summary>Per slot: tick at which the stun ends (valid while Gen matches the unit's generation).</summary>
    public readonly int[] Until;
    /// <summary>Per slot: generation of the stunned unit (0 = none).</summary>
    public readonly int[] Gen;
    /// <summary>Stunned slots (stun order, compacted by Expire: deterministic).</summary>
    public readonly List<int> Active = new();
    [Persistence.NoSave] private List<int>? _scratch;
    /// <summary>Scratch for area queries.</summary>
    internal List<int> Scratch => _scratch ??= new();

    private EmpStunState(int capacity)
    {
        Until = new int[capacity];
        Gen = new int[capacity];
    }
}

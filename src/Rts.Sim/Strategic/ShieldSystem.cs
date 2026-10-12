using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Strategic;

/// <summary>
/// Shield domes (SupCom bubbles). Sequential update each tick (after movement, before combat):
/// <list type="bullet">
/// <item>A shield is up while its unit is complete, not paused and powered: units with energy upkeep need
///   <c>Econ.Efficiency</c> ≥ <see cref="MinPowerEfficiency"/> (an energy stall collapses every shield).</item>
/// <item>Up: regenerates RegenRate HP/s, RegenDelay seconds after the last hit. Damage to 0 HP (or a stall, or a nuke)
///   collapses it; it then needs RechargeTime powered seconds to come back up at full HP.</item>
/// <item>The domes of up shields go into <see cref="ShieldIndex"/>; ProjectileSystem asks it for the first enemy dome
///   a projectile segment enters from outside (the projectile is absorbed there and damages the shield; shots fired
///   from inside pass out; overlapping shields: the first surface crossed takes the hit). Area damage of explosions
///   outside a dome doesn't reach units inside it (<see cref="ProtectsFromArea"/>). Instant weapons (beams) from
///   outside are absorbed by the first enemy dome on the muzzle→target line (<see cref="TryBlockInstant"/>).
///   Strategic missiles (nukes) ignore shields and knock down every shield in their outer blast radius.</item>
/// </list>
/// </summary>
public static class ShieldSystem
{
    /// <summary>Shields with energy upkeep collapse when their unit's economy efficiency is below this.</summary>
    public const float MinPowerEfficiency = 0.95f;
    /// <summary>Fraction of RechargeTime a freshly built shield needs before it first comes up.</summary>
    public const float BootFraction = 0.25f;

    internal static void InitUnit(StrategicState s, SimWorld w, int i, UnitBlueprint bp)
    {
        ref var sh = ref s.Shield[i];
        sh = default;
        sh.LastHitTick = -1000;
        if (bp.Shield == null) return;
        if (w.Units.IsComplete(i)) { sh.Up = true; sh.Hp = bp.Shield.MaxHealth; }
        else sh.RechargeLeft = bp.Shield.RechargeTime * BootFraction;
    }

    public static Vector3 CenterOf(SimWorld w, int i, ShieldBlueprint sb) =>
        new(w.Units.Pos[i].X, w.Units.Elevation[i] + sb.CenterHeight, w.Units.Pos[i].Y);

    /// <summary>Updates shield states and rebuilds the shield index. Sequential.</summary>
    internal static void Update(StrategicState s, SimWorld w)
    {
        var u = w.Units;
        const float dt = SimWorld.Dt;
        s.Index.Clear();
        foreach (var h in s.Units)
        {
            int i = h.Index;
            if (!u.IsActive(h)) continue;
            var bp = w.BpOf(i);
            var sb = bp.Shield;
            if (sb == null) continue;
            ref var sh = ref s.Shield[i];
            bool active = (u.Flags[i] & (UnitFlags.UnderConstruction | UnitFlags.Paused | UnitFlags.Dead)) == 0;
            // An up shield pays its upkeep and reads its own efficiency; a collapsed one pays nothing (its
            // Econ.Efficiency is a meaningless 1), so its recharge reads the army's energy health instead.
            bool powered = active && (bp.Economy.MaintenanceEnergy <= 0
                || (sh.Up ? u.Econ[i].Efficiency >= MinPowerEfficiency
                          : ArmyCanPower(w, u.Army[i], bp.Economy.MaintenanceEnergy * (1f - u.Econ[i].AdjEnergyUse))));
            if (sh.Up)
            {
                if (!powered) Collapse(w, i, sb, ref sh);
                else if (sh.RegenDelay > 0) sh.RegenDelay -= dt;
                else sh.Hp = MathF.Min(sb.MaxHealth, sh.Hp + sb.RegenRate * dt);
            }
            else if (powered)
            {
                sh.RechargeLeft -= dt;
                if (sh.RechargeLeft <= 0)
                {
                    sh.Up = true;
                    sh.Hp = sb.MaxHealth;
                    sh.RechargeLeft = 0;
                    sh.RegenDelay = 0;
                    CombatFx.Add(w, EffectKind.ShieldUp, CenterOf(w, i, sb), sb.Radius, u.Army[i], bp.Index);
                }
            }
            if (sh.Up)
                s.Index.Add(new ShieldEntry
                {
                    Unit = i, Army = u.Army[i], Center = CenterOf(w, i, sb), Radius = sb.Radius,
                    InvHeightScale = 1f / MathF.Max(0.05f, sb.HeightScale),
                });
        }
        s.Index.Build();
    }

    /// <summary>
    /// Recharge condition of a collapsed shield with upkeep: its army is not stalling on energy
    /// (<see cref="Army"/> EnergyEfficiency ≥ <see cref="MinPowerEfficiency"/>) and the stored energy plus this tick's
    /// net income would still cover one tick of the shield's own upkeep (else it would come up and collapse at once,
    /// e.g. an army with no power at all has no demand and so an efficiency of 1).
    /// </summary>
    public static bool ArmyCanPower(SimWorld w, int army, float upkeep)
    {
        var eco = w.Armies[army].Eco;
        if (eco.EnergyEfficiency < MinPowerEfficiency) return false;
        return eco.Energy + (eco.EnergyIncome - eco.EnergyDemand - upkeep) * SimWorld.Dt >= 0;
    }

    private static void Collapse(SimWorld w, int i, ShieldBlueprint sb, ref ShieldState sh)
    {
        sh.Up = false;
        sh.Hp = 0;
        sh.RegenDelay = 0;
        sh.RechargeLeft = sb.RechargeTime;
        CombatFx.Add(w, EffectKind.ShieldDown, CenterOf(w, i, sb), sb.Radius, w.Units.Army[i], w.Units.Bp[i]);
    }

    /// <summary>Knocks a shield down (nuke blast, tests). Sequential.</summary>
    public static void KnockDown(SimWorld w, int unit)
    {
        var s = StrategicState.Of(w);
        var sb = w.BpOf(unit).Shield;
        if (sb == null || !s.Shield[unit].Up) return;
        Collapse(w, unit, sb, ref s.Shield[unit]);
    }

    public static bool IsUp(SimWorld w, int unit) =>
        w.Units.IsActive(unit) && w.BpOf(unit).Shield != null && StrategicState.Of(w).Shield[unit].Up;

    /// <summary>Applies damage to a shield (sequential). Returns false if the shield was already down.</summary>
    public static bool Damage(SimWorld w, int unit, float amount, Vector3 hitPos, int shooterBp, int weapon)
    {
        var s = StrategicState.Of(w);
        if (!w.Units.IsActive(unit)) return false;
        var sb = w.BpOf(unit).Shield;
        ref var sh = ref s.Shield[unit];
        if (sb == null || !sh.Up) return false;
        sh.Hp -= MathF.Max(0, amount);
        sh.RegenDelay = sb.RegenDelay;
        sh.LastHitTick = w.Tick;
        w.Units.LastHitTick[unit] = w.Tick;
        CombatFx.Add(w, EffectKind.ShieldHit, hitPos, Math.Clamp(amount / 150f, 0.3f, 4f), w.Units.Army[unit], shooterBp, weapon);
        if (sh.Hp <= 0) Collapse(w, unit, sb, ref sh);
        return true;
    }

    /// <summary>Entry index of the first enemy (to <paramref name="army"/>) dome entered by segment a→b, or -1.</summary>
    public static int FirstEntry(StrategicState s, int army, Vector3 a, Vector3 b, out float t)
    {
        if (s.Index.Count == 0) { t = 2f; return -1; }
        return s.Index.FirstEntry(s.EnemyMask[army], a, b, s.Shield, out t);
    }

    /// <summary>
    /// Instant weapons (beams): absorbed by the first up enemy dome that the muzzle→target line enters from outside.
    /// Returns true if the shot was absorbed (shield damaged, effect emitted). Sequential (WeaponSystem.Emit).
    /// </summary>
    public static bool TryBlockInstant(SimWorld w, int army, Vector3 muzzle, Vector3 target, float damage, int shooterBp, int weapon)
    {
        var s = StrategicState.Of(w);
        int e = FirstEntry(s, army, muzzle, target, out float t);
        if (e < 0) return false;
        int unit = s.Index.Entries[e].Unit;
        return Damage(w, unit, damage, Vector3.Lerp(muzzle, target, t), shooterBp, weapon);
    }

    /// <summary>
    /// Area damage filter (DamageSystem): true if unit j is inside an up dome that is enemy to the source army and the
    /// blast center is outside that dome's footprint.
    /// </summary>
    public static bool ProtectsFromArea(SimWorld w, int sourceArmy, Vector2 blast, int j)
    {
        var s = StrategicState.Of(w);
        if (s.Index.Count == 0 || (uint)sourceArmy >= (uint)s.EnemyMask.Length) return false;
        var u = w.Units;
        var victim = new Vector3(u.Pos[j].X, u.Elevation[j] + 0.3f, u.Pos[j].Y);
        // A dome only shields what its owner side protects: domes of the source army's enemies.
        return s.Index.Protects(s.EnemyMask[sourceArmy], victim, blast, s.Shield);
    }
}

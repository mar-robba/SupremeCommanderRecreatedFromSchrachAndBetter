using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>How a weapon delivers damage (derived from the weapon blueprint).</summary>
public enum WeaponKind : byte
{
    /// <summary>Straight-flying projectile (MuzzleVelocity &gt; 0, not ballistic).</summary>
    Direct,
    /// <summary>Straight projectile that steers toward its target (ProjectileVisual "missile").</summary>
    Homing,
    /// <summary>Artillery shell on a gravity arc (Ballistic, MuzzleVelocity &gt; 0).</summary>
    Ballistic,
    /// <summary>Bomb released with the shooter's velocity (Ballistic, MuzzleVelocity 0): bombers.</summary>
    Bomb,
    /// <summary>Instant hit (MuzzleVelocity 0, not ballistic): beams/lasers.</summary>
    Beam,
    /// <summary>Silo missile / missile-defense interceptor: never fires automatically (Rts.Sim/Strategic drives it).</summary>
    Manual,
    /// <summary>Torpedo (ProjectileVisual "torpedo"): runs under the water surface toward a Water/Sub target, dies
    /// on shore; dropped torpedoes fall into the water first (Naval/Torpedoes.cs).</summary>
    Torpedo,
    /// <summary>Torpedo defense (ProjectileVisual "antitorpedo", no target layers): never targets units; intercepts
    /// enemy torpedoes within Range at RateOfFire (Naval/Torpedoes.cs).</summary>
    AntiTorpedo,
}

/// <summary>Precomputed per-weapon combat data (immutable, thread-safe).</summary>
public readonly struct WeaponInfo
{
    public readonly WeaponKind Kind;
    /// <summary>Ballistic weapons of indirect-fire units lob shells on the high arc.</summary>
    public readonly bool HighArc;
    /// <summary>Area weapon fired straight (flak): bursts near air targets / at the end of its timed fuse.</summary>
    public readonly bool ProximityFuse;
    /// <summary>Horizontal acquisition range. Equals Range, except bombs (Range + release distance).</summary>
    public readonly float AcquireRange;
    /// <summary>Seconds between shots (1 / RateOfFire).</summary>
    public readonly float Period;
    /// <summary>Turret yaw rate in radians per second.</summary>
    public readonly float TurnRate;
    /// <summary>Max yaw error (radians) to fire.</summary>
    public readonly float Tolerance;

    public WeaponInfo(WeaponKind kind, bool highArc, bool proximityFuse, float acquireRange, float period, float turnRate, float tolerance)
    {
        Kind = kind; HighArc = highArc; ProximityFuse = proximityFuse; AcquireRange = acquireRange;
        Period = period; TurnRate = turnRate; Tolerance = tolerance;
    }
}

/// <summary>Precomputed per-blueprint combat data (immutable, thread-safe).</summary>
public sealed class UnitCombatInfo
{
    public WeaponInfo[] Weapons = [];
    /// <summary>Union of the layers this unit's weapons can hit ("can it shoot back at me?").</summary>
    public LayerMask ThreatMask;
    public bool Armed;
    public bool IsStructure;
    /// <summary>Indirect-fire units (artillery) prefer armed structures over mobile units.</summary>
    public bool PrefersStructures;
    /// <summary>Radius added to weapon ranges when this unit is the target.</summary>
    public float TargetRadius;
    /// <summary>Radius of the collision sphere used by projectiles.</summary>
    public float HitRadius;
    /// <summary>Height above the unit's Elevation of its aim/collision center.</summary>
    public float HitHeight;
    /// <summary>Height above the unit's Elevation where its projectiles spawn.</summary>
    public float MuzzleHeight;
}

/// <summary>Combat data derived once from the BlueprintDb (indexed by blueprint index).</summary>
public sealed class CombatCatalog
{
    public readonly UnitCombatInfo[] Units;
    /// <summary>Largest TargetRadius/HitRadius of any blueprint (query margins).</summary>
    public readonly float MaxRadius;

    public CombatCatalog(BlueprintDb db)
    {
        Units = new UnitCombatInfo[db.Count];
        float maxR = 0.5f;
        for (int b = 0; b < db.Count; b++)
        {
            var bp = db[b];
            int nw = Math.Min(bp.Weapons.Length, UnitStore.MaxWeapons);
            var info = new UnitCombatInfo
            {
                Weapons = new WeaponInfo[nw],
                Armed = nw > 0,
                IsStructure = bp.IsStructure,
                PrefersStructures = bp.HasAny(UnitCategory.IndirectFire | UnitCategory.Artillery),
                TargetRadius = bp.Radius,
                HitRadius = bp.Radius + 0.15f,
                HitHeight = bp.Visual.Size.Y * 0.5f,
                MuzzleHeight = bp.Visual.Size.Y * 0.6f,
            };
            for (int k = 0; k < nw; k++)
            {
                var wb = bp.Weapons[k];
                var kind = Naval.Torpedoes.KindOf(wb) is WeaponKind nk ? nk
                    : wb.MuzzleVelocity <= 0
                    ? (wb.Ballistic ? WeaponKind.Bomb : WeaponKind.Beam)
                    : wb.Ballistic ? WeaponKind.Ballistic
                    : string.Equals(wb.ProjectileVisual, "missile", StringComparison.OrdinalIgnoreCase) ? WeaponKind.Homing
                    : WeaponKind.Direct;
                if (Strategic.StrategicSystem.IsManualWeapon(bp, k)) kind = WeaponKind.Manual;
                float acquire = wb.Range;
                if (kind == WeaponKind.Bomb)
                {
                    // A bomb falls for sqrt(2h/g) seconds and keeps the plane's velocity: the plane must be
                    // able to "see" targets that far ahead to release on time.
                    float alt = MathF.Max(1f, bp.Movement.Altitude);
                    acquire += bp.Movement.MaxSpeed * MathF.Sqrt(2f * alt / Ballistics.Gravity);
                }
                if (kind != WeaponKind.Manual || wb.Damage > 0) info.ThreatMask |= wb.TargetMask; // interceptors threaten no unit
                info.Weapons[k] = new WeaponInfo(
                    kind,
                    highArc: info.PrefersStructures,
                    proximityFuse: wb.DamageRadius > 0 && kind is WeaponKind.Direct or WeaponKind.Homing,
                    acquireRange: acquire,
                    period: wb.RateOfFire > 0 ? 1f / wb.RateOfFire : 1e6f,
                    turnRate: MathF.Max(1f, wb.TurretTurnRate) * DMath.Deg2Rad,
                    tolerance: MathF.Max(0.1f, wb.FiringTolerance) * DMath.Deg2Rad);
            }
            maxR = MathF.Max(maxR, info.HitRadius);
            Units[b] = info;
        }
        MaxRadius = maxR;
    }
}

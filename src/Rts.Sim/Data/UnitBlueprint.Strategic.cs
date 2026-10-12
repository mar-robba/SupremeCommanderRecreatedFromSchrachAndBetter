namespace Rts.Sim.Data;

/// <summary>Strategic / defensive modules of a blueprint (shields, missile silos, missile defense). See Rts.Sim/Strategic.</summary>
public sealed partial class UnitBlueprint
{
    /// <summary>Shield generator (dome around the unit). null = none.</summary>
    public ShieldBlueprint? Shield;
    /// <summary>Missile silo: builds a stockpile of missiles from the economy, launched with a Launch order. null = none.</summary>
    public SiloBlueprint? Silo;
    /// <summary>Shoots down enemy strategic missiles passing within its radius. null = none.</summary>
    public MissileDefenseBlueprint? MissileDefense;
}

/// <summary>
/// Shield dome (SupCom bubble). Projectiles from enemies entering the dome from outside hit the shield instead;
/// shots fired from inside pass. Energy upkeep is <c>Economy.MaintenanceEnergy</c>: the shield collapses when the
/// army stalls on energy (unit economy efficiency below <see cref="Strategic.ShieldSystem.MinPowerEfficiency"/>).
/// </summary>
public sealed class ShieldBlueprint
{
    /// <summary>Horizontal radius of the dome (world units).</summary>
    public float Radius = 10;
    /// <summary>Vertical semi-axis = Radius × HeightScale (flattened dome: aircraft fly over it).</summary>
    public float HeightScale = 0.7f;
    /// <summary>Height of the dome center above the unit's base.</summary>
    public float CenterHeight;
    public float MaxHealth = 1000;
    /// <summary>Shield HP regenerated per second while up (after <see cref="RegenDelay"/> without hits).</summary>
    public float RegenRate = 10;
    /// <summary>Seconds after a hit before regeneration resumes.</summary>
    public float RegenDelay = 1.5f;
    /// <summary>Seconds (powered) to come back up at full HP after being knocked down or after an energy stall.</summary>
    public float RechargeTime = 20;
}

public enum SiloKind : byte
{
    /// <summary>Tactical missile launcher: cruise missile at a ground target within the weapon's Range.</summary>
    Tactical,
    /// <summary>Strategic (nuclear) missile: high-arc flight to any point of the map, two-radius blast.</summary>
    Strategic,
    /// <summary>Anti-nuke: the stockpile feeds the unit's <see cref="MissileDefenseBlueprint"/> interceptors.</summary>
    AntiNuke,
}

/// <summary>
/// Missile silo. Missiles are built one at a time from the economy (pay-first like construction: the silo requests
/// MissileCost / MissileBuildTime per second; progress scales with the unit's economy efficiency) up to MaxStorage.
/// The missile itself is described by <c>Weapons[WeaponIndex]</c> (Range, MinRange, Damage, DamageRadius, Spread,
/// MuzzleVelocity = cruise speed, ProjectileVisual); that weapon never fires automatically.
/// </summary>
public sealed class SiloBlueprint
{
    public SiloKind Kind;
    public int WeaponIndex;
    public int MaxStorage = 1;
    public float MissileCostMass;
    public float MissileCostEnergy;
    /// <summary>Seconds to build one missile at full economy.</summary>
    public float MissileBuildTime = 30;
    /// <summary>Apex/cruise altitude above the launch/target terrain (0 = derived from the distance).</summary>
    public float CruiseAltitude;
    /// <summary>Strategic missiles: outer blast ring (the weapon's Damage/DamageRadius is the inner ring).</summary>
    public float OuterDamage;
    public float OuterRadius;
}

/// <summary>What a missile defense intercepts.</summary>
public enum InterceptKind : byte { Tactical, Strategic }

/// <summary>
/// Missile defense (TMD / anti-nuke). Fires interceptors (projectiles described by <c>Weapons[WeaponIndex]</c>,
/// MuzzleVelocity = interceptor speed) at enemy missiles of <see cref="Intercepts"/> kind within <see cref="Radius"/>
/// (horizontal), at most <see cref="RateOfFire"/> per second, never two interceptors at the same missile.
/// With <see cref="UsesStockpile"/> each interceptor consumes one missile of the unit's silo (anti-nuke).
/// </summary>
public sealed class MissileDefenseBlueprint
{
    public InterceptKind Intercepts;
    public float Radius = 30;
    public float RateOfFire = 0.5f;
    public int WeaponIndex;
    public bool UsesStockpile;
}

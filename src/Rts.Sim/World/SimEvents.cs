using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;

namespace Rts.Sim.World;

public struct DamageEvent
{
    public UnitHandle Target;
    public UnitHandle Source;
    public int SourceArmy;
    public float Amount;
}

/// <summary>Area damage applied by DamageSystem to all units in Radius (linear falloff optional).</summary>
public struct AreaDamageEvent
{
    public Vector2 Center;
    public float Radius;
    public float Amount;
    public UnitHandle Source;
    public int SourceArmy;
    /// <summary>Only units currently on these layers are damaged (None = all layers). E.g. flak bursts only
    /// hurt aircraft, bombs exploding on the ground don't hurt planes flying overhead.</summary>
    public LayerMask Layers;
    /// <summary>ACU Overcharge shot (Combat/OverchargeSystem.Scale): damage vs structures × OcStructureMult, vs
    /// commanders capped at OcCommanderCap.</summary>
    public bool Overcharge;
    public float OcStructureMult;
    public float OcCommanderCap;
}

public enum EffectKind : byte
{
    MuzzleFlash,
    Impact,
    Explosion,
    UnitDeath,
    BuildStarted,
    BuildComplete,
    Reclaim,
    // ---- Strategic (Rts.Sim/Strategic) ----
    /// <summary>A projectile hit a shield dome at Pos (Army = shield owner, Size = damage scale, BlueprintIndex/WeaponIndex = shooter's weapon).</summary>
    ShieldHit,
    /// <summary>A shield collapsed (knocked down or energy stall). Pos = dome center, Size = radius, BlueprintIndex = generator.</summary>
    ShieldDown,
    /// <summary>A shield came back up. Pos = dome center, Size = radius, BlueprintIndex = generator.</summary>
    ShieldUp,
    /// <summary>A silo launched a missile (Pos = launch point, BlueprintIndex/WeaponIndex = silo weapon).</summary>
    MissileLaunch,
    /// <summary>A missile was shot down by an interceptor at Pos (BlueprintIndex/WeaponIndex = the destroyed missile's weapon).</summary>
    MissileIntercepted,
    /// <summary>Strategic (nuclear) detonation at Pos (ground), Size = outer blast radius. Raised by the strategic
    /// missile (MissileSystem.Detonate, also for sandbox nukes) and by the sandbox instant blast; always visible
    /// through fog. One client visual (NukeFx) and one sound.</summary>
    Nuke,
    /// <summary>A unit was captured (Economy/CaptureSystem): Pos = unit, Army = NEW owner, BlueprintIndex = unit.</summary>
    Captured,
    // ---- Commander (Rts.Sim/Combat/OverchargeSystem) ----
    /// <summary>An ACU fired an Overcharge shot: Pos = muzzle, Size = the shot's DamageRadius, Army = shooter,
    /// BlueprintIndex/WeaponIndex = its weapon. Raised in addition to that shot's MuzzleFlash.</summary>
    Overcharge,
    /// <summary>An Overcharge shot hit: Pos = impact, Size = DamageRadius, BlueprintIndex/WeaponIndex = shooter's weapon.
    /// Raised in addition to the shot's Explosion.</summary>
    OverchargeHit,
}

/// <summary>Cosmetic events forwarded to the client (visual/audio effects). Never read by gameplay code.</summary>
public struct EffectEvent
{
    public EffectKind Kind;
    public Vector3 Pos;
    public float Size;
    public int Army;
    public int BlueprintIndex;
    /// <summary>Combat effects (MuzzleFlash / Impact / Explosion): index of the weapon in BlueprintIndex's
    /// Weapons array that caused it (client picks sounds/visuals per weapon). 0 for other kinds.</summary>
    public int WeaponIndex;
}

/// <summary>Event lists filled during a tick. Parallel systems write into ChunkedBuffers and drain here.</summary>
public sealed class SimEvents
{
    public readonly List<DamageEvent> Damage = new();
    public readonly List<AreaDamageEvent> AreaDamage = new();
    /// <summary>Effects since the last snapshot (cleared by the snapshot writer). Cosmetic: not saved.</summary>
    [Persistence.NoSave] public readonly List<EffectEvent> Effects = new();
}

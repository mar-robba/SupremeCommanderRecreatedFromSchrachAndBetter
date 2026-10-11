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
    /// <summary>Effects since the last snapshot (cleared by the snapshot writer).</summary>
    public readonly List<EffectEvent> Effects = new();
}

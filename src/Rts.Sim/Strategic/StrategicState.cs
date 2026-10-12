using System.Numerics;
using System.Runtime.CompilerServices;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Strategic;

/// <summary>Runtime state of a unit's shield (see <see cref="ShieldSystem"/>).</summary>
public struct ShieldState
{
    /// <summary>Current shield HP (0 while down).</summary>
    public float Hp;
    /// <summary>While down: powered seconds left until the shield comes back up at full HP.</summary>
    public float RechargeLeft;
    /// <summary>Seconds left before regeneration resumes after a hit.</summary>
    public float RegenDelay;
    public bool Up;
    public int LastHitTick;
}

/// <summary>Runtime state of a missile silo (see <see cref="SiloSystem"/>).</summary>
public struct SiloState
{
    /// <summary>Missiles ready to launch.</summary>
    public int Stock;
    /// <summary>0..1 progress of the missile being built.</summary>
    public float Progress;
    /// <summary>Fraction of a missile requested last tick; paid by EconomySystem this tick, then applied × efficiency.</summary>
    public float PendingWork;
}

/// <summary>Runtime state of a missile defense (see <see cref="MissileSystem"/>).</summary>
public struct DefenseState
{
    /// <summary>Seconds until the next interceptor can be fired.</summary>
    public float Cooldown;
}

/// <summary>What a projectile slot registered in <see cref="StrategicState"/> is.</summary>
public enum MissileRole : byte { None, Tactical, Strategic, Interceptor }

/// <summary>A strategic launch (nuke) for the global HUD warning ("Strategic launch detected").</summary>
public struct LaunchRecord
{
    /// <summary>1, 2, 3, ... in launch order (the client shows each serial once).</summary>
    public int Serial;
    public int Tick;
    public int Army;
    public SiloKind Kind;
    public Vector2 From, Target;
}

/// <summary>A strategic detonation waiting for DamageSystem (two-ring blast, ignores shields).</summary>
internal struct PendingDetonation
{
    public Vector2 Center;
    public float InnerRadius, InnerDamage, OuterRadius, OuterDamage;
    public UnitHandle Source;
    public int Army;
}

/// <summary>
/// Per-world state of the strategic module (shields, silos, missile defense, strategic missiles), attached to the
/// <see cref="SimWorld"/> with a ConditionalWeakTable so no shared world/store file needs new fields.
/// Per-unit arrays are indexed by unit slot (reset in <see cref="StrategicSystem.InitUnit"/>), per-projectile arrays
/// by projectile slot and only valid while <c>MissileId[p] == Projectiles.Id[p]</c>.
/// </summary>
public sealed class StrategicState
{
    private static readonly ConditionalWeakTable<SimWorld, StrategicState> Table = new();

    public static StrategicState Of(SimWorld w) => Table.GetValue(w, static x => new StrategicState(x));

    /// <summary>Strategic units (any module) in spawn order; dead entries are pruned each tick.</summary>
    public readonly List<UnitHandle> Units = new();
    public readonly ShieldState[] Shield;
    public readonly SiloState[] Silo;
    public readonly DefenseState[] Defense;

    /// <summary>Projectile id of the strategic missile / interceptor in this slot (0 = none).</summary>
    public readonly int[] MissileId;
    public readonly MissileRole[] Role;
    /// <summary>Missiles: launch point, flight duration, elapsed time, apex height (absolute y) of the path.</summary>
    public readonly Vector3[] Start;
    public readonly float[] Duration, Elapsed, Apex;
    /// <summary>Interceptors: target projectile slot + id, and the target's position/velocity at the start of the tick.</summary>
    public readonly int[] TargetSlot, TargetId;
    public readonly Vector3[] AimPos, AimVel;
    /// <summary>Interceptors already chasing this missile slot (recomputed each tick).</summary>
    public readonly int[] Engaged;
    /// <summary>Registered missile/interceptor slots (pruned each tick, registration order).</summary>
    public readonly List<int> Missiles = new();

    public readonly ShieldIndex Index;
    /// <summary>Per army: bit b set if army b is an enemy (refreshed each tick).</summary>
    public readonly ulong[] EnemyMask;

    internal readonly List<PendingDetonation> Detonations = new();
    /// <summary>Inner rings of applied blasts (tick applied, center, radius): props inside are removed next tick.</summary>
    internal readonly List<(int Tick, Vector2 Center, float Radius)> Craters = new();
    /// <summary>Recent strategic launches (newest last, at most <see cref="MaxLaunchRecords"/>).</summary>
    public readonly List<LaunchRecord> Launches = new();
    public const int MaxLaunchRecords = 16;
    public int LaunchSerial;

    private StrategicState(SimWorld w)
    {
        if (w.Armies.Length > 64) throw new NotSupportedException("Strategic module supports at most 64 armies");
        StrategicSystem.Validate(w.Blueprints);
        int n = w.Units.Capacity;
        Shield = new ShieldState[n];
        Silo = new SiloState[n];
        Defense = new DefenseState[n];
        int pc = w.Projectiles.Capacity;
        MissileId = new int[pc];
        Role = new MissileRole[pc];
        Start = new Vector3[pc];
        Duration = new float[pc];
        Elapsed = new float[pc];
        Apex = new float[pc];
        TargetSlot = new int[pc];
        TargetId = new int[pc];
        AimPos = new Vector3[pc];
        AimVel = new Vector3[pc];
        Engaged = new int[pc];
        Index = new ShieldIndex(w.Terrain.Width, w.Terrain.Height);
        EnemyMask = new ulong[w.Armies.Length];
        RefreshEnemies(w);
    }

    internal void RefreshEnemies(SimWorld w)
    {
        for (int a = 0; a < w.Armies.Length; a++)
        {
            ulong m = 0;
            for (int b = 0; b < w.Armies.Length; b++)
                if (w.IsEnemy(a, b)) m |= 1UL << b;
            EnemyMask[a] = m;
        }
    }

    /// <summary>Is projectile slot p a live strategic missile or interceptor?</summary>
    public bool IsMissile(ProjectileStore p, int slot) => MissileId[slot] != 0 && MissileId[slot] == p.Id[slot] && p.Alive[slot];

    public bool IsEnemy(int army, int other) => ((EnemyMask[army] >> other) & 1) != 0;

    /// <summary>Stockpile of a silo unit (0 if none / not a silo).</summary>
    public int StockOf(SimWorld w, int unit) => w.Units.IsActive(unit) && w.BpOf(unit).Silo != null ? Silo[unit].Stock : 0;
}

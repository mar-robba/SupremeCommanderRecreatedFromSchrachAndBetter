using System.Numerics;
using System.Runtime.CompilerServices;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>A shot decided in parallel by WeaponSystem, executed sequentially in chunk order.</summary>
internal struct ShotRequest
{
    public int Shooter;
    public int Weapon;
    public int Bp;
    public int Army;
    public UnitHandle Source;
    public UnitHandle Target;
    public Vector3 Pos;
    public Vector3 Vel;
    public Vector3 TargetPos;
    public float Gravity;
    public float Life;
    public float Damage;
    public float DamageRadius;
    public ProjectileFlags Flags;
    /// <summary>Beam: apply damage now instead of spawning a projectile.</summary>
    public bool Instant;
    /// <summary>Emit a muzzle flash (first projectile of a salvo).</summary>
    public bool Flash;
    public Vector3 FlashPos;
}

internal enum ImpactKind : byte
{
    /// <summary>Lifetime over / left the map: vanish silently.</summary>
    Expire,
    /// <summary>Hit a unit's collision sphere.</summary>
    Unit,
    /// <summary>Hit the ground or water surface.</summary>
    Ground,
    /// <summary>Proximity/timed burst (flak).</summary>
    Burst,
}

/// <summary>Projectile end-of-life decided in parallel by ProjectileSystem, applied sequentially in chunk order.</summary>
internal struct ProjectileImpact
{
    public int Projectile;
    public ImpactKind Kind;
    /// <summary>Unit receiving direct damage (-1 = none).</summary>
    public int Unit;
    public Vector3 Pos;
}

/// <summary>
/// Per-world combat scratch data (owned by the combat systems, attached to the SimWorld without touching it).
/// <see cref="Prepare"/> runs once per tick, after movement, and caches each unit's current layer plus a per-cell
/// "which armies are here" bitmask so that target scans and projectile collision skip cells with no enemies.
/// Supports up to 64 armies.
/// </summary>
internal sealed class CombatState
{
    private static readonly ConditionalWeakTable<SimWorld, CombatState> Table = new();

    public static CombatState Of(SimWorld w) => Table.GetValue(w, static x => new CombatState(x));

    public readonly CombatCatalog Catalog;
    /// <summary>Current targeting layer of each unit slot (valid for units in AliveList this tick).</summary>
    public readonly LayerMask[] Layer;
    /// <summary>1 if the unit has a target within weapon range this tick (drives UnitFlags.Firing).</summary>
    public readonly byte[] Engaged;
    /// <summary>Per SpatialGrid cell: bit a set if a unit of army a is listed in the cell.</summary>
    public readonly ulong[] CellArmies;
    /// <summary>Per army: bit b set if army b is an enemy.</summary>
    public readonly ulong[] EnemyArmies;
    public readonly ChunkedBuffer<ShotRequest> Shots = new();
    public readonly ChunkedBuffer<ProjectileImpact> Impacts = new();
    private int _preparedTick = int.MinValue;
    private readonly Action<int, int, int> _layerBody;
    private SimWorld _w;

    private CombatState(SimWorld w)
    {
        if (w.Armies.Length > 64) throw new NotSupportedException("Combat supports at most 64 armies");
        _w = w;
        Catalog = new CombatCatalog(w.Blueprints);
        Layer = new LayerMask[w.Units.Capacity];
        Engaged = new byte[w.Units.Capacity];
        CellArmies = new ulong[w.Grid.Cols * w.Grid.Rows];
        EnemyArmies = new ulong[w.Armies.Length];
        _layerBody = ComputeLayers;
    }

    /// <summary>Refreshes per-tick caches (idempotent within a tick). Call at the start of each combat system.</summary>
    public void Prepare(SimWorld w)
    {
        if (_preparedTick == w.Tick) return;
        _preparedTick = w.Tick;
        _w = w;
        var u = w.Units;

        for (int a = 0; a < w.Armies.Length; a++)
        {
            ulong m = 0;
            for (int b = 0; b < w.Armies.Length; b++)
                if (w.IsEnemy(a, b)) m |= 1UL << b;
            EnemyArmies[a] = m;
        }

        Par.For(u.AliveCount, 512, _layerBody);

        var g = w.Grid;
        for (int cz = 0; cz < g.Rows; cz++)
        for (int cx = 0; cx < g.Cols; cx++)
        {
            ulong m = 0;
            foreach (int j in g.Cell(cx, cz)) m |= 1UL << u.Army[j];
            CellArmies[cz * g.Cols + cx] = m;
        }
    }

    private void ComputeLayers(int _, int start, int end)
    {
        var w = _w;
        var u = w.Units;
        for (int k = start; k < end; k++)
        {
            int i = u.AliveList[k];
            Layer[i] = OrderSystem.LayerOf(w, i);
        }
    }

    /// <summary>Aim/collision center of a unit in 3D.</summary>
    public Vector3 Center(UnitStore u, int j) =>
        new(u.Pos[j].X, u.Elevation[j] + Catalog.Units[u.Bp[j]].HitHeight, u.Pos[j].Y);

    /// <summary>
    /// Visits units listed in grid cells overlapping the circle, skipping cells that contain no army in
    /// <paramref name="armyMask"/>. Deterministic order. The grid holds start-of-tick positions, so callers add
    /// <see cref="GridSlack"/> to the radius and check exact distances themselves.
    /// </summary>
    public void ForEachInRadius<T>(SimWorld w, Vector2 center, float radius, ulong armyMask, ref T visitor) where T : struct, IUnitVisitor
    {
        var g = w.Grid;
        float inv = 1f / g.CellSize;
        int x0 = Math.Clamp((int)((center.X - radius) * inv), 0, g.Cols - 1);
        int x1 = Math.Clamp((int)((center.X + radius) * inv), 0, g.Cols - 1);
        int z0 = Math.Clamp((int)((center.Y - radius) * inv), 0, g.Rows - 1);
        int z1 = Math.Clamp((int)((center.Y + radius) * inv), 0, g.Rows - 1);
        for (int cz = z0; cz <= z1; cz++)
        for (int cx = x0; cx <= x1; cx++)
        {
            if ((CellArmies[cz * g.Cols + cx] & armyMask) == 0) continue;
            foreach (int j in g.Cell(cx, cz))
                if (!visitor.Visit(j)) return;
        }
    }

    /// <summary>Extra query radius covering units that moved since the grid was rebuilt this tick.</summary>
    public const float GridSlack = 2f;

    public bool IsEnemyArmy(int army, int other) => ((EnemyArmies[army] >> other) & 1) != 0;

    /// <summary>Can the army shoot at this unit (vision or radar contact)?</summary>
    public static bool CanSee(SimWorld w, int army, int j) => w.Intel.IsVisible(army, j) || w.Intel.IsDetected(army, j);
}

/// <summary>Cosmetic effect helper with a safety cap (headless runs never drain the effect list).</summary>
internal static class CombatFx
{
    public const int MaxPendingEffects = 32768;

    public static void Add(SimWorld w, EffectKind kind, Vector3 pos, float size, int army, int bp)
    {
        var list = w.Events.Effects;
        if (list.Count >= MaxPendingEffects) return;
        list.Add(new EffectEvent { Kind = kind, Pos = pos, Size = size, Army = army, BlueprintIndex = bp });
    }
}

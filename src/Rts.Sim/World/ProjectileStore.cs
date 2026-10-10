using System.Numerics;
using Rts.Sim.Core;

namespace Rts.Sim.World;

/// <summary>Projectile behaviour bits (set by WeaponSystem, read by ProjectileSystem and the client).</summary>
[Flags]
public enum ProjectileFlags : byte
{
    None = 0,
    /// <summary>Steers toward Target (missiles).</summary>
    Homing = 1,
    /// <summary>Gravity arc; explodes on ground/water contact.</summary>
    Ballistic = 2,
    /// <summary>Flak: bursts near its air target or when its timed fuse (TimeLeft) runs out.</summary>
    ProximityFuse = 4,
    /// <summary>Dropped by a bomber.</summary>
    Bomb = 8,
}

/// <summary>SoA storage for projectiles in flight. Spawn/free only in sequential phases.</summary>
public sealed class ProjectileStore
{
    public readonly int Capacity;
    public readonly bool[] Alive;
    public readonly Vector3[] Pos;
    public readonly Vector3[] Vel;
    public readonly int[] Army;
    public readonly UnitHandle[] Source;
    /// <summary>Blueprint index of the firing unit and weapon slot (for damage/visual data).</summary>
    public readonly int[] SourceBp;
    public readonly int[] WeaponIndex;
    public readonly UnitHandle[] Target;
    public readonly Vector3[] TargetPos;
    public readonly float[] Damage;
    public readonly float[] DamageRadius;
    /// <summary>Seconds left before the projectile expires.</summary>
    public readonly float[] TimeLeft;
    /// <summary>Gravity applied (ballistic) in units/s^2; 0 for straight shots.</summary>
    public readonly float[] Gravity;
    /// <summary>Unique id for render interpolation.</summary>
    public readonly int[] Id;
    public readonly ProjectileFlags[] Flags;

    public readonly int[] AliveList;
    public int AliveCount;
    private readonly Stack<int> _free;
    private int _nextId = 1;

    public ProjectileStore(int capacity)
    {
        Capacity = capacity;
        Alive = new bool[capacity];
        Pos = new Vector3[capacity];
        Vel = new Vector3[capacity];
        Army = new int[capacity];
        Source = new UnitHandle[capacity];
        SourceBp = new int[capacity];
        WeaponIndex = new int[capacity];
        Target = new UnitHandle[capacity];
        TargetPos = new Vector3[capacity];
        Damage = new float[capacity];
        DamageRadius = new float[capacity];
        TimeLeft = new float[capacity];
        Gravity = new float[capacity];
        Id = new int[capacity];
        Flags = new ProjectileFlags[capacity];
        AliveList = new int[capacity];
        _free = new Stack<int>(capacity);
        for (int i = capacity - 1; i >= 0; i--) _free.Push(i);
    }

    /// <summary>Returns the slot index or -1 if full. Caller fills remaining fields.</summary>
    public int Spawn(Vector3 pos, Vector3 vel, int army, UnitHandle source, int sourceBp, int weaponIndex,
        UnitHandle target, Vector3 targetPos, float damage, float damageRadius, float timeLeft, float gravity)
    {
        if (_free.Count == 0) return -1;
        int i = _free.Pop();
        Alive[i] = true;
        Pos[i] = pos; Vel[i] = vel; Army[i] = army; Source[i] = source; SourceBp[i] = sourceBp;
        WeaponIndex[i] = weaponIndex; Target[i] = target; TargetPos[i] = targetPos; Damage[i] = damage;
        DamageRadius[i] = damageRadius; TimeLeft[i] = timeLeft; Gravity[i] = gravity; Id[i] = _nextId++;
        Flags[i] = ProjectileFlags.None;
        return i;
    }

    public void Free(int i)
    {
        if (!Alive[i]) return;
        Alive[i] = false;
        _free.Push(i);
    }

    public void RebuildAliveList()
    {
        int n = 0;
        for (int i = 0; i < Capacity; i++) if (Alive[i]) AliveList[n++] = i;
        AliveCount = n;
    }
}

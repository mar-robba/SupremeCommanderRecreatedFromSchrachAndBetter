using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;

namespace Rts.Sim.World;

/// <summary>
/// Structure-of-arrays storage for all units, fixed capacity, slot reuse via free list with generations.
/// Iterate live units via <see cref="AliveList"/>[0..AliveCount) (sorted ascending, rebuilt each tick).
/// Spawning/freeing is only allowed in SEQUENTIAL phases (SimWorld.SpawnUnit / CleanupSystem).
/// </summary>
public sealed class UnitStore
{
    public const int MaxWeapons = 4;

    public readonly int Capacity;

    // Core
    public readonly bool[] Alive;
    public readonly int[] Gen;
    public readonly int[] Bp;
    public readonly int[] Army;
    public readonly Vector2[] Pos;
    /// <summary>World Y of the unit's base (ground/water surface, or flight altitude).</summary>
    public readonly float[] Elevation;
    public readonly float[] Heading;
    public readonly float[] Health;
    /// <summary>0..1; 1 = complete.</summary>
    public readonly float[] BuildFraction;
    public readonly UnitFlags[] Flags;
    /// <summary>Tick of the last damage taken (UI/AI).</summary>
    public readonly int[] LastHitTick;
    /// <summary>Accumulated veterancy kills.</summary>
    public readonly int[] Kills;
    /// <summary>Fire state (Fire at will / Return fire / Hold fire, see Combat/FireStates).</summary>
    public readonly FireState[] FireStates;

    // Modules
    public readonly MoveState[] Move;
    public readonly WeaponState[] Weapons;
    public readonly OrderQueue[] Orders;
    public readonly BuilderState[] Builder;
    public readonly FactoryState[] Factory;
    public readonly EconState[] Econ;
    /// <summary>In-place structure upgrade progress (owned by UpgradeSystem).</summary>
    public readonly UpgradeState[] Upgrade;

    /// <summary>Scratch buffer for double-buffered position writes in parallel systems.</summary>
    public readonly Vector2[] ScratchPos;

    public readonly int[] AliveList;
    public int AliveCount;

    private readonly Stack<int> _free;

    public UnitStore(int capacity)
    {
        Capacity = capacity;
        Alive = new bool[capacity];
        Gen = new int[capacity];
        Bp = new int[capacity];
        Army = new int[capacity];
        Pos = new Vector2[capacity];
        Elevation = new float[capacity];
        Heading = new float[capacity];
        Health = new float[capacity];
        BuildFraction = new float[capacity];
        Flags = new UnitFlags[capacity];
        LastHitTick = new int[capacity];
        Kills = new int[capacity];
        FireStates = new FireState[capacity];
        Move = new MoveState[capacity];
        Weapons = new WeaponState[capacity * MaxWeapons];
        Orders = new OrderQueue[capacity];
        for (int i = 0; i < capacity; i++) Orders[i] = new OrderQueue();
        Builder = new BuilderState[capacity];
        Factory = new FactoryState[capacity];
        Econ = new EconState[capacity];
        Upgrade = new UpgradeState[capacity];
        ScratchPos = new Vector2[capacity];
        AliveList = new int[capacity];
        _free = new Stack<int>(capacity);
        for (int i = capacity - 1; i >= 0; i--) _free.Push(i);
    }

    public int FreeSlots => _free.Count;

    public UnitHandle HandleOf(int index) => new(index, Gen[index]);

    /// <summary>True if the handle points to a live, not-dead unit.</summary>
    public bool IsActive(UnitHandle h) =>
        (uint)h.Index < (uint)Capacity && Alive[h.Index] && Gen[h.Index] == h.Generation && (Flags[h.Index] & UnitFlags.Dead) == 0;

    public bool IsActive(int index) => Alive[index] && (Flags[index] & UnitFlags.Dead) == 0;

    public bool IsComplete(int index) => (Flags[index] & UnitFlags.UnderConstruction) == 0;

    public Span<WeaponState> WeaponsOf(int index) => Weapons.AsSpan(index * MaxWeapons, MaxWeapons);

    /// <summary>Allocates a slot and resets core fields. Use SimWorld.SpawnUnit (which also initializes modules).</summary>
    internal int Allocate()
    {
        if (_free.Count == 0) return -1;
        int i = _free.Pop();
        Alive[i] = true;
        Gen[i]++;
        Flags[i] = UnitFlags.None;
        Elevation[i] = 0;
        LastHitTick[i] = -1000;
        Kills[i] = 0;
        FireStates[i] = FireState.FireAtWill;
        Move[i] = default;
        Move[i].PathId = -1;
        WeaponsOf(i).Clear();
        for (int w = 0; w < MaxWeapons; w++) Weapons[i * MaxWeapons + w].Target = UnitHandle.None;
        Orders[i].Clear();
        Builder[i] = new BuilderState { Target = UnitHandle.None };
        var q = Factory[i].Queue;
        q?.Clear();
        Factory[i] = new FactoryState { Queue = q, Current = UnitHandle.None };
        Econ[i] = default;
        Upgrade[i] = default;
        return i;
    }

    /// <summary>Frees the slot (CleanupSystem only).</summary>
    internal void Free(int i)
    {
        Alive[i] = false;
        Flags[i] = UnitFlags.Dead;
        Orders[i].Clear();
        _free.Push(i);
    }

    /// <summary>Rebuilds AliveList (ascending index order = deterministic iteration order). Carried units (inside a
    /// transport/carrier) are alive but left out: they are off the map.</summary>
    public void RebuildAliveList()
    {
        int n = 0;
        for (int i = 0; i < Capacity; i++)
            if (Alive[i] && (Flags[i] & (UnitFlags.Dead | UnitFlags.Carried)) == 0) AliveList[n++] = i;
        AliveCount = n;
    }
}

using System.Numerics;
using Rts.Sim.Core;

namespace Rts.Sim.World;

/// <summary>
/// Reclaimable props: unit wrecks (and later trees/rocks). Fixed-capacity SoA with a free list and generations,
/// like <see cref="UnitStore"/>. Spawn/free only in SEQUENTIAL phases (unit death hook, ConstructionSystem).
/// Props do not block movement or placement; a new structure absorbs (auto-reclaims) the props under its footprint.
/// A prop reference is stored as <c>UnitHandle(propIndex, propGen)</c> (e.g. in Order.Spawned of Reclaim orders);
/// validate it with <see cref="IsActive"/>.
/// </summary>
public sealed class PropStore
{
    public readonly int Capacity;
    public readonly bool[] Alive;
    public readonly int[] Gen;
    /// <summary>Unique id (never reused) for client-side tracking/interpolation.</summary>
    public readonly int[] Id;
    public readonly Vector2[] Pos;
    public readonly float[] Heading;
    /// <summary>Footprint radius (selection / reclaim range).</summary>
    public readonly float[] Radius;
    /// <summary>Remaining reclaimable mass / energy.</summary>
    public readonly float[] Mass;
    public readonly float[] Energy;
    /// <summary>Initial mass / energy (reclaim time and "how much is left" visuals).</summary>
    public readonly float[] MassMax;
    public readonly float[] EnergyMax;
    /// <summary>Blueprint of the unit this wreck came from (visuals), -1 for generic props.</summary>
    public readonly int[] BlueprintIndex;

    private readonly int[] _aliveList;
    private int _aliveCount;
    private bool _dirty;
    private readonly Stack<int> _free;
    private int _nextId = 1;

    public PropStore(int capacity)
    {
        Capacity = capacity;
        Alive = new bool[capacity];
        Gen = new int[capacity];
        Id = new int[capacity];
        Pos = new Vector2[capacity];
        Heading = new float[capacity];
        Radius = new float[capacity];
        Mass = new float[capacity];
        Energy = new float[capacity];
        MassMax = new float[capacity];
        EnergyMax = new float[capacity];
        BlueprintIndex = new int[capacity];
        _aliveList = new int[capacity];
        _free = new Stack<int>(capacity);
        for (int i = capacity - 1; i >= 0; i--) _free.Push(i);
    }

    public int Count => Capacity - _free.Count;

    /// <summary>Live prop slots in ascending index order (deterministic). Rebuilt lazily after spawns/frees.</summary>
    public ReadOnlySpan<int> AliveList
    {
        get
        {
            if (_dirty)
            {
                int n = 0;
                for (int i = 0; i < Capacity; i++) if (Alive[i]) _aliveList[n++] = i;
                _aliveCount = n;
                _dirty = false;
            }
            return _aliveList.AsSpan(0, _aliveCount);
        }
    }

    public UnitHandle HandleOf(int index) => new(index, Gen[index]);

    public bool IsActive(UnitHandle h) => (uint)h.Index < (uint)Capacity && Alive[h.Index] && Gen[h.Index] == h.Generation;

    /// <summary>Creates a prop. Returns the slot or -1 when the store is full (the prop is then simply not created).</summary>
    public int Spawn(Vector2 pos, float heading, int blueprintIndex, float radius, float mass, float energy)
    {
        if (_free.Count == 0) return -1;
        int i = _free.Pop();
        Alive[i] = true;
        Gen[i]++;
        Id[i] = _nextId++;
        Pos[i] = pos;
        Heading[i] = heading;
        Radius[i] = radius;
        Mass[i] = MassMax[i] = MathF.Max(0, mass);
        Energy[i] = EnergyMax[i] = MathF.Max(0, energy);
        BlueprintIndex[i] = blueprintIndex;
        _dirty = true;
        return i;
    }

    public void Free(int i)
    {
        if (!Alive[i]) return;
        Alive[i] = false;
        _free.Push(i);
        _dirty = true;
    }

    /// <summary>Nearest live prop whose edge is within maxDist of pos, or -1. Ties: lowest index.</summary>
    public int FindNearest(Vector2 pos, float maxDist)
    {
        int best = -1;
        float bestD = float.MaxValue;
        foreach (int i in AliveList)
        {
            float d = Vector2.Distance(Pos[i], pos) - Radius[i];
            if (d <= maxDist && d < bestD) { bestD = d; best = i; }
        }
        return best;
    }
}

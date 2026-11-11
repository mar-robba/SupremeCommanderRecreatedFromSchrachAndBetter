using System.Numerics;
using Rts.Sim.Core;

namespace Rts.Sim.World;

/// <summary>What a prop is (append-only). Wrecks come from dead units; trees and rocks from the map generator.</summary>
public enum PropKind : byte { Wreck, Tree, Rock }

/// <summary>Tree state (wrecks and rocks keep None). Fallen and Burning/Burnt combine.</summary>
[Flags]
public enum PropFlags : byte { None = 0, Fallen = 1, Burning = 2, Burnt = 4 }

/// <summary>
/// Reclaimable props: unit wrecks, trees and rocks. Fixed-capacity SoA with a free list and generations,
/// like <see cref="UnitStore"/>. Spawn/free/state changes only in SEQUENTIAL phases (unit death hook,
/// ConstructionSystem, <see cref="PropSystem"/>). Props do not block movement or placement; a new structure absorbs
/// (auto-reclaims) the props under its footprint. A prop reference is stored as <c>UnitHandle(propIndex, propGen)</c>
/// (e.g. in Order.Spawned of Reclaim orders); validate it with <see cref="IsActive"/>.
/// Spatial queries (<see cref="FindNearest"/>, <see cref="QueryRadius"/>) use a lazy [NoSave] grid whose results do not
/// depend on when it was built (sorted by index / ties by lowest index), so they equal a linear scan.
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
    // ---- wave 8b: trees and rocks (additive)
    public readonly PropKind[] Kind;
    public readonly PropFlags[] Flags;
    /// <summary>Visual variant (tree species / rock shape), chosen by the map generator.</summary>
    public readonly byte[] Variant;
    /// <summary>Direction a fallen tree points to (heading convention).</summary>
    public readonly float[] FallHeading;
    /// <summary>Tick of the last Flags change (client animations: fall, burn).</summary>
    public readonly int[] StateTick;
    /// <summary>Burning trees end at this tick (become Burnt).</summary>
    public readonly int[] BurnEnd;
    /// <summary>Burning trees in ignition order (handles: a prop may be reclaimed while it burns). Saved state.</summary>
    public readonly List<UnitHandle> Burning = new();
    /// <summary>PropSystem's per-chunk fall events (scratch).</summary>
    [Persistence.NoSave] internal ChunkedBuffer<(int, float)>? FallScratch;

    // Lazy cache of the live slots: [NoSave] because its stale tail (slots past _aliveCount) depends on WHEN it was last
    // rebuilt, i.e. on who read AliveList (state hash, sandbox, AI), not on the game: saved, it made a replay's end
    // state differ from the recorded game's (ReplayTests, wave 8a). After a load it is null/invalid and rebuilt on access.
    [Persistence.NoSave] private int[]? _aliveList;
    [Persistence.NoSave] private int _aliveCount;
    [Persistence.NoSave] private bool _aliveValid;
    private readonly Stack<int> _free;
    private int _nextId = 1;

    // Spatial grid (cells of GridCell world units): built on first query, then kept up to date by Spawn/Free.
    public const float GridCell = 4f;
    [Persistence.NoSave] private int[][]? _cells;
    [Persistence.NoSave] private int[]? _cellCount;
    [Persistence.NoSave] private int _gw, _gh;
    [Persistence.NoSave] private float _maxRadius;

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
        Kind = new PropKind[capacity];
        Flags = new PropFlags[capacity];
        Variant = new byte[capacity];
        FallHeading = new float[capacity];
        StateTick = new int[capacity];
        BurnEnd = new int[capacity];
        _free = new Stack<int>(capacity);
        for (int i = capacity - 1; i >= 0; i--) _free.Push(i);
    }

    public int Count => Capacity - _free.Count;

    /// <summary>Live prop slots in ascending index order (deterministic). Rebuilt lazily after spawns/frees.</summary>
    public ReadOnlySpan<int> AliveList
    {
        get
        {
            if (!_aliveValid)
            {
                _aliveList ??= new int[Capacity];
                int n = 0;
                for (int i = 0; i < Capacity; i++) if (Alive[i]) _aliveList[n++] = i;
                _aliveCount = n;
                _aliveValid = true;
            }
            return _aliveList.AsSpan(0, _aliveCount);
        }
    }

    public UnitHandle HandleOf(int index) => new(index, Gen[index]);

    public bool IsActive(UnitHandle h) => (uint)h.Index < (uint)Capacity && Alive[h.Index] && Gen[h.Index] == h.Generation;

    /// <summary>Creates a wreck / generic prop. Returns the slot or -1 when the store is full (the prop is then simply not created).</summary>
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
        Kind[i] = PropKind.Wreck;
        Flags[i] = PropFlags.None;
        Variant[i] = 0;
        FallHeading[i] = 0;
        StateTick[i] = 0;
        BurnEnd[i] = 0;
        _aliveValid = false;
        GridAdd(i);
        return i;
    }

    /// <summary>Creates a tree or rock (map props). Returns the slot or -1 when full.</summary>
    public int SpawnNature(PropKind kind, Vector2 pos, float heading, byte variant, float radius, float mass, float energy)
    {
        int i = Spawn(pos, heading, -1, radius, mass, energy);
        if (i < 0) return -1;
        Kind[i] = kind;
        Variant[i] = variant;
        return i;
    }

    public void Free(int i)
    {
        if (!Alive[i]) return;
        Alive[i] = false;
        _free.Push(i);
        _aliveValid = false;
        GridRemove(i);
    }

    /// <summary>Nearest live prop whose edge is within maxDist of pos, or -1. Ties: lowest index.</summary>
    public int FindNearest(Vector2 pos, float maxDist)
    {
        if (Count == 0) return -1;
        EnsureGrid();
        float reach = maxDist + _maxRadius;
        GridRange(pos, reach, out int x0, out int z0, out int x1, out int z1);
        int best = -1;
        float bestD = float.MaxValue;
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            int c = z * _gw + x;
            int n = _cellCount![c];
            if (n == 0) continue;
            var items = _cells![c];
            for (int k = 0; k < n; k++)
            {
                int i = items[k];
                float d = Vector2.Distance(Pos[i], pos) - Radius[i];
                if (d <= maxDist && (d < bestD || (d == bestD && i < best))) { bestD = d; best = i; }
            }
        }
        return best;
    }

    /// <summary>Appends the live props whose CENTER is within radius of pos, in ascending index order.</summary>
    public void QueryRadius(Vector2 pos, float radius, List<int> result)
    {
        int start = result.Count;
        if (Count == 0) return;
        EnsureGrid();
        GridRange(pos, radius, out int x0, out int z0, out int x1, out int z1);
        float r2 = radius * radius;
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            int c = z * _gw + x;
            int n = _cellCount![c];
            var items = _cells![c];
            for (int k = 0; k < n; k++)
            {
                int i = items[k];
                if (Vector2.DistanceSquared(Pos[i], pos) <= r2) result.Add(i);
            }
        }
        if (result.Count - start > 1) result.Sort(start, result.Count - start, null);
    }

    /// <summary>Read-only access for parallel code (call <see cref="EnsureGrid"/> in a sequential phase first):
    /// the slots in grid cell (cx, cz), unordered.</summary>
    public ReadOnlySpan<int> Cell(int cx, int cz)
    {
        if ((uint)cx >= (uint)_gw || (uint)cz >= (uint)_gh) return default;
        int c = cz * _gw + cx;
        return _cells![c] == null ? default : _cells[c].AsSpan(0, _cellCount![c]);
    }

    public int GridWidth => _gw;
    public int GridHeight => _gh;
    public float MaxRadius => _maxRadius;

    /// <summary>Builds the spatial grid if needed (sequential).</summary>
    public void EnsureGrid()
    {
        if (_cells != null) return;
        // The world size is not known here: size the grid from the props (positions are never negative).
        float maxX = 64, maxZ = 64;
        foreach (int i in AliveList) { maxX = MathF.Max(maxX, Pos[i].X); maxZ = MathF.Max(maxZ, Pos[i].Y); }
        _gw = (int)(MathF.Max(maxX, _boundsHint.X) / GridCell) + 2;
        _gh = (int)(MathF.Max(maxZ, _boundsHint.Y) / GridCell) + 2;
        _cells = new int[_gw * _gh][];
        _cellCount = new int[_gw * _gh];
        _maxRadius = 0;
        foreach (int i in AliveList) GridAdd(i);
    }

    private Vector2 _boundsHint;

    /// <summary>World size (sets the grid extent; props outside are clamped into the border cells).</summary>
    public void SetBounds(float width, float height)
    {
        _boundsHint = new Vector2(width, height);
        _cells = null;
    }

    private void GridRange(Vector2 pos, float r, out int x0, out int z0, out int x1, out int z1)
    {
        x0 = Math.Clamp((int)MathF.Floor((pos.X - r) / GridCell), 0, _gw - 1);
        z0 = Math.Clamp((int)MathF.Floor((pos.Y - r) / GridCell), 0, _gh - 1);
        x1 = Math.Clamp((int)MathF.Floor((pos.X + r) / GridCell), 0, _gw - 1);
        z1 = Math.Clamp((int)MathF.Floor((pos.Y + r) / GridCell), 0, _gh - 1);
    }

    private int CellOf(Vector2 p) =>
        Math.Clamp((int)MathF.Floor(p.Y / GridCell), 0, _gh - 1) * _gw + Math.Clamp((int)MathF.Floor(p.X / GridCell), 0, _gw - 1);

    private void GridAdd(int i)
    {
        if (_cells == null) return;
        _maxRadius = MathF.Max(_maxRadius, Radius[i]);
        int c = CellOf(Pos[i]);
        ref var items = ref _cells[c];
        items ??= new int[4];
        int n = _cellCount![c];
        if (n == items.Length) Array.Resize(ref items, n * 2);
        items[n] = i;
        _cellCount[c] = n + 1;
    }

    private void GridRemove(int i)
    {
        if (_cells == null) return;
        int c = CellOf(Pos[i]);
        var items = _cells[c];
        int n = _cellCount![c];
        for (int k = 0; k < n; k++)
            if (items[k] == i) { items[k] = items[n - 1]; _cellCount[c] = n - 1; return; }
    }

    /// <summary>Changes a tree's flags (sequential) and stamps <see cref="StateTick"/>.</summary>
    public void SetFlags(int i, PropFlags flags, int tick)
    {
        if (Flags[i] == flags) return;
        Flags[i] = flags;
        StateTick[i] = tick;
    }
}

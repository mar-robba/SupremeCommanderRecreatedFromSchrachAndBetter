using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// (11a-1) Slope classes, grid/graph slots, the "may this unit stand here" question and the saved nav format.
/// <list type="bullet">
/// <item>Slope classes: the distinct values of max(<see cref="Terrain.DefaultMaxSlope"/>, Movement.MaxSlope) over
///   the mobile Land/Hover/Amphibious blueprints, sorted, at most <see cref="SlopeSlots"/> (more = the extra ones
///   fall back to the steepest class below their value). Class 0 is always the default 0.75, which the map
///   generator guarantees connectivity for; nothing is stricter. Water/Sub ignore slope (class 0).</item>
/// <item>Grids exist per (layer, slope class) only where a blueprint uses the class (<see cref="EnsureGrids"/>);
///   graphs per (grid, requirement) only when a unit needs them (lazy, saved).</item>
/// </list>
/// </summary>
public sealed partial class PathService
{
    public const int SlopeSlots = 3;
    internal const int GridSlots = 5 * SlopeSlots;
    internal const int GraphStride = NavGrid.MaxClearance + 1;
    /// <summary>Format of the saved nav state (grids, graphs, paths). A save whose format or array sizes differ is
    /// dropped after load and rebuilt (grids and graphs are pure functions of terrain + occupancy).
    /// 0 = before 11a-1 (Chebyshev classes), 1 = 11a-1 (Euclidean field, slope classes).</summary>
    public const int CurrentNavFormat = 1;

    /// <summary>Saved: format of this nav state (see <see cref="CurrentNavFormat"/>).</summary>
    public int NavFormat = CurrentNavFormat;

    [Persistence.NoSave] private float[]? _slopeClasses;
    [Persistence.NoSave] private bool[]? _slopeGridsUsed;
    [Persistence.NoSave] private byte[]? _bpSlope;

    /// <summary>Slope limits of the classes (index 0 = <see cref="Terrain.DefaultMaxSlope"/>), from the blueprints.</summary>
    public float[] SlopeClasses => _slopeClasses ??= BuildSlopeClasses(_w.Blueprints);

    private bool[] SlopeGridsUsed
    {
        get
        {
            if (_slopeGridsUsed != null) return _slopeGridsUsed;
            var used = new bool[GridSlots];
            foreach (var bp in _w.Blueprints.All)
            {
                int slot = GridSlotOf(bp);
                if (slot >= 0) used[slot] = true;
            }
            return _slopeGridsUsed = used;
        }
    }

    public static float[] BuildSlopeClasses(BlueprintDb db)
    {
        var vals = new List<float> { Terrain.DefaultMaxSlope };
        foreach (var bp in db.All)
        {
            if (!bp.IsMobile || bp.Movement.Layer is not (MotionLayer.Land or MotionLayer.Hover or MotionLayer.Amphibious)) continue;
            float s = MathF.Max(Terrain.DefaultMaxSlope, bp.Movement.MaxSlope);
            if (!vals.Contains(s)) vals.Add(s);
        }
        vals.Sort();
        if (vals.Count > SlopeSlots) vals.RemoveRange(SlopeSlots, vals.Count - SlopeSlots);
        return vals.ToArray();
    }

    /// <summary>Slope class of a blueprint: the steepest class not steeper than its MaxSlope (0 for Water/Sub).
    /// Thread-safe (the lazy table is idempotent).</summary>
    public int SlopeClassOf(UnitBlueprint bp)
    {
        var t = _bpSlope;
        if (t == null || t.Length != _w.Blueprints.All.Count)
        {
            var classes = SlopeClasses;
            t = new byte[_w.Blueprints.All.Count];
            foreach (var b in _w.Blueprints.All)
            {
                if (b.Movement.Layer is not (MotionLayer.Land or MotionLayer.Hover or MotionLayer.Amphibious)) continue;
                float s = MathF.Max(Terrain.DefaultMaxSlope, b.Movement.MaxSlope);
                int si = 0;
                for (int k = 1; k < classes.Length; k++) if (classes[k] <= s) si = k;
                t[b.Index] = (byte)si;
            }
            _bpSlope = t;
        }
        return t[bp.Index];
    }

    /// <summary>Grid slot of a blueprint (-1 for Air/None).</summary>
    internal int GridSlotOf(UnitBlueprint bp)
    {
        int gi = GridIndex(bp.Movement.Layer);
        return gi < 0 ? -1 : gi * SlopeSlots + SlopeClassOf(bp);
    }

    /// <summary>
    /// THE question "may a unit of this blueprint stand at p" (its layer, slope class and radius). Air: always.
    /// MovementSystem accepts a step only where this holds (after separation), like the path search's cells.
    /// Thread-safe.
    /// </summary>
    public bool Fits(UnitBlueprint bp, Vector2 p)
    {
        if (bp.Movement.Layer == MotionLayer.Air) return true;
        var g = GridFor(bp);
        return g != null && g.Fits(p, ClearanceOf(bp));
    }

    /// <summary>(11a-2) A save from before <see cref="NavFormat"/> existed: the migrated object holds the field
    /// initializer's (current) value, so mark it as an old format and let <see cref="AfterLoad"/> rebuild the nav state.</summary>
    public void OnMigrated(Persistence.SaveMigrationInfo info)
    {
        if (!info.Has(nameof(NavFormat))) NavFormat = 0;
    }

    /// <summary>Drops a nav state of another format (an old save) so it is rebuilt lazily; units re-path.</summary>
    public void AfterLoad()
    {
        if (NavFormat == CurrentNavFormat && _grids.Length == GridSlots && _graphs.Length == GridSlots * GraphStride
            && (_occSnapshot == null || Array.TrueForAll(_grids, g => g == null || g.MaxReq == NavGrid.MaxClearance)))
            return;
        ResetNav();
    }

    /// <summary>Forgets every grid, graph and path (rebuilt on the next PathSystem run). SEQUENTIAL.</summary>
    internal void ResetNav()
    {
        NavFormat = CurrentNavFormat;
        _grids = new NavGrid?[GridSlots];
        _graphs = new NavGraph?[GridSlots * GraphStride];
        _occSnapshot = null;
        _slopeClasses = null; _slopeGridsUsed = null; _bpSlope = null;
        _fields.Clear();
        var u = _w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            u.Move[i].PathId = -1;
            u.Move[i].PathIndex = 0;
        }
    }
}

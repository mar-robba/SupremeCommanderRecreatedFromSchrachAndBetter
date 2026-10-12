using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Input;

/// <summary>Why a structure cannot be placed (None = OK).</summary>
public enum PlacementError { None, OutOfMap, Blocked, Planned, Terrain, NeedsMassDeposit, NeedsHydroDeposit }

/// <summary>
/// CLIENT-SIDE mirror of the sim's placement rules (<see cref="OccupancyGrid.CanPlace"/>), rebuilt from the
/// current snapshot: structures in the snapshot block their footprint, and so do the queued Build orders of
/// the local army (planned buildings). Terrain is immutable and read directly. The live OccupancyGrid is
/// never touched (it belongs to the sim thread).
/// </summary>
public sealed class PlacementGrid
{
    public const byte Free = 0, Structure = 1, Planned = 2;

    private readonly Terrain _t;
    private readonly BlueprintDb _db;
    private readonly byte[] _cells;
    private SimSnapshot? _builtFrom;
    private int _builtTick = -1;

    public PlacementGrid(Terrain terrain, BlueprintDb db)
    {
        _t = terrain;
        _db = db;
        _cells = new byte[terrain.Width * terrain.Height];
    }

    public byte At(int cx, int cz) => _t.CellInBounds(cx, cz) ? _cells[cz * _t.Width + cx] : Structure;

    /// <summary>Rebuilds the occupancy mirror if the snapshot changed.</summary>
    public void Refresh(SimSnapshot snap, int localArmy)
    {
        if (ReferenceEquals(snap, _builtFrom) && snap.Tick == _builtTick) return;
        _builtFrom = snap;
        _builtTick = snap.Tick;
        Array.Clear(_cells);
        for (int k = 0; k < snap.UnitCount; k++)
        {
            ref var v = ref snap.Units[k];
            var bp = _db[v.Bp];
            if (bp.IsStructure) Stamp(bp, new Vector2(v.Pos.X, v.Pos.Z), Structure);
            if (v.Army != localArmy) continue;
            for (int o = 0; o < v.OrderCount; o++)
            {
                ref var ov = ref snap.Orders[v.OrderStart + o];
                if (ov.Type != OrderType.Build || (uint)ov.BlueprintIndex >= (uint)_db.Count) continue;
                Stamp(_db[ov.BlueprintIndex], new Vector2(ov.Pos.X, ov.Pos.Z), Planned);
            }
        }
    }

    private void Stamp(UnitBlueprint bp, Vector2 pos, byte value)
    {
        var (x0, z0, x1, z1) = OccupancyGrid.Footprint(bp, pos);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            if (!_t.CellInBounds(x, z)) continue;
            ref byte c = ref _cells[z * _t.Width + x];
            if (c == Free || value == Structure) c = value;
        }
    }

    /// <summary>State of one footprint cell for drawing: OK or the first error found for that cell.</summary>
    public PlacementError CellError(UnitBlueprint bp, int x, int z)
    {
        if (!_t.CellInBounds(x, z)) return PlacementError.OutOfMap;
        byte c = _cells[z * _t.Width + x];
        if (c == Structure) return PlacementError.Blocked;
        if (c == Planned) return PlacementError.Planned;
        var p = _t.PassAt(x, z);
        bool naval = bp.HasAny(UnitCategory.Naval);
        if (naval ? (p & PassFlags.WaterSurface) == 0 : (p & PassFlags.Land) == 0) return PlacementError.Terrain;
        return PlacementError.None;
    }

    /// <summary>Same rules as OccupancyGrid.CanPlace (plus planned buildings), against the mirror.</summary>
    public PlacementError Check(UnitBlueprint bp, Vector2 pos)
    {
        var (x0, z0, x1, z1) = OccupancyGrid.Footprint(bp, pos);
        var worst = PlacementError.None;
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            var e = CellError(bp, x, z);
            if (e != PlacementError.None && (worst == PlacementError.None || e < worst)) worst = e;
        }
        if (worst != PlacementError.None) return worst;
        if (bp.Economy.RequiresMassDeposit && !IgnoreDeposits && !NearDeposit(pos, DepositType.Mass)) return PlacementError.NeedsMassDeposit;
        if (bp.Economy.RequiresHydroDeposit && !IgnoreDeposits && !NearDeposit(pos, DepositType.Hydrocarbon)) return PlacementError.NeedsHydroDeposit;
        return PlacementError.None;
    }

    private bool NearDeposit(Vector2 pos, DepositType type)
    {
        foreach (var d in _t.Deposits)
            if (d.Type == type && Vector2.DistanceSquared(d.Pos, pos) < 1.0f) return true;
        return false;
    }

    /// <summary>Nearest deposit of the type within maxDist of p, or null.</summary>
    public Deposit? NearestDeposit(Vector2 p, DepositType type, float maxDist)
    {
        Deposit? best = null;
        float bestD = maxDist * maxDist;
        foreach (var d in _t.Deposits)
        {
            if (d.Type != type) continue;
            float dd = Vector2.DistanceSquared(d.Pos, p);
            if (dd < bestD) { bestD = dd; best = d; }
        }
        return best;
    }

    /// <summary>Sandbox "build anywhere" for the local army: extractors don't need a deposit (set by SandboxController).</summary>
    public static bool IgnoreDeposits;

    public static string Describe(PlacementError e) => e switch
    {
        PlacementError.OutOfMap => "Outside the map",
        PlacementError.Blocked => "Blocked by a structure",
        PlacementError.Planned => "Overlaps a planned building",
        PlacementError.Terrain => "Unbuildable terrain (slope or water)",
        PlacementError.NeedsMassDeposit => "Must be placed on a mass deposit",
        PlacementError.NeedsHydroDeposit => "Must be placed on a hydrocarbon deposit",
        _ => "",
    };
}

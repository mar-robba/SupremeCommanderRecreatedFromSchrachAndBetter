using System.Numerics;
using Raylib_cs;
using Rts.Client.Audio;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Input;

/// <summary>
/// Structure placement mode (SupCom-like): a ghost footprint follows the cursor, snapped to the cell grid like
/// <see cref="OccupancyGrid.SnapStructure"/>; extractors snap to the nearest deposit. Green/red validity is
/// computed client-side by <see cref="PlacementGrid"/>. Left click issues a Build order to the selected builders
/// (Shift = queue and stay in placement mode while Shift is held), right click / Esc cancels, R rotates.
/// </summary>
public sealed class BuildPlacement
{
    private readonly GameContext _ctx;
    public readonly PlacementGrid Grid;

    public int BpIndex { get; private set; } = -1;
    public bool Active => BpIndex >= 0;
    public float Heading;
    /// <summary>Snapped structure center of the ghost (valid when HasPos).</summary>
    public Vector2 Pos;
    public bool HasPos;
    public PlacementError Error;
    private bool _placedWithShift;
    private double _rejectFlashUntil;

    public BuildPlacement(GameContext ctx)
    {
        _ctx = ctx;
        Grid = new PlacementGrid(ctx.Terrain, ctx.Db);
    }

    public UnitBlueprint? Blueprint => Active ? _ctx.Db[BpIndex] : null;

    public void Begin(int bpIndex)
    {
        if ((uint)bpIndex >= (uint)_ctx.Db.Count) return; // structures, and mobile units built in place (Fatboy)
        if (BpIndex != bpIndex) Heading = DefaultHeading();
        BpIndex = bpIndex;
        HasPos = false;
        _placedWithShift = false;
    }

    public void Cancel()
    {
        BpIndex = -1;
        HasPos = false;
        _placedWithShift = false;
    }

    /// <summary>Structures face the map center by default (90-degree steps).</summary>
    private float DefaultHeading()
    {
        var t = _ctx.Terrain;
        var start = t.StartPositions.Length > _ctx.LocalArmy ? t.StartPositions[_ctx.LocalArmy] : new Vector2(t.Width, t.Height) * 0.5f;
        var dir = new Vector2(t.Width * 0.5f, t.Height * 0.5f) - start;
        float h = dir.LengthSquared() > 1 ? MathF.Atan2(dir.Y, dir.X) : 0;
        return MathF.Round(h / (MathF.PI * 0.5f)) * (MathF.PI * 0.5f);
    }

    /// <summary>Builders in the selection able to build the current blueprint.</summary>
    public UnitHandle[] Builders(IReadOnlyList<UnitHandle> selected, SimSnapshot snap)
    {
        var list = new List<UnitHandle>();
        foreach (var h in selected)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || v.Army != _ctx.LocalArmy) continue;
            var bp = _ctx.Db[v.Bp];
            if (bp.IsMobile && Array.IndexOf(bp.BuildIndices, BpIndex) >= 0) list.Add(h);
        }
        return list.ToArray();
    }

    /// <summary>Per-frame input while active. Called by SelectionController instead of the normal click handling.</summary>
    public void Update(bool mouseOverUi, IReadOnlyList<UnitHandle> selected, bool shift)
    {
        if (!Active) return;
        var snap = _ctx.Snapshots.Current;
        var builders = Builders(selected, snap);
        if (builders.Length == 0) { Cancel(); return; }

        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { Cancel(); return; }
        if (Raylib.IsKeyPressed(KeyboardKey.R)) Heading = DMath.WrapAngle(Heading + MathF.PI * 0.5f);
        // SupCom: after shift-placing, releasing Shift leaves placement mode.
        if (_placedWithShift && !shift) { Cancel(); return; }

        Grid.Refresh(snap, _ctx.LocalArmy);
        var bp = _ctx.Db[BpIndex];
        if (_ctx.PickGround(Raylib.GetMousePosition()) is Vector3 g)
        {
            Pos = SnapToCursor(bp, new Vector2(g.X, g.Z));
            HasPos = true;
            Error = Grid.Check(bp, Pos);
        }
        else HasPos = false;

        if (mouseOverUi) return;
        if (Raylib.IsMouseButtonPressed(MouseButton.Right)) { Cancel(); return; }
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && HasPos)
        {
            if (Error != PlacementError.None) { _rejectFlashUntil = Raylib.GetTime() + 0.35; UiSounds.Error(); return; }
            _ctx.Runner.Enqueue(new IssueOrderCommand(_ctx.LocalArmy, builders, Order.BuildAt(BpIndex, Pos, Heading), shift));
            _ctx.Feedback.AddClickMarker(new Vector3(Pos.X, _ctx.Terrain.GetSurfaceHeight(Pos), Pos.Y), OrderType.Build);
            UiSounds.Confirm();
            if (shift) _placedWithShift = true;
            else Cancel();
        }
    }

    /// <summary>Grid snap; deposit-bound structures snap to the nearest matching deposit near the cursor.</summary>
    public Vector2 SnapToCursor(UnitBlueprint bp, Vector2 cursor)
    {
        if (bp.Economy.RequiresMassDeposit || bp.Economy.RequiresHydroDeposit)
        {
            var type = bp.Economy.RequiresMassDeposit ? DepositType.Mass : DepositType.Hydrocarbon;
            float radius = MathF.Max(5f, MathF.Max(bp.FootprintX, bp.FootprintZ) * 1.5f) + _ctx.Camera.Distance * 0.03f;
            if (Grid.NearestDeposit(cursor, type, radius) is Deposit d) return OccupancyGrid.SnapStructure(bp, d.Pos);
        }
        return OccupancyGrid.SnapStructure(bp, cursor);
    }

    // ------------------------------------------------------------------ drawing (inside BeginMode3D)

    private static readonly Color OkFill = new(60, 230, 90, 120), OkLine = new(110, 255, 140, 230);
    private static readonly Color BadFill = new(240, 60, 50, 130), BadLine = new(255, 90, 80, 240);
    private static readonly Color PlannedFill = new(240, 210, 60, 60), PlannedLine = new(250, 220, 90, 200);

    /// <summary>Ghost of the structure being placed, plus queued build ghosts of the selected builders.</summary>
    public void Draw3D(IReadOnlyList<UnitHandle> selected)
    {
        var snap = _ctx.Snapshots.Current;
        // GL state changes apply to the whole pending batch: flush before toggling.
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();
        DrawQueuedGhosts(selected, snap);
        if (Active && HasPos)
        {
            var bp = _ctx.Db[BpIndex];
            bool ok = Error == PlacementError.None;
            bool flash = !ok && Raylib.GetTime() < _rejectFlashUntil && ((int)(Raylib.GetTime() * 16) & 1) == 0;
            DrawRangeRings(bp, Pos, ok ? new Color(255, 120, 100, 160) : new Color(255, 120, 100, 90));
            // The ghost is drawn on top of everything so it stays visible inside/behind other structures.
            Rlgl.DrawRenderBatchActive();
            Rlgl.DisableDepthTest();
            DrawFootprint(bp, Pos, true);
            var fill = ok ? new Color(120, 240, 140, 60) : new Color(250, 90, 80, flash ? 150 : 70);
            var line = ok ? OkLine : BadLine;
            DrawGhostBox(bp, Pos, Heading, fill, line);
            Rlgl.DrawRenderBatchActive();
            Rlgl.EnableDepthTest();
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
    }

    private void DrawQueuedGhosts(IReadOnlyList<UnitHandle> selected, SimSnapshot snap)
    {
        if (selected.Count == 0) return;
        int shown = 0;
        foreach (var h in selected)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v)) continue;
            for (int o = 0; o < v.OrderCount && shown < 200; o++)
            {
                ref var ov = ref snap.Orders[v.OrderStart + o];
                if (ov.Type != OrderType.Build || (uint)ov.BlueprintIndex >= (uint)_ctx.Db.Count) continue;
                var bp = _ctx.Db[ov.BlueprintIndex];
                var p = new Vector2(ov.Pos.X, ov.Pos.Z);
                if (StructureExistsAt(snap, ov.BlueprintIndex, p)) continue;
                DrawFootprintOutline(bp, p, PlannedLine);
                DrawGhostBox(bp, p, 0, PlannedFill, PlannedLine, wireOnly: true);
                shown++;
            }
        }
    }

    private bool StructureExistsAt(SimSnapshot snap, int bpIndex, Vector2 p)
    {
        // Construction already started: the real structure is drawn by the unit renderer.
        if (_ctx.Db[bpIndex].IsMobile)
        {
            // Mobile units built in place (Fatboy) are not in the grid: look for the unfinished unit at the site.
            for (int i = 0; i < snap.UnitCount; i++)
            {
                ref var u = ref snap.Units[i];
                if (u.Bp == bpIndex && (u.Flags & UnitFlags.UnderConstruction) != 0
                    && Vector2.DistanceSquared(new Vector2(u.Pos.X, u.Pos.Z), p) < 4f) return true;
            }
            return false;
        }
        var (x0, z0, _, _) = OccupancyGrid.Footprint(_ctx.Db[bpIndex], p);
        Grid.Refresh(snap, _ctx.LocalArmy);
        return Grid.At(x0, z0) == PlacementGrid.Structure;
    }

    private float SurfaceY(float x, float z, bool naval) =>
        naval ? MathF.Max(_ctx.Terrain.VertexHeight((int)x, (int)z), _ctx.Terrain.WaterLevel) : _ctx.Terrain.VertexHeight((int)x, (int)z);

    private void DrawFootprint(UnitBlueprint bp, Vector2 pos, bool perCell)
    {
        var (x0, z0, x1, z1) = OccupancyGrid.Footprint(bp, pos);
        bool naval = bp.HasAny(UnitCategory.Naval);
        const float lift = 0.07f;
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            var e = Grid.CellError(bp, x, z);
            if (Error is PlacementError.NeedsMassDeposit or PlacementError.NeedsHydroDeposit) e = Error;
            var col = e == PlacementError.None ? OkFill : e == PlacementError.Planned ? new Color(240, 160, 40, 120) : BadFill;
            var a = new Vector3(x, SurfaceY(x, z, naval) + lift, z);
            var b = new Vector3(x + 1, SurfaceY(x + 1, z, naval) + lift, z);
            var c = new Vector3(x, SurfaceY(x, z + 1, naval) + lift, z + 1);
            var d = new Vector3(x + 1, SurfaceY(x + 1, z + 1, naval) + lift, z + 1);
            // Slightly inset cells so the grid is visible.
            const float k = 0.06f;
            a += new Vector3(k, 0, k); b += new Vector3(-k, 0, k); c += new Vector3(k, 0, -k); d += new Vector3(-k, 0, -k);
            Raylib.DrawTriangle3D(a, c, b, col);
            Raylib.DrawTriangle3D(b, c, d, col);
        }
        DrawFootprintOutline(bp, pos, Error == PlacementError.None ? OkLine : BadLine);
    }

    private void DrawFootprintOutline(UnitBlueprint bp, Vector2 pos, Color col)
    {
        var (x0, z0, x1, z1) = OccupancyGrid.Footprint(bp, pos);
        bool naval = bp.HasAny(UnitCategory.Naval);
        const float lift = 0.12f;
        Vector3 P(int x, int z) => new(x, SurfaceY(x, z, naval) + lift, z);
        for (int x = x0; x <= x1; x++)
        {
            Raylib.DrawLine3D(P(x, z0), P(x + 1, z0), col);
            Raylib.DrawLine3D(P(x, z1 + 1), P(x + 1, z1 + 1), col);
        }
        for (int z = z0; z <= z1; z++)
        {
            Raylib.DrawLine3D(P(x0, z), P(x0, z + 1), col);
            Raylib.DrawLine3D(P(x1 + 1, z), P(x1 + 1, z + 1), col);
        }
    }

    private void DrawGhostBox(UnitBlueprint bp, Vector2 pos, float heading, Color fill, Color line, bool wireOnly = false)
    {
        float y = bp.HasAny(UnitCategory.Naval) ? _ctx.Terrain.GetSurfaceHeight(pos) : _ctx.Terrain.GetHeight(pos);
        var size = bp.Visual.Size;
        Rlgl.PushMatrix();
        Rlgl.Translatef(pos.X, y + size.Y * 0.5f, pos.Y);
        Rlgl.Rotatef((MathF.PI * 0.5f - heading) * 180f / MathF.PI, 0, 1, 0);
        if (!wireOnly) Raylib.DrawCubeV(Vector3.Zero, size, fill);
        Raylib.DrawCubeWiresV(Vector3.Zero, size, line);
        if (!wireOnly)
        {
            // Facing marker (front = +Z in model space).
            var tip = new Vector3(0, -size.Y * 0.5f + 0.1f, size.Z * 0.5f + 0.9f);
            Raylib.DrawLine3D(new Vector3(0, -size.Y * 0.5f + 0.1f, size.Z * 0.5f), tip, line);
            Raylib.DrawLine3D(tip, tip + new Vector3(-0.4f, 0, -0.4f), line);
            Raylib.DrawLine3D(tip, tip + new Vector3(0.4f, 0, -0.4f), line);
        }
        Rlgl.PopMatrix();
    }

    private void DrawRangeRings(UnitBlueprint bp, Vector2 pos, Color col)
    {
        float y = _ctx.Terrain.GetSurfaceHeight(pos) + 0.3f;
        foreach (var w in bp.Weapons)
            if (w.Range > 0) Raylib.DrawCircle3D(new Vector3(pos.X, y, pos.Y), w.Range, Vector3.UnitX, 90, col);
        if (bp.Intel.RadarRadius > 0)
            Raylib.DrawCircle3D(new Vector3(pos.X, y, pos.Y), bp.Intel.RadarRadius, Vector3.UnitX, 90, new Color(90, 200, 230, 150));
        if (bp.Intel.SonarRadius > 0)
        {
            // Sonar: deeper blue, drawn twice (slightly apart) so it reads as a different ring from the radar.
            var sonar = new Color(70, 120, 255, 170);
            Raylib.DrawCircle3D(new Vector3(pos.X, y, pos.Y), bp.Intel.SonarRadius, Vector3.UnitX, 90, sonar);
            Raylib.DrawCircle3D(new Vector3(pos.X, y, pos.Y), bp.Intel.SonarRadius - 0.35f, Vector3.UnitX, 90, sonar with { A = 90 });
        }
    }

    public string Hint()
    {
        if (!Active) return "";
        var bp = _ctx.Db[BpIndex];
        string s = $"Place {bp.Name}";
        if (HasPos && Error != PlacementError.None) s += "  —  " + PlacementGrid.Describe(Error);
        return s;
    }
}

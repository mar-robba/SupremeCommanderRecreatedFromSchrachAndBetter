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
/// Shift + left-drag lays a straight line of copies from the press point to the cursor (<see cref="PlacementLine"/>):
/// blocked ghosts are drawn red and skipped, the release queues one Build order per valid ghost, in line order.
/// Adjacency preview (<see cref="AdjacencyPreview"/>): touching own structures that would give or get a bonus are
/// outlined and linked to the ghost, with a short summary next to it (<see cref="DrawOverlay"/>).
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
    /// <summary>Adjacency bonuses at the ghost position (null = none / no position).</summary>
    public AdjacencyPreviewResult? Adjacency;

    /// <summary>Shift+drag line: snapped center where the drag started (null = no line being dragged).</summary>
    private Vector2? _lineAnchor;
    /// <summary>Screenshot hook (<see cref="DebugHoldLine"/>): keep the line open instead of committing it.</summary>
    private bool _lineHold;
    /// <summary>Ghost centers of the line being dragged (anchor first) and their placement errors.</summary>
    public readonly List<Vector2> LinePos = new();
    public readonly List<PlacementError> LineErr = new();
    public bool LineActive => Active && _lineAnchor.HasValue;

    public BuildPlacement(GameContext ctx)
    {
        _ctx = ctx;
        Grid = new PlacementGrid(ctx.Terrain, ctx.Db);
    }

    public UnitBlueprint? Blueprint => Active ? _ctx.Db[BpIndex] : null;

    public void Begin(int bpIndex)
    {
        if ((uint)bpIndex >= (uint)_ctx.Db.Count || !_ctx.Db[bpIndex].IsStructure) return;
        if (BpIndex != bpIndex) Heading = DefaultHeading();
        BpIndex = bpIndex;
        HasPos = false;
        _placedWithShift = false;
        _lineAnchor = null;
    }

    public void Cancel()
    {
        BpIndex = -1;
        HasPos = false;
        _placedWithShift = false;
        _lineAnchor = null;
        _lineHold = false;
    }

    /// <summary>Screenshot helper (--ui-place id@x,z): shows a line dragged from the world point to the cursor.</summary>
    public void DebugHoldLine(Vector2 worldAnchor)
    {
        if (!Active || !CanLine(_ctx.Db[BpIndex])) return;
        _lineAnchor = SnapToCursor(_ctx.Db[BpIndex], worldAnchor);
        _lineHold = true;
    }

    /// <summary>Deposit-bound structures (extractors) are placed one by one.</summary>
    private static bool CanLine(UnitBlueprint bp) => !bp.Economy.RequiresMassDeposit && !bp.Economy.RequiresHydroDeposit;

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
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || !_ctx.Controls(v.Army)) continue;
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
        if (_placedWithShift && !shift && _lineAnchor == null) { Cancel(); return; }

        Grid.Refresh(snap, _ctx.LocalArmy);
        var bp = _ctx.Db[BpIndex];
        if (_ctx.PickGround(Raylib.GetMousePosition()) is Vector3 g)
        {
            Pos = SnapToCursor(bp, new Vector2(g.X, g.Z));
            HasPos = true;
            Error = Grid.Check(bp, Pos);
            Adjacency = ComputeAdjacency(bp, Pos, snap, ArmyCommands.PrimaryArmy(_ctx, builders));
        }
        else { HasPos = false; Adjacency = null; }

        if (_lineAnchor is Vector2 anchor)
        {
            if (HasPos) LayoutLine(bp, anchor);
            if (_lineHold) return;
            // Right click drops the line (placement mode stays); releasing the left button anywhere commits it.
            if (Raylib.IsMouseButtonPressed(MouseButton.Right)) { _lineAnchor = null; return; }
            if (!Raylib.IsMouseButtonDown(MouseButton.Left)) CommitLine(builders);
            return;
        }

        if (mouseOverUi) return;
        if (Raylib.IsMouseButtonPressed(MouseButton.Right)) { Cancel(); return; }
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && HasPos)
        {
            // Shift: the press starts a line; a release without dragging places the single ghost (= shift-click).
            if (shift && CanLine(bp)) { _lineAnchor = Pos; LayoutLine(bp, Pos); return; }
            if (Error != PlacementError.None) { _rejectFlashUntil = Raylib.GetTime() + 0.35; UiSounds.Error(); return; }
            var order = Order.BuildAt(BpIndex, Pos, Heading);
            ArmyCommands.Send(_ctx, builders, (army, units) => new IssueOrderCommand(army, units, order, shift));
            _ctx.Feedback.AddClickMarker(new Vector3(Pos.X, _ctx.Terrain.GetSurfaceHeight(Pos), Pos.Y), OrderType.Build);
            UiSounds.Confirm();
            if (shift) _placedWithShift = true;
            else Cancel();
        }
    }

    private void LayoutLine(UnitBlueprint bp, Vector2 anchor)
    {
        PlacementLine.Layout(anchor, Pos, bp.FootprintX, bp.FootprintZ, LinePos);
        LineErr.Clear();
        foreach (var p in LinePos) LineErr.Add(Grid.Check(bp, p));
    }

    /// <summary>One queued Build order per valid ghost, in line order (anchor first), like successive shift-clicks.</summary>
    private void CommitLine(UnitHandle[] builders)
    {
        _lineAnchor = null;
        int placed = 0;
        for (int i = 0; i < LinePos.Count; i++)
        {
            if (LineErr[i] != PlacementError.None) continue;
            var p = LinePos[i];
            var order = Order.BuildAt(BpIndex, p, Heading);
            ArmyCommands.Send(_ctx, builders, (army, units) => new IssueOrderCommand(army, units, order, true));
            _ctx.Feedback.AddClickMarker(new Vector3(p.X, _ctx.Terrain.GetSurfaceHeight(p), p.Y), OrderType.Build);
            placed++;
        }
        LinePos.Clear();
        LineErr.Clear();
        if (placed == 0) { _rejectFlashUntil = Raylib.GetTime() + 0.35; UiSounds.Error(); return; }
        UiSounds.Confirm();
        _placedWithShift = true; // releasing Shift leaves placement mode, as after a shift-click
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

    /// <summary>Adjacency preview against the army's structures (complete or being built) near the ghost.</summary>
    private AdjacencyPreviewResult? ComputeAdjacency(UnitBlueprint bp, Vector2 pos, SimSnapshot snap, int army)
    {
        if (!Rts.Sim.Systems.AdjacencyRules.Participates(bp)) return null;
        var near = new List<(int, UnitBlueprint, Vector2)>();
        for (int k = 0; k < snap.UnitCount; k++)
        {
            ref var v = ref snap.Units[k];
            if (v.Army != army) continue;
            var ob = _ctx.Db[v.Bp];
            if (!ob.IsStructure) continue;
            var p = new Vector2(v.Pos.X, v.Pos.Z);
            float rx = (bp.FootprintX + ob.FootprintX) * 0.5f + 1, rz = (bp.FootprintZ + ob.FootprintZ) * 0.5f + 1;
            if (MathF.Abs(p.X - pos.X) > rx || MathF.Abs(p.Y - pos.Y) > rz) continue;
            near.Add((k, ob, p));
        }
        var r = AdjacencyPreview.Compute(bp, pos, near);
        return r.IsEmpty ? null : r;
    }

    private static readonly Color AdjReceive = new(120, 255, 150, 235), AdjGive = new(255, 215, 90, 235), AdjBoth = new(120, 230, 255, 235);

    private static Color AdjColor(in AdjacencyLink l) =>
        !l.Gives.IsZero && !l.Receives.IsZero ? AdjBoth : l.Receives.IsZero ? AdjGive : AdjReceive;

    /// <summary>Outlines touching structures that exchange a bonus with the ghost and links them to it (no depth test).</summary>
    private void DrawAdjacency3D(UnitBlueprint bp)
    {
        if (Adjacency is not { } adj) return;
        float t = (float)Raylib.GetTime();
        float y0 = _ctx.Terrain.GetSurfaceHeight(Pos) + bp.Visual.Size.Y * 0.35f;
        foreach (var l in adj.Links)
        {
            var col = AdjColor(l);
            DrawFootprintOutline(l.Bp, l.Pos, col);
            float y1 = _ctx.Terrain.GetSurfaceHeight(l.Pos) + l.Bp.Visual.Size.Y * 0.35f;
            var a = new Vector3(Pos.X, y0, Pos.Y);
            var b = new Vector3(l.Pos.X, y1, l.Pos.Y);
            Raylib.DrawLine3D(a, b, col);
            // A pulse travels the way the bonus flows (both ways: alternating).
            float k = (t * 1.2f) % 1f;
            bool toGhost = l.Gives.IsZero || (!l.Receives.IsZero && ((int)(t * 1.2f) & 1) == 1);
            var dot = toGhost ? Vector3.Lerp(b, a, k) : Vector3.Lerp(a, b, k);
            Raylib.DrawSphereEx(dot, 0.22f, 6, 6, col);
        }
    }

    /// <summary>2D summary next to the ghost (call after EndMode3D).</summary>
    public void DrawOverlay()
    {
        if (!Active || !HasPos || LineActive || Adjacency is not { } adj) return;
        var lines = AdjacencyPreview.Summary(adj);
        if (lines.Count == 0) return;
        var bp = _ctx.Db[BpIndex];
        var anchor = new Vector3(Pos.X, _ctx.Terrain.GetSurfaceHeight(Pos) + bp.Visual.Size.Y, Pos.Y);
        var sp = Raylib.GetWorldToScreen(anchor, _ctx.Camera.Camera);
        int fs = UI.Ui.F(12);
        float x = sp.X + UI.Ui.P(14), y = sp.Y - lines.Count * (fs + 2) - UI.Ui.P(4);
        foreach (var line in lines)
        {
            UI.Ui.TextShadow(line, x, y, fs, line.StartsWith("Adjacency") ? AdjReceive : AdjGive, true);
            y += fs + 2;
        }
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
        if (LineActive) DrawLine3D();
        else if (Active && HasPos)
        {
            var bp = _ctx.Db[BpIndex];
            bool ok = Error == PlacementError.None;
            bool flash = !ok && Raylib.GetTime() < _rejectFlashUntil && ((int)(Raylib.GetTime() * 16) & 1) == 0;
            DrawRangeRings(bp, Pos, ok ? new Color(255, 120, 100, 160) : new Color(255, 120, 100, 90));
            // The ghost is drawn on top of everything so it stays visible inside/behind other structures.
            Rlgl.DrawRenderBatchActive();
            Rlgl.DisableDepthTest();
            DrawFootprint(bp, Pos, Error);
            var fill = ok ? new Color(120, 240, 140, 60) : new Color(250, 90, 80, flash ? 150 : 70);
            var line = ok ? OkLine : BadLine;
            DrawGhostBox(bp, Pos, Heading, fill, line);
            DrawAdjacency3D(bp);
            Rlgl.DrawRenderBatchActive();
            Rlgl.EnableDepthTest();
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
    }

    /// <summary>Ghosts of the Shift+drag line: green = will be queued, red = blocked (skipped).</summary>
    private void DrawLine3D()
    {
        var bp = _ctx.Db[BpIndex];
        bool flash = Raylib.GetTime() < _rejectFlashUntil && ((int)(Raylib.GetTime() * 16) & 1) == 0;
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthTest();
        for (int i = 0; i < LinePos.Count; i++)
        {
            bool ok = LineErr[i] == PlacementError.None;
            DrawFootprint(bp, LinePos[i], LineErr[i]);
            var fill = ok ? new Color(120, 240, 140, 60) : new Color(250, 90, 80, flash ? 150 : 70);
            DrawGhostBox(bp, LinePos[i], Heading, fill, ok ? OkLine : BadLine);
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthTest();
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
        var (x0, z0, _, _) = OccupancyGrid.Footprint(_ctx.Db[bpIndex], p);
        Grid.Refresh(snap, _ctx.LocalArmy);
        return Grid.At(x0, z0) == PlacementGrid.Structure;
    }

    private float SurfaceY(float x, float z, bool naval) =>
        naval ? MathF.Max(_ctx.Terrain.VertexHeight((int)x, (int)z), _ctx.Terrain.WaterLevel) : _ctx.Terrain.VertexHeight((int)x, (int)z);

    private void DrawFootprint(UnitBlueprint bp, Vector2 pos, PlacementError err)
    {
        var (x0, z0, x1, z1) = OccupancyGrid.Footprint(bp, pos);
        bool naval = bp.HasAny(UnitCategory.Naval);
        const float lift = 0.07f;
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            var e = Grid.CellError(bp, x, z);
            if (err is PlacementError.NeedsMassDeposit or PlacementError.NeedsHydroDeposit) e = err;
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
        DrawFootprintOutline(bp, pos, err == PlacementError.None ? OkLine : BadLine);
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
        if (LineActive) return LineHint() is { } lh ? $"Place {bp.Name}: {lh}" : $"Place {bp.Name}";
        string s = $"Place {bp.Name}";
        if (HasPos && Error != PlacementError.None) s += "  —  " + PlacementGrid.Describe(Error);
        return s;
    }

    /// <summary>Cursor text while dragging a line: "5 to build, 2 blocked (skipped)" (null = no line).</summary>
    public string? LineHint()
    {
        if (!LineActive || LinePos.Count == 0) return null;
        int bad = 0;
        foreach (var e in LineErr) if (e != PlacementError.None) bad++;
        int ok = LinePos.Count - bad;
        string s = ok == 0 ? "nothing to build" : $"{ok} to build";
        if (bad > 0) s += $", {bad} blocked (skipped)";
        return s;
    }
}
